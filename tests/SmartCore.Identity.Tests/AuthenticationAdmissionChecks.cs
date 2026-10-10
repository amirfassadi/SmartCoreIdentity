using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SmartCore.Identity;

internal static class AuthenticationAdmissionChecks
{
    public static async Task<int> Run(Database db,Secrets secrets,TimeProvider clock,Action<TimeSpan> advance)
    {
        var count=0;
        void Check(bool value,string label) {if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);count++;}
        async Task Error(Func<Task> action,string code)
        {
            try {await action();}catch(ApiError e) when(e.Code==code){return;}
            throw new Exception("Expected "+code);
        }
        await using var sql=await db.Source.OpenConnectionAsync();
        var admission=new AuthenticationAdmission(db,secrets,clock);var otherInstance=new AuthenticationAdmission(db,secrets,clock);
        var subject=Secrets.Token();var client="admission-test";
        async Task<bool> Admit(int index)
        {
            try {await (index%2==0?admission:otherInstance).Admit(client,subject,"login");return true;}
            catch(ApiError e) when(e.Status==429){return false;}
        }
        var attempts=await Task.WhenAll(Enumerable.Range(0,13).Select(Admit));
        Check(attempts.Count(x=>x)==12,"distributed caller budget admits exactly twelve concurrent login attempts across two instances");
        await admission.Admit(client,Secrets.Token(),"login");await admission.Admit(client,subject,"session");
        Check(true,"independent BFF subjects and Session traffic do not share an exhausted login bucket");
        await sql.Execute("UPDATE auth_admission_buckets SET hits=600 WHERE bucket_key=@key",("key",secrets.Mac("auth-admission:client",client,"login")));
        var before=(await sql.One("SELECT count(*) AS n FROM auth_admission_buckets"))!.Get<long>("n");
        await Error(()=>admission.Admit(client,Secrets.Token(),"login"),"RATE_LIMITED");
        Check((await sql.One("SELECT count(*) AS n FROM auth_admission_buckets"))!.Get<long>("n")==before,
            "exhausted authenticated client budget prevents allocation of arbitrary new subject rows");
        advance(TimeSpan.FromMinutes(3));await admission.Admit(client,subject,"login");
        Check((await sql.One("SELECT count(*) AS n FROM auth_admission_buckets WHERE window_start<@now-interval '2 minutes'",("now",Timestamps.Now(clock))))!.Get<long>("n")==0,
            "expired caller windows reset and bounded cleanup removes stale partitions");

        const string password="strong password admission fixture";
        var email=Guid.NewGuid()+"@example.test";var binding=Secrets.Token();
        var registration=new RegistrationService(db,secrets,clock);var provisioning=new Provisioning(db,secrets,clock);
        var started=await registration.Start(new(email,null,password,"Admission",binding),Secrets.Token());
        var delivery=(await sql.One("SELECT * FROM delivery_outbox WHERE verification_id=@id",("id",started.VerificationSessionId)))!;
        var code=secrets.Open(delivery.Get<byte[]>("sealed_code"),"delivery:"+delivery.Get<Guid>("id"));
        var verified=await registration.Verify(new(started.VerificationSessionId,code,binding));
        await provisioning.EnsureCredential(verified.Result.RegistrationId);await provisioning.MarkReady(verified.Result.RegistrationId);await provisioning.Acknowledge(verified.Result.RegistrationId);
        var access=new AccessTokens(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),clock);var gate=new PostgresAuthenticationIssuanceGate();
        var credential=new CredentialChanges(db,secrets,clock);var changes=new PasswordChanges(db,secrets,clock,gate,access,credential);
        var auth=new AuthenticationService(db,secrets,clock,gate,access);
        var first=(await auth.Login(new(email,null,password),client)).Session;
        var person=access.Read(first.AccessToken,client).PersonId;var failures=new PasswordFailures(db,clock);
        for(var i=0;i<4;i++) await Error(async()=>{await auth.Login(new(email,null,"wrong password"),client);},"UNAUTHORIZED");
        var blocked=(await sql.One("SELECT * FROM auth_password_failures WHERE person_id=@id",("id",person)))!;
        await Error(async()=>{await changes.Accept(first.AccessToken,client,new(Guid.NewGuid(),password,"another valid replacement password"));},"UNAUTHORIZED");
        Check(blocked.Get<int>("failures")==4 && blocked.Time("blocked_until")==Timestamps.Now(clock).AddSeconds(1),
            "fourth login failure creates Person backoff shared with password-change admission");
        Check((await sql.One("SELECT failures,blocked_until FROM auth_password_failures WHERE person_id=@id",("id",person)))!.Time("blocked_until")==blocked.Time("blocked_until"),
            "blocked attempts neither test the current password nor extend the Person deadline");
        advance(TimeSpan.FromSeconds(1));var current=(await auth.Login(new(email,null,password),client)).Session;
        Check(await sql.One("SELECT person_id FROM auth_password_failures WHERE person_id=@id",("id",person)) is null,
            "successful password verification at the exact deadline clears failure evidence");
        for(var i=0;i<4;i++) await Error(async()=>{await changes.Accept(current.AccessToken,client,new(Guid.NewGuid(),"wrong password","another valid replacement password"));},"UNAUTHORIZED");
        await Error(async()=>{await auth.Login(new(email,null,password),client);},"UNAUTHORIZED");
        Check((await sql.One("SELECT failures FROM auth_password_failures WHERE person_id=@id",("id",person)))!.Get<int>("failures")==4,
            "stolen-access current-password guessing triggers the same bounded Person backoff as login");
        advance(TimeSpan.FromMinutes(15));await failures.Record(person,false);
        Check((await sql.One("SELECT failures FROM auth_password_failures WHERE person_id=@id",("id",person)))!.Get<int>("failures")==1,
            "failure history naturally resets after fifteen quiet minutes");
        await failures.Record(person,true);
        await Task.WhenAll(Enumerable.Range(0,6).Select(_=>new PasswordFailures(db,clock).Record(person,false)));
        Check((await sql.One("SELECT failures FROM auth_password_failures WHERE person_id=@id",("id",person)))!.Get<int>("failures")==6,
            "parallel instances atomically count every Person password failure without lost increments");
        for(var i=0;i<10;i++)await failures.Record(person,false);
        Check((await sql.One("SELECT blocked_until FROM auth_password_failures WHERE person_id=@id",("id",person)))!.Time("blocked_until")==Timestamps.Now(clock).AddSeconds(30),
            "exponential Person backoff is capped at thirty seconds and is not a permanent account lock");
        await failures.Record(person,true);
        var live=(await auth.Login(new(email,null,password),client)).Session;
        var request=new ChangePasswordRequest(Guid.NewGuid(),password,"reconciled despite unexpected worker exception");
        await changes.Accept(live.AccessToken,client,request);
        var fault=new FaultGate();var faulty=new PasswordChanges(db,secrets,clock,fault,access,credential);
        await faulty.Tick();
        Check((await sql.One("SELECT last_classification FROM auth_credential_change_intents WHERE id=@id",("id",request.OperationId)))!.Get<string>("last_classification")=="OutcomeUnknown"
            && (await changes.Status(request.OperationId)).Stage!="Reconciled",
            "unexpected InvalidCast during Process is classified OutcomeUnknown and preserves the fence");
        advance(TimeSpan.FromMinutes(1));fault.Fail=false;await faulty.Tick();
        Check((await changes.Status(request.OperationId)).Stage=="Reconciled","next scheduled attempt survives an unexpected exception and reconciles the same operation");
        await changes.Recover(request.OperationId,"ResolveNotApplied");
        Check((await sql.One("SELECT effective_outcome FROM auth_change_operator_audit WHERE intent_id=@id",("id",request.OperationId)))!.Get<string>("effective_outcome")=="Applied",
            "operator audit records Applied even when the requested action is ResolveNotApplied on an existing receipt");
        var runner=new FaultRunner();using var worker=new PasswordChangeWorker(runner,NullLogger<PasswordChangeWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);await runner.Continued.Task.WaitAsync(TimeSpan.FromSeconds(10));await worker.StopAsync(CancellationToken.None);
        Check(runner.Calls>=2,"background worker catches unexpected Tick exceptions and continues until explicit cancellation");
        advance(TimeSpan.FromSeconds(900));await changes.Tick();
        Check(!(await sql.One("SELECT sealed_replay_hash FROM auth_change_material WHERE intent_id=@id",("id",request.OperationId)))!.Has("sealed_replay_hash"),
            "bounded Argon2 replay verifier is erased independently of Credential recovery material");
        await LegacyMigration(secrets,clock,Check);
        await QueueChecks(Check);
        return count;
    }
    private static async Task<int> LegacyMigration(Secrets secrets,TimeProvider clock,Action<bool,string> check)
    {
        var schema="legacy_"+Guid.NewGuid().ToString("N");
        var original=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SMARTCORE_TEST_DB") ?? throw new Exception("Disposable database required"));
        await using var administration=new Database(original.ConnectionString);await using var admin=await administration.Source.OpenConnectionAsync();
        await admin.Execute("CREATE SCHEMA "+schema);
        try
        {
            original.SearchPath=schema;await using var legacy=new Database(original.ConnectionString);await using var c=await legacy.Source.OpenConnectionAsync();
            foreach(var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"database"),"*.sql").Order().Where(f=>int.Parse(Path.GetFileName(f).Split('_')[0])<=5)) await c.Execute(await File.ReadAllTextAsync(file));
            var person=Guid.NewGuid();var credential=Guid.NewGuid();var intent=Guid.NewGuid();var operation=Guid.NewGuid();var now=Timestamps.Now(clock);
            var old=secrets.Mac("legacy-fast-oracle","guessable old password","guessable new password");
            await c.Execute("INSERT INTO persons VALUES(@id,'email',@contact,'Legacy','Active',@now)",("id",person),("contact",person+"@example.test"),("now",now));
            await c.Execute("INSERT INTO credentials VALUES(@id,@person,'legacy-test-encoding','Active')",("id",credential),("person",person));
            await c.Execute("""
                INSERT INTO auth_credential_change_intents(id,person_id,kind,request_mac,verified_at,created_at,target_epoch,credential_operation_id,
                expected_credential_id,protected_material_reference,stage,next_attempt_at,resolved_at)
                VALUES(@id,@person,'ChangePassword',@mac,@now,@now,1,@op,@credential,'legacy-test','Reconciled',@now,@now)
                """,("id",intent),("person",person),("mac",old),("now",now),("op",operation),("credential",credential));
            await c.Execute("INSERT INTO credential_change_receipts VALUES(@op,@person,@credential,@mac,@mac,@mac,'Applied',@now)",
                ("op",operation),("person",person),("credential",credential),("mac",old),("now",now));
            await c.Execute(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","006_authentication_admission.sql")));
            var session=Guid.NewGuid();var recovery=secrets.Seal("legacy-test-encoding","password-change:"+intent);
            await c.Execute("INSERT INTO auth_sessions(id,person_id,issuance_epoch,client_id,created_at,expires_at,last_foreground_refresh_at,status) VALUES(@id,@person,0,'legacy-test',@now,@expires,@now,'Active')",
                ("id",session),("person",person),("now",now),("expires",now.AddSeconds(86400)));
            await c.Execute("INSERT INTO auth_change_material VALUES(@id,@session,'legacy-test',@mac,@mac,@sealed,@expiry,'plaintext-legacy-verifier',@replay)",
                ("id",intent),("session",session),("mac",old),("sealed",recovery),("expiry",now.AddHours(24)),("replay",now.AddSeconds(900)));
            await legacy.Migrate();
            var material=(await c.One("SELECT sealed_hash,sealed_replay_hash,replay_until FROM auth_change_material WHERE intent_id=@id",("id",intent)))!;
            check(!material.Has("sealed_replay_hash") && Secrets.Equal(material.Get<byte[]>("sealed_hash"),recovery)
                && (await c.One("SELECT count(*) AS n FROM pg_attribute WHERE attrelid='auth_change_material'::regclass AND attname='replay_password_hash' AND NOT attisdropped"))!.Get<long>("n")==0,
                "migration 007 drops the old clear replay verifier without changing independently sealed Credential recovery material");
            var changed=(await c.One("SELECT request_mac,request_version FROM auth_credential_change_intents WHERE id=@id",("id",intent)))!;
            var receipt=(await c.One("SELECT request_mac,outcome FROM credential_change_receipts WHERE operation_id=@id",("id",operation)))!;
            check(!Secrets.Equal(old,changed.Get<byte[]>("request_mac")) && Secrets.Equal(changed.Get<byte[]>("request_mac"),receipt.Get<byte[]>("request_mac"))
                && changed.Get<short>("request_version")==1 && receipt.Get<string>("outcome")=="Applied",
                "forward migration removes legacy raw-password HMAC evidence while preserving linked terminal outcomes");
            await legacy.Migrate();
            check(Secrets.Equal(changed.Get<byte[]>("request_mac"),(await c.One("SELECT request_mac FROM auth_credential_change_intents WHERE id=@id",("id",intent)))!.Get<byte[]>("request_mac")),
                "legacy oracle removal is one-time and migration replay preserves the new immutable binding");
        }
        finally {await admin.Execute("DROP SCHEMA "+schema+" CASCADE");}
        return 0; // The supplied check already increments the caller's count.
    }
    private static async Task QueueChecks(Action<bool,string> check)
    {
        var queue=new PasswordWorkQueue(1,2,TimeSpan.FromSeconds(2));
        var release=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running=queue.Run(()=>release.Task);
        using var cancellation=new CancellationTokenSource();
        var cancelled=queue.Run(()=>Task.FromResult(2),cancellation.Token);
        var waiting=queue.Run(()=>Task.FromResult(3));
        check(queue.Waiting==2 && !waiting.IsCompleted,"KDF burst queues within the bounded capacity instead of immediately rejecting allowed work");
        try {await queue.Run(()=>Task.FromResult(4));throw new Exception("Expected queue rejection");}
        catch(ApiError error) when(error.Status==503 && error.Code=="AUTHENTICATION_BUSY") { }
        check(queue.Waiting==2,"full KDF queue reports server busy separately from caller RATE_LIMITED without allocating more waiters");
        cancellation.Cancel();
        try {await cancelled;throw new Exception("Expected cancellation");}catch(OperationCanceledException) { }
        check(queue.Waiting==1,"cancelled queued KDF work releases its waiting reservation");
        release.SetResult(1);
        check(await running==1 && await waiting==3 && queue.Waiting==0,"short KDF queue drains after capacity becomes available without losing a slot");
        var timeoutQueue=new PasswordWorkQueue(1,1,TimeSpan.FromMilliseconds(50));
        var hold=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active=timeoutQueue.Run(()=>hold.Task);
        try {await timeoutQueue.Run(()=>Task.FromResult(2));throw new Exception("Expected queue timeout");}
        catch(ApiError error) when(error.Status==503 && error.Code=="AUTHENTICATION_BUSY") { }
        check(timeoutQueue.Waiting==0,"KDF queue deadline reports AUTHENTICATION_BUSY and removes the waiter");
        hold.SetResult(1);await active;
        try {await timeoutQueue.Run<int>(()=>throw new InvalidOperationException("synthetic"));}catch(InvalidOperationException) { }
        check(await timeoutQueue.Run(()=>Task.FromResult(7))==7,"KDF work exception releases acquired capacity for the next request");
    }
    private sealed class FaultGate : IAuthenticationIssuanceGate
    {
        public bool Fail=true;
        public Task<IssuanceGateState> Acquire(NpgsqlTransaction transaction,Guid person)
        {
            if(Fail)throw new InvalidCastException("synthetic test exception");
            return new PostgresAuthenticationIssuanceGate().Acquire(transaction,person);
        }
    }
    private sealed class FaultRunner : IPasswordChangeRunner
    {
        public int Calls;
        public TaskCompletionSource Continued {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Tick()
        {
            if(Interlocked.Increment(ref Calls)==1)throw new NullReferenceException("synthetic test exception");
            Continued.TrySetResult();return Task.CompletedTask;
        }
    }
}
