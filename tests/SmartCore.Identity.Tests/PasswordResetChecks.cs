using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using SmartCore.Identity;

internal static class PasswordResetChecks
{
    public static async Task<int> Run(Database db,Secrets secrets,TimeProvider clock,Action<TimeSpan> advance)
    {
        var count=0;
        void Check(bool value,string label) {if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);count++;}
        async Task Denied(Func<Task> work) {try {await work();}catch(ApiError e) when(e.Code=="UNAUTHORIZED"){return;}throw new Exception("Expected uniform UNAUTHORIZED");}
        await using var sql=await db.Source.OpenConnectionAsync();
        const string client="reset-tests",password="original password reset fixture",replacement="new password reset fixture";
        var gate=new PostgresAuthenticationIssuanceGate();var access=new AccessTokens(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),clock);
        var auth=new AuthenticationService(db,secrets,clock,gate,access);var enroll=new RecoveryEnrollment(db,secrets,clock,gate,access);
        var starts=new PasswordResetInitiation(db,secrets,clock,gate);var credential=new CredentialChanges(db,secrets,clock);
        var changes=new PasswordChanges(db,secrets,clock,gate,access,credential);var complete=new PasswordResetCompletion(db,secrets,clock,gate,changes);
        async Task<(string Contact,Guid Person,string Token,string Recovery)> Ready(bool phone=false)
        {
            var contact=phone?"+989"+RandomNumberGenerator.GetInt32(100000000,999999999):Guid.NewGuid()+"@example.test";
            var registration=new RegistrationService(db,secrets,clock);var provisioning=new Provisioning(db,secrets,clock);var binding=Secrets.Token();
            var start=await registration.Start(new(phone?null:contact,phone?contact:null,password,"Reset",binding),Secrets.Token());
            var delivery=(await sql.One("SELECT * FROM delivery_outbox WHERE verification_id=@id",("id",start.VerificationSessionId)))!;
            var code=secrets.Open(delivery.Get<byte[]>("sealed_code"),"delivery:"+delivery.Get<Guid>("id"));
            var verified=await registration.Verify(new(start.VerificationSessionId,code,binding));
            await provisioning.EnsureCredential(verified.Result.RegistrationId);await provisioning.MarkReady(verified.Result.RegistrationId);await provisioning.Acknowledge(verified.Result.RegistrationId);
            var login=await auth.Login(new(phone?null:contact,phone?contact:null,password),client);
            var recovery=await enroll.Enroll(login.Session.AccessToken,client,new(Guid.NewGuid(),password,true));
            return (contact,login.Person.PersonId,login.Session.AccessToken,recovery.RecoveryCode!);
        }
        async Task<CompletePasswordReset> Proof(string contact,string recovery,bool phone=false)
        {
            var binding=Secrets.Token();var start=await starts.Start(client,new(Guid.NewGuid(),phone?null:contact,phone?contact:null,binding));
            var delivery=(await sql.One("SELECT * FROM auth_reset_delivery WHERE challenge_id=@id",("id",start.ChallengeId)))!;
            var otp=secrets.Open(delivery.Get<byte[]>("sealed_code"),"reset-delivery:"+delivery.Get<Guid>("id"));
            return new(Guid.NewGuid(),start.ChallengeId,binding,otp,recovery,replacement);
        }
        var fixture=await Ready();var request=await Proof(fixture.Contact,fixture.Recovery);
        await Denied(async()=>{await complete.Accept(client,request with {Code=request.Code=="000000"?"111111":"000000"});});
        await Denied(async()=>{await complete.Accept(client,request with {RecoveryCode=new string('0',32)});});
        Check((await sql.One("SELECT failures FROM auth_reset_failures WHERE person_id=@id",("id",fixture.Person)))!.Get<int>("failures")==2
            && await sql.One("SELECT id FROM auth_credential_change_intents WHERE id=@id",("id",request.OperationId)) is null,
            "wrong OTP and wrong recovery code share a Person failure counter and never fence");
        var raced=await Task.WhenAll(complete.Accept(client,request),complete.Accept(client,request));
        Check(raced.All(r=>r.OperationId==request.OperationId && r.Stage=="Fenced")
            && (await sql.One("SELECT epoch FROM auth_issuance_state WHERE person_id=@id",("id",fixture.Person)))!.Get<long>("epoch")==1,
            "identical concurrent dual-factor reset admits one durable intent and advances epoch once");
        var reserved=(await sql.One("SELECT * FROM auth_recovery_codes WHERE person_id=@id",("id",fixture.Person)))!;
        Check(reserved.Has("verifier") && reserved.Get<Guid>("reserved_intent_id")==request.OperationId && !reserved.Has("consumed_at"),
            "reset acceptance reserves the one-time recovery code without consuming it before Credential outcome");
        await Denied(async()=>{await auth.Self(access.Read(fixture.Token,client));});
        await Denied(async()=>{await auth.Login(new(fixture.Contact,null,password),client);});
        Check((await sql.One("SELECT count(*) AS n FROM auth_sessions WHERE person_id=@id AND status='Active'",("id",fixture.Person)))!.Get<long>("n")==0,
            "verified reset closes old Sessions and issuance stays fenced until reconciliation");
        var intent=(await PasswordChanges.Load(sql,request.OperationId))!;
        await credential.Apply(intent); // Simulate lost response after Credential commit, before Session reconciliation.
        Check((await sql.One("SELECT reserved_intent_id FROM auth_recovery_codes WHERE person_id=@id",("id",fixture.Person)))!.Has("reserved_intent_id"),
            "a lost Credential response preserves the reset fence and reserved factor");
        var restarted=new PasswordChanges(db,secrets,clock,gate,access,new CredentialChanges(db,secrets,clock));
        await restarted.Process(request.OperationId);await restarted.Process(request.OperationId);
        Check((await complete.Accept(client,request)).Stage=="Reconciled"
            && !(await sql.One("SELECT verifier FROM auth_recovery_codes WHERE person_id=@id",("id",fixture.Person)))!.Has("verifier"),
            "restarted worker reconciles the receipt, consumes the recovery code and permits bounded lost-response replay");
        Check((await sql.One("SELECT count(*) AS n FROM auth_account_notifications WHERE operation_id=@id AND kind='PasswordResetCompleted'",("id",request.OperationId)))!.Get<long>("n")==1,
            "reset completion notification is committed once despite worker replay");
        var envelope=JsonDocument.Parse((await sql.One("SELECT payload::text AS payload FROM credential_change_outbox WHERE operation_id=@op",("op",intent.Get<Guid>("credential_operation_id"))))!.Get<string>("payload")).RootElement;
        Check(!envelope.TryGetProperty("SessionReference",out _) && envelope.GetProperty("ExecutionContext").GetProperty("RecoveryProofReference").GetGuid()==request.OperationId,
            "sessionless PasswordChanged references the accepted reset intent rather than a challenge or synthetic Session");
        PasswordChangedEvents.ValidateContext(envelope);
        var invalid=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(envelope.GetRawText())!;
        invalid["SessionReference"]=JsonSerializer.SerializeToElement(Guid.NewGuid());
        try {PasswordChangedEvents.ValidateContext(JsonSerializer.SerializeToElement(invalid));throw new Exception("Both contexts accepted");}catch(InvalidOperationException) { }
        invalid.Remove("SessionReference");invalid["ExecutionContext"]=JsonSerializer.SerializeToElement(new {CorrelationId=request.OperationId.ToString()});
        try {PasswordChangedEvents.ValidateContext(JsonSerializer.SerializeToElement(invalid));throw new Exception("Missing contexts accepted");}catch(InvalidOperationException) { }
        Check(true,"runtime PasswordChanged context guard rejects both contexts and neither context");
        var newLogin=await auth.Login(new(fixture.Contact,null,replacement),client);
        Check(newLogin.Person.RecoveryEnrollmentRequired,"first new-password login explicitly requires replacing the consumed recovery code");
        var newCode=await enroll.Enroll(newLogin.Session.AccessToken,client,new(Guid.NewGuid(),replacement,true));
        Check(newCode.Version==2 && !(await auth.Self(access.Read(newLogin.Session.AccessToken,client))).RecoveryEnrollmentRequired,
            "fresh post-reset Session can enroll a new one-time recovery code and complete onboarding");
        var limited=await Ready();var first=await Proof(limited.Contact,limited.Recovery);var second=await Proof(limited.Contact,limited.Recovery);
        for(var i=0;i<3;i++)await Denied(async()=>{await complete.Accept(client,first with {RecoveryCode=new string('0',32)});});
        for(var i=0;i<2;i++)await Denied(async()=>{await complete.Accept(client,second with {RecoveryCode=new string('0',32)});});
        var third=await Proof(limited.Contact,limited.Recovery);await Denied(async()=>{await complete.Accept(client,third);});
        Check((await sql.One("SELECT failures FROM auth_reset_failures WHERE person_id=@id",("id",limited.Person)))!.Get<int>("failures")==5,
            "new challenges do not reset the five-failure Person proof budget");
        var noGate=new ForbiddenGate();var deniedStart=new PasswordResetInitiation(db,secrets,clock,noGate);
        await deniedStart.Start(client,new(Guid.NewGuid(),limited.Contact,null,Secrets.Token()));
        await deniedStart.Start(client,new(Guid.NewGuid(),limited.Contact,null,Secrets.Token()));
        var unknown=await deniedStart.Start(client,new(Guid.NewGuid(),Guid.NewGuid()+"@example.test",null,Secrets.Token()));
        Check((await sql.One("SELECT count(*) AS n FROM auth_account_notifications WHERE person_id=@id AND kind='ResetRequestsLimited'",("id",limited.Person)))!.Get<long>("n")==1,
            "unknown and budget-denied initiation never acquire the Person gate and queue only one budget notice per window");
        advance(TimeSpan.FromMinutes(16));await restarted.Tick();
        Check(await sql.One("SELECT id FROM auth_reset_decoys WHERE id=@id",("id",unknown.ChallengeId)) is null
            && await sql.One("SELECT id FROM auth_reset_challenges WHERE id=@id",("id",third.ChallengeId)) is null
            && await sql.One("SELECT id FROM auth_reset_challenges WHERE id=@id",("id",request.ChallengeId)) is not null,
            "TTL cleanup removes decoys and unaccepted eligible challenges while preserving accepted reset evidence");
        await Denied(async()=>{await complete.Accept(client,request);});
        Check(true,"accepted reset replay cannot extend the original ten-minute proof deadline");
        var retry=await Proof(limited.Contact,limited.Recovery);await complete.Accept(client,retry);
        await changes.Recover(retry.OperationId,"ResolveNotApplied");
        Check((await sql.One("SELECT verifier,reserved_intent_id FROM auth_recovery_codes WHERE person_id=@id",("id",limited.Person)))!.Has("verifier")
            && !(await sql.One("SELECT reserved_intent_id FROM auth_recovery_codes WHERE person_id=@id",("id",limited.Person)))!.Has("reserved_intent_id")
            && (await auth.Login(new(limited.Contact,null,password),client)).Person.PersonId==limited.Person,
            "terminal NotApplied receipt releases the reserved code without changing the password");
        var phone=await Ready(true);var phoneProof=await Proof(phone.Contact,phone.Recovery,true);
        await complete.Accept(client,phoneProof);await changes.Process(phoneProof.OperationId);
        Check((await auth.Login(new(null,phone.Contact,replacement),client)).Person.RecoveryEnrollmentRequired,
            "telephone accounts require both proofs and use the same receipt-backed reset coordinator");
        var stale=await Ready();var staleProof=await Proof(stale.Contact,stale.Recovery);
        await enroll.Enroll(stale.Token,client,new(Guid.NewGuid(),password,true));
        await Denied(async()=>{await complete.Accept(client,staleProof);});
        Check(await sql.One("SELECT id FROM auth_credential_change_intents WHERE id=@id",("id",staleProof.OperationId)) is null,
            "recovery version replacement invalidates previously issued reset challenges before fencing");
        return count;
    }
    private sealed class ForbiddenGate:IAuthenticationIssuanceGate
    {public Task<IssuanceGateState> Acquire(NpgsqlTransaction transaction,Guid person)=>throw new Exception("Denied initiation acquired Person gate");}
}
