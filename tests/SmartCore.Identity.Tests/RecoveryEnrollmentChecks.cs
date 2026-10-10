using System.Security.Cryptography;
using Npgsql;
using SmartCore.Identity;

internal static class RecoveryEnrollmentChecks
{
    public static async Task<int> Run(Database db,Secrets secrets,TimeProvider clock,Action<TimeSpan> advance)
    {
        var count=0;
        void Check(bool value,string label) {if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);count++;}
        async Task Error(Func<Task> action,string code)
        {try {await action();}catch(ApiError error) when(error.Code==code){return;}throw new Exception("Expected "+code);}
        await using var sql=await db.Source.OpenConnectionAsync();
        const string password="recovery enrollment fixture password";const string client="recovery-test";
        var registration=new RegistrationService(db,secrets,clock);var provisioning=new Provisioning(db,secrets,clock);
        async Task<string> Ready(string? email,string? mobile)
        {
            var binding=Secrets.Token();var started=await registration.Start(new(email,mobile,password,"Recovery",binding),Secrets.Token());
            var delivery=(await sql.One("SELECT * FROM delivery_outbox WHERE verification_id=@id",("id",started.VerificationSessionId)))!;
            var code=secrets.Open(delivery.Get<byte[]>("sealed_code"),"delivery:"+delivery.Get<Guid>("id"));
            var verified=await registration.Verify(new(started.VerificationSessionId,code,binding));
            await provisioning.EnsureCredential(verified.Result.RegistrationId);await provisioning.MarkReady(verified.Result.RegistrationId);await provisioning.Acknowledge(verified.Result.RegistrationId);
            return email ?? mobile!;
        }
        var email=await Ready(Guid.NewGuid()+"@example.test",null);
        var mobile=await Ready(null,"+989"+RandomNumberGenerator.GetInt32(100000000,999999999));
        var access=new AccessTokens(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),clock);
        var gate=new PostgresAuthenticationIssuanceGate();var auth=new AuthenticationService(db,secrets,clock,gate,access);
        var enrollment=new RecoveryEnrollment(db,secrets,clock,gate,access);var initiation=new PasswordResetInitiation(db,secrets,clock,gate);
        var login=await auth.Login(new(email,null,password),client);var token=login.Session.AccessToken;
        var proof=access.Read(token,client);var person=proof.PersonId;
        Check(login.Person.RecoveryEnrollmentRequired,"first successful login explicitly requires recovery enrollment");
        var request=new EnrollRecoveryCodeRequest(Guid.NewGuid(),password,true);
        await Error(async()=>{await enrollment.Enroll(token,client,request with {AcceptLossRisk=false});},"VALIDATION_FAILED");
        await Error(async()=>{await enrollment.Enroll(token,client,request with {CurrentPassword="wrong password"});},"UNAUTHORIZED");
        Check(await sql.One("SELECT person_id FROM auth_recovery_codes WHERE person_id=@id",("id",person)) is null,
            "missing lockout acknowledgement and stolen-access wrong-password proof create no recovery factor");
        var race=await Task.WhenAll(enrollment.Enroll(token,client,request),enrollment.Enroll(token,client,request));
        var issued=race.Single(r=>r.RecoveryCode is not null);var repeated=race.Single(r=>r.RecoveryCode is null);
        Check(issued.RecoveryCode!.Length==32 && issued.RecoveryCode!.All(Uri.IsHexDigit) && issued.Version==1 && repeated.Status=="AlreadyIssued",
            "identical concurrent enrollment commits one version and reveals one 128-bit code exactly once");
        var stored=(await sql.One("SELECT * FROM auth_recovery_codes WHERE person_id=@id",("id",person)))!;
        Check(Secrets.Equal(stored.Get<byte[]>("verifier"),enrollment.Verifier(person,1,issued.RecoveryCode!))
            && !Secrets.Equal(stored.Get<byte[]>("verifier"),enrollment.Verifier(person,2,issued.RecoveryCode!))
            && !Secrets.Equal(stored.Get<byte[]>("verifier"),enrollment.Verifier(Guid.NewGuid(),1,issued.RecoveryCode!)),
            "recovery MAC binds high-entropy code to Person and version without storing plaintext");
        Check((await auth.Self(proof)).RecoveryEnrollmentRequired==false && !(await auth.Login(new(email,null,password),client)).Person.RecoveryEnrollmentRequired,
            "self and subsequent login reflect successful enrollment for BFF onboarding");
        var lost=await enrollment.Enroll(token,client,request);
        Check(lost.RecoveryCode is null && lost.Version==1,"lost enrollment response replays only a non-secret receipt and never redisplays the code");
        var otherSession=(await auth.Login(new(email,null,password),client)).Session;
        await Error(async()=>{await enrollment.Enroll(otherSession.AccessToken,client,request);},"IDEMPOTENCY_CONFLICT");
        Check((await sql.One("SELECT count(*) AS n FROM auth_account_notifications WHERE operation_id=@id",("id",request.OperationId)))!.Get<long>("n")==1,
            "registration notification is atomically queued once despite concurrent enrollment and replay");
        try {await sql.Execute("UPDATE auth_recovery_codes SET verifier=@v WHERE person_id=@id",("v",RandomNumberGenerator.GetBytes(32)),("id",person));throw new Exception("Expected immutable version");}
        catch(PostgresException error) when(error.SqlState=="23514") { }
        try {await sql.Execute("UPDATE auth_recovery_enrollments SET version=2 WHERE operation_id=@id",("id",request.OperationId));throw new Exception("Expected immutable receipt");}
        catch(PostgresException error) when(error.SqlState=="23514") { }
        Check(true,"database rejects unversioned code replacement and rewriting enrollment receipts");
        var replacement=await enrollment.Enroll(token,client,request with {OperationId=Guid.NewGuid()});
        var updated=(await sql.One("SELECT * FROM auth_recovery_codes WHERE person_id=@id",("id",person)))!;
        Check(replacement.Version==2 && !Secrets.Equal(updated.Get<byte[]>("verifier"),enrollment.Verifier(person,1,issued.RecoveryCode!))
            && updated.Get<long>("enrolled_epoch")==proof.Epoch,
            "replacement advances recovery version beside the current epoch and invalidates the old code");
        Check((await sql.One("SELECT kind FROM auth_account_notifications WHERE operation_id=@id",("id",replacement.OperationId)))!.Get<string>("kind")=="RecoveryCodeReplaced",
            "replacement queues an account notification without any code or verifier payload");
        var phoneLogin=await auth.Login(new(null,mobile,password),client);
        var phoneCode=await enrollment.Enroll(phoneLogin.Session.AccessToken,client,new(Guid.NewGuid(),password,true));
        Check(phoneCode.RecoveryCode is not null && !(await auth.Self(access.Read(phoneLogin.Session.AccessToken,client))).RecoveryEnrollmentRequired,
            "the same recovery enrollment policy applies to telephone and email accounts");
        var phoneChallenge=await initiation.Start(client,new(Guid.NewGuid(),null,mobile,Secrets.Token()));
        await enrollment.Enroll(phoneLogin.Session.AccessToken,client,new(Guid.NewGuid(),password,true));
        Check((await sql.One("SELECT recovery_version FROM auth_reset_challenges WHERE id=@id",("id",phoneChallenge.ChallengeId)))!.Get<long>("recovery_version")==1
            && (await sql.One("SELECT version FROM auth_recovery_codes WHERE person_id=@id",("id",phoneLogin.Person.PersonId)))!.Get<long>("version")==2,
            "replacement leaves prior challenges bound to a superseded version for later proof rejection");
        var begin=new StartPasswordReset(Guid.NewGuid(),email,null,Secrets.Token());
        var known=await initiation.Start(client,begin);var unknown=await initiation.Start(client,begin with {OperationId=Guid.NewGuid(),Email=Guid.NewGuid()+"@example.test"});
        var pending=await Task.WhenAll(initiation.Start(client,begin),initiation.Start(client,begin));
        Check(pending.All(x=>x==known) && known.Status==unknown.Status && known.ChallengeId.Length==unknown.ChallengeId.Length && known.ExpiresAt==unknown.ExpiresAt,
            "reset initiation has the same accepted response shape for known and unknown contacts with stable replay");
        Check((await sql.One("SELECT observed_epoch,recovery_version FROM auth_reset_challenges WHERE id=@id",("id",known.ChallengeId)))!.Get<long>("recovery_version")==2
            && await sql.One("SELECT id FROM auth_reset_delivery WHERE challenge_id=@id",("id",unknown.ChallengeId)) is null,
            "eligible reset challenge captures enrollment version and epoch while unknown accounts queue no delivery");
        var otp=(await sql.One("SELECT * FROM auth_reset_delivery WHERE challenge_id=@id",("id",known.ChallengeId)))!;
        var clear=secrets.Open(otp.Get<byte[]>("sealed_code"),"reset-delivery:"+otp.Get<Guid>("id"));
        Check(clear.Length==8 && Secrets.Equal(otp.Get<byte[]>("verifier"),secrets.Mac("reset-otp-v1",known.ChallengeId,clear)),
            "reset OTP has distinct verification and encrypted-delivery purposes");
        await initiation.Start(client,begin with {OperationId=Guid.NewGuid()});await initiation.Start(client,begin with {OperationId=Guid.NewGuid()});
        var suppressed=await initiation.Start(client,begin with {OperationId=Guid.NewGuid()});
        Check(suppressed.Status=="Accepted" && await sql.One("SELECT id FROM auth_reset_delivery WHERE challenge_id=@id",("id",suppressed.ChallengeId)) is null
            && (await sql.One("SELECT count(*) AS n FROM auth_reset_delivery WHERE person_id=@id",("id",person)))!.Get<long>("n")==3,
            "fresh reset challenges cannot bypass the shared three-deliveries-per-Person fifteen-minute budget");
        Check((await sql.One("SELECT epoch,pending_operation_id FROM auth_issuance_state WHERE person_id=@id",("id",person)))!.Get<long>("epoch")==proof.Epoch
            && (await auth.Self(proof)).PersonId==person,"OTP initiation, suppressed delivery and unknown contact never fence issuance or close Sessions");
        await Error(async()=>{await initiation.Start(client,begin with {BindingSecret=Secrets.Token()});},"IDEMPOTENCY_CONFLICT");
        advance(TimeSpan.FromMinutes(10));
        await Error(async()=>{await initiation.Start(client,begin);},"UNAUTHORIZED");
        var changes=new PasswordChanges(db,secrets,clock,gate,access,new CredentialChanges(db,secrets,clock));await changes.Tick();
        Check(await sql.One("SELECT sealed_code FROM auth_reset_delivery WHERE id=@id",("id",otp.Get<Guid>("id"))) is null,
            "expired reset replay cannot extend OTP life and the scheduled cleanup erases delivery material");
        var expiring=(await auth.Login(new(email,null,password),client)).Session;
        var expiryGate=new PausedGate();var expiryEnrollment=new RecoveryEnrollment(db,secrets,clock,expiryGate,access);
        var expiryRequest=new EnrollRecoveryCodeRequest(Guid.NewGuid(),password,true);
        var expired=expiryEnrollment.Enroll(expiring.AccessToken,client,expiryRequest);
        await expiryGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));advance(TimeSpan.FromSeconds(900));expiryGate.Resume.SetResult();
        await Error(async()=>{await expired;},"UNAUTHORIZED");
        Check(await sql.One("SELECT operation_id FROM auth_recovery_enrollments WHERE operation_id=@id",("id",expiryRequest.OperationId)) is null,
            "access expiry during KDF or issuance-gate wait cannot commit recovery enrollment");
        var fresh=(await auth.Login(new(email,null,password),client)).Session;
        var paused=new PausedGate();var racingEnrollment=new RecoveryEnrollment(db,secrets,clock,paused,access);
        var raced=racingEnrollment.Enroll(fresh.AccessToken,client,new(Guid.NewGuid(),password,true));
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var change=await changes.Accept(fresh.AccessToken,client,new(Guid.NewGuid(),password,"changed password invalidates stale enrollment"));
        paused.Resume.SetResult();await Error(async()=>{await raced;},"UNAUTHORIZED");
        Check((await sql.One("SELECT version FROM auth_recovery_codes WHERE person_id=@id",("id",person)))!.Get<long>("version")==2,
            "enrollment proof verified before a racing password change cannot replace the code after the fence");
        await changes.Process(change.OperationId);
        return count;
    }
    private sealed class PausedGate : IAuthenticationIssuanceGate
    {
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IssuanceGateState> Acquire(NpgsqlTransaction transaction,Guid person)
        {Entered.SetResult();await Resume.Task;return await new PostgresAuthenticationIssuanceGate().Acquire(transaction,person);}
    }
}
