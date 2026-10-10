using System.Security.Cryptography;
using Npgsql;
using SmartCore.Identity;

internal static class PasswordChangeChecks
{
    public static async Task<int> Run(Database db,Secrets secrets,TimeProvider clock,Action<TimeSpan> advance,string email,string initialPassword)
    {
        var count=0;
        void Check(bool value,string label) {if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);count++;}
        async Task Error(Func<Task> action,string code)
        {
            try {await action();}catch(ApiError e) when(e.Code==code){return;}
            throw new Exception("Expected "+code);
        }
        await using var sql=await db.Source.OpenConnectionAsync();
        const string client="test-bff";
        var access=new AccessTokens(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),clock);
        var gate=new PostgresAuthenticationIssuanceGate();var credential=new CredentialChanges(db,secrets,clock);
        var changes=new PasswordChanges(db,secrets,clock,gate,access,credential);
        var auth=new AuthenticationService(db,secrets,clock,gate,access);
        string password=initialPassword;
        Task<LoginResult> Login()=>auth.Login(new(email,null,password),client);
        async Task<Row> Intent(Guid id)=>(await sql.One("""
            SELECT i.*,m.session_id,m.client_id,m.expected_hash_mac,m.replacement_hash_mac,m.sealed_hash,m.expires_at
            FROM auth_credential_change_intents i JOIN auth_change_material m ON m.intent_id=i.id WHERE i.id=@id
            """,("id",id)))!;
        var login=await Login();var session=login.Session;var person=login.Person.PersonId;
        var epoch=(await sql.One("SELECT epoch FROM auth_issuance_state WHERE person_id=@id",("id",person)))!.Get<long>("epoch");
        await Error(async()=>{await changes.Accept(session.AccessToken,client,new(Guid.NewGuid(),password,"short"));},"VALIDATION_FAILED");
        await Error(async()=>{await changes.Accept(session.AccessToken,client,new(Guid.NewGuid(),"wrong password","valid new password for testing"));},"UNAUTHORIZED");
        Check((await sql.One("SELECT epoch FROM auth_issuance_state WHERE person_id=@id",("id",person)))!.Get<long>("epoch")==epoch
            && (await sql.One("SELECT status FROM auth_sessions WHERE id=@id",("id",session.SessionId)))!.Get<string>("status")=="Active",
            "password policy and invalid proof reject before fencing or Session closure");
        var expiryGate=new PausedGate();
        var delayedChanges=new PasswordChanges(db,secrets,clock,expiryGate,access,credential);
        var expiryRequest=new ChangePasswordRequest(Guid.NewGuid(),password,"valid delayed password admission");
        var delayed=delayedChanges.Accept(session.AccessToken,client,expiryRequest);
        await expiryGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        advance(TimeSpan.FromSeconds(900));expiryGate.Resume.TrySetResult();
        await Error(async()=>{await delayed;},"UNAUTHORIZED");
        Check(await sql.One("SELECT id FROM auth_credential_change_intents WHERE id=@id",("id",expiryRequest.OperationId)) is null
            && (await sql.One("SELECT epoch FROM auth_issuance_state WHERE person_id=@id",("id",person)))!.Get<long>("epoch")==epoch,
            "access proof expiring during KDF or gate wait cannot admit a mutation or advance epoch");
        session=(await Login()).Session;
        var other=(await Login()).Session;
        var request=new ChangePasswordRequest(Guid.NewGuid(),password,"first changed password for tests");
        var pausedGate=new PausedGate();var racingAuth=new AuthenticationService(db,secrets,clock,pausedGate,access);
        var staleLogin=racingAuth.Login(new(email,null,password),client);
        await pausedGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var acceptRace=await Task.WhenAll(changes.Accept(session.AccessToken,client,request),changes.Accept(session.AccessToken,client,request));
        var accepted=acceptRace[0];
        var replay=await changes.Accept(session.AccessToken,client,request);
        await Error(async()=>{await changes.Accept(session.AccessToken,client,request with {NewPassword="different password for collision"});},"IDEMPOTENCY_CONFLICT");
        Check(accepted==replay && acceptRace[0]==acceptRace[1] && (await sql.One("SELECT epoch FROM auth_issuance_state WHERE person_id=@id",("id",person)))!.Get<long>("epoch")==epoch+1,
            "bound accepted-intent replay advances epoch exactly once and rejects a different request");
        Check((await sql.One("SELECT count(*) AS n FROM auth_sessions WHERE person_id=@person AND status='Active'",("person",person)))!.Get<long>("n")==0
            && (await sql.One("SELECT status FROM auth_refresh_families WHERE session_id=@id",("id",other.SessionId)))!.Get<string>("status")=="Revoked",
            "admission durably fences and closes every Session/family before Credential replacement");
        await Error(async()=>{await Login();},"UNAUTHORIZED");
        await Error(async()=>{await auth.Refresh(new(other.RefreshToken,true),client);},"UNAUTHORIZED");
        Check(true,"fenced issuance and old refresh uniformly deny while Credential commit is pending");
        // Crash immediately after admission: a new coordinator/worker instance uses only durable state.
        var restarted=new PasswordChanges(db,secrets,clock,new PostgresAuthenticationIssuanceGate(),access,new CredentialChanges(db,secrets,clock));
        await restarted.Tick();
        pausedGate.Resume.TrySetResult();
        await Error(async()=>{await staleLogin;},"UNAUTHORIZED");
        Check(true,"old-password login verified before a real mutation cannot issue after its fence and reconciliation");
        Check((await changes.Status(request.OperationId)).Stage=="Reconciled" && !(await Intent(request.OperationId)).Has("sealed_hash"),
            "restarted scheduled worker completes the admitted operation and purges protected material");
        await Error(async()=>{await Login();},"UNAUTHORIZED");
        password=request.NewPassword;
        Check((await Login()).Person.PersonId==person,"old password is denied and replacement password authenticates after reconciliation");
        var receipt=(await credential.Outcome((await Intent(request.OperationId)).Get<Guid>("credential_operation_id")))!;
        Check(receipt.Get<string>("outcome")=="Applied" && (await sql.One("SELECT count(*) AS n FROM credential_change_outbox WHERE operation_id=@id",("id",receipt.Get<Guid>("operation_id"))))!.Get<long>("n")==1,
            "Credential replacement, one immutable Applied receipt and one PasswordChanged fact commit together");
        await changes.Process(request.OperationId);
        try {await sql.Execute("UPDATE credential_change_receipts SET outcome='RejectedNotApplied' WHERE operation_id=@id",("id",receipt.Get<Guid>("operation_id")));throw new Exception("Expected receipt guard");}
        catch(PostgresException e) when(e.SqlState=="23514") { }
        Check(true,"terminal Credential receipt cannot be rewritten by a later retry");
        var second=(await Login()).Session;var secondRequest=new ChangePasswordRequest(Guid.NewGuid(),password,"second changed password for tests");
        await changes.Accept(second.AccessToken,client,secondRequest);
        var secondIntent=await Intent(secondRequest.OperationId);
        // Actual independent Credential commit, followed by a simulated lost result / process crash.
        await credential.Apply(secondIntent);
        await Error(async()=>{await auth.Login(new(email,null,secondRequest.NewPassword),client);},"UNAUTHORIZED");
        await restarted.Process(secondRequest.OperationId);await restarted.Process(secondRequest.OperationId);
        password=secondRequest.NewPassword;
        Check((await Login()).Person.PersonId==person && (await sql.One("SELECT count(*) AS n FROM credential_change_receipts WHERE operation_id=@id",("id",secondIntent.Get<Guid>("credential_operation_id"))))!.Get<long>("n")==1,
            "lost Credential result resumes from authoritative receipt without replacing twice or reopening early");
        // A retry of an earlier receipt must survive the new Credential hash and purged old material.
        Check((await credential.Apply(await Intent(request.OperationId))).Get<string>("outcome")=="Applied",
            "historical Credential outcome remains authoritative after a later password change");
        var failed=(await Login()).Session;var failedRequest=new ChangePasswordRequest(Guid.NewGuid(),password,"recoverable password after failure");
        await changes.Accept(failed.AccessToken,client,failedRequest);
        await sql.Execute("UPDATE auth_credential_change_intents SET attempts=7 WHERE id=@id",("id",failedRequest.OperationId));
        await sql.Execute("CREATE FUNCTION test_fail_change() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected'; END $$; CREATE TRIGGER test_change_failure BEFORE UPDATE ON credentials FOR EACH ROW EXECUTE FUNCTION test_fail_change()");
        try {await changes.Tick();}
        finally {await sql.Execute("DROP TRIGGER test_change_failure ON credentials; DROP FUNCTION test_fail_change()");}
        Check((await changes.Status(failedRequest.OperationId)).Stage=="FailedClosed"
            && (await sql.One("SELECT pending_operation_id FROM auth_issuance_state WHERE person_id=@id",("id",person)))!.Has("pending_operation_id"),
            "eighth failed attempt remains FailedClosed with issuance fence intact");
        await changes.Recover(failedRequest.OperationId,"Retry");await restarted.Tick();password=failedRequest.NewPassword;
        Check((await changes.Status(failedRequest.OperationId)).Stage=="Reconciled" && (await Login()).Person.PersonId==person,
            "restricted operator retry restarts the same operation and releases only after authoritative reconciliation");
        var expired=(await Login()).Session;var expiredRequest=new ChangePasswordRequest(Guid.NewGuid(),password,"expired material password must not apply");
        await changes.Accept(expired.AccessToken,client,expiredRequest);
        var staleIntent=await Intent(expiredRequest.OperationId);
        advance(TimeSpan.FromHours(24));await restarted.Tick();
        Check((await changes.Status(expiredRequest.OperationId)).Stage=="FailedClosed" && !(await Intent(expiredRequest.OperationId)).Has("sealed_hash"),
            "expired protected material fails closed and is erased; timeout never unlocks issuance");
        await changes.Recover(expiredRequest.OperationId,"ResolveNotApplied");
        var terminal=await credential.Apply(staleIntent); // A stale in-flight worker must see the terminal rejection.
        Check(terminal.Get<string>("outcome")=="RejectedNotApplied" && (await Login()).Person.PersonId==person
            && (await sql.One("SELECT status FROM auth_sessions WHERE id=@id",("id",expired.SessionId)))!.Get<string>("status")=="Closed",
            "operator proves not-applied under Credential lock; stale worker cannot mutate and old Sessions remain closed");
        var recovered=(await Login()).Session;var finalRequest=new ChangePasswordRequest(Guid.NewGuid(),password,"operator sees committed password receipt");
        await changes.Accept(recovered.AccessToken,client,finalRequest);
        await credential.Apply(await Intent(finalRequest.OperationId));
        await sql.Execute("UPDATE auth_credential_change_intents SET stage='FailedClosed',resolved_at=@now,last_classification='OutcomeUnknown' WHERE id=@id",("now",Timestamps.Now(clock)),("id",finalRequest.OperationId));
        await changes.Recover(finalRequest.OperationId,"ResolveNotApplied");password=finalRequest.NewPassword;
        Check((await Login()).Person.PersonId==person,"operator not-applied request honors an existing Applied receipt instead of undoing the password");
        Check((await sql.One("SELECT count(*) AS n FROM auth_change_operator_audit"))!.Get<long>("n")==3,
            "operator actions are durably audited with their Session recovery commits");
        var raceSession=(await Login()).Session;
        var raceRequest=new ChangePasswordRequest(Guid.NewGuid(),password,"password racing with foreground refresh");
        SessionTokens? successor=null;
        async Task RotateOrDeny()
        {
            try {successor=await auth.Refresh(new(raceSession.RefreshToken,true),client);}
            catch(ApiError e) when(e.Code=="UNAUTHORIZED") { }
        }
        await Task.WhenAll(RotateOrDeny(),changes.Accept(raceSession.AccessToken,client,raceRequest));
        if(successor is not null) await Error(async()=>{await auth.Refresh(new(successor.RefreshToken,true),client);},"UNAUTHORIZED");
        await restarted.Tick();password=raceRequest.NewPassword;
        Check((await sql.One("SELECT count(*) AS n FROM auth_sessions WHERE person_id=@id AND status='Active'",("id",person)))!.Get<long>("n")==0,
            "real password mutation racing refresh leaves no usable successor or active old Session");
        return count;
    }
    private sealed class PausedGate : IAuthenticationIssuanceGate
    {
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IssuanceGateState> Acquire(NpgsqlTransaction transaction,Guid person)
        {
            Entered.TrySetResult();await Resume.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return await new PostgresAuthenticationIssuanceGate().Acquire(transaction,person);
        }
    }
}
