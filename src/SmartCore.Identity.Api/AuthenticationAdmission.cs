namespace SmartCore.Identity;

// Shared database failure evidence. No issuance/Credential lock is held during KDF.
public sealed class PasswordFailures(Database db,TimeProvider clock)
{
    public async Task<bool> Allowed(Guid person)
    {
        await using var c=await db.Source.OpenConnectionAsync();
        var row=await c.One("SELECT blocked_until,last_failure_at FROM auth_password_failures WHERE person_id=@id",("id",person));
        var now=Timestamps.Now(clock);
        return row is null || !row.Has("last_failure_at") || row.Time("last_failure_at").AddMinutes(15)<=now || row.Time("blocked_until")<=now;
    }
    public async Task Record(Guid person,bool success)
    {
        await using var c=await db.Source.OpenConnectionAsync();var now=Timestamps.Now(clock);
        if(success)
        {
            await c.Execute("DELETE FROM auth_password_failures WHERE person_id=@id",("id",person));return;
        }
        // One atomic upsert prevents lost failure increments across application instances.
        await c.Execute("""
            INSERT INTO auth_password_failures VALUES(@id,1,@now,@now)
            ON CONFLICT(person_id) DO UPDATE SET
              failures=CASE WHEN auth_password_failures.last_failure_at<=@now-interval '15 minutes' THEN 1 ELSE LEAST(auth_password_failures.failures+1,100) END,
              last_failure_at=@now,
              blocked_until=@now + (CASE WHEN auth_password_failures.last_failure_at<=@now-interval '15 minutes' THEN 0
                WHEN auth_password_failures.failures+1<4 THEN 0 ELSE LEAST(power(2,LEAST(auth_password_failures.failures-3,5)),30) END) * interval '1 second'
            """,("id",person),("now",now));
    }
}

// Bounded process-wide KDF concurrency supplements distributed caller admission.
public static class PasswordWork
{
    private static readonly SemaphoreSlim Slots=new(4,4);
    private static async Task<T> Run<T>(Func<Task<T>> work)
    {
        if(!Slots.Wait(0)) throw new ApiError(429,"RATE_LIMITED");
        try {return await work();} finally {Slots.Release();}
    }
    public static Task<bool> Verify(string password,string hash)=>Run(()=>Secrets.VerifyPassword(password,hash));
    public static Task<string> Hash(string password)=>Run(()=>Secrets.HashPassword(password));
}

// Only an authenticated BFF may supply the opaque, stable subject assertion.
public sealed class AuthenticationAdmission(Database db,Secrets secrets,TimeProvider clock)
{
    public async Task Admit(string client,string subject,string category)
    {
        Input.Require(Input.Id(subject));
        var (clientLimit,sourceLimit)=category switch {"login"=>(600,12),"change"=>(120,6),"session"=>(6000,120),_=>throw new InvalidOperationException("Invalid admission category")};
        await using var c=await db.Source.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        var now=Timestamps.Now(clock);
        async Task<bool> Reserve(string scope,int limit,params string[] parts)
        {
            var row=await c.One("""
                INSERT INTO auth_admission_buckets VALUES(@key,@now,1)
                ON CONFLICT(bucket_key) DO UPDATE SET
                  hits=CASE WHEN auth_admission_buckets.window_start<=@now-interval '1 minute' THEN 1 ELSE LEAST(auth_admission_buckets.hits+1,@limit+1) END,
                  window_start=CASE WHEN auth_admission_buckets.window_start<=@now-interval '1 minute' THEN @now ELSE auth_admission_buckets.window_start END
                RETURNING hits
                """,("key",secrets.Mac("auth-admission:"+scope,parts)),("now",now),("limit",limit));
            return row!.Get<int>("hits")<=limit;
        }
        // Always client before source. A denied client cannot allocate arbitrarily many subjects.
        var allowed=await Reserve("client",clientLimit,client,category);
        if(allowed) allowed=await Reserve("source",sourceLimit,client,category,subject);
        await tx.CommitAsync();
        // Cleanup never retains an admission row lock across another bucket's transaction.
        await c.Execute("DELETE FROM auth_admission_buckets WHERE bucket_key IN (SELECT bucket_key FROM auth_admission_buckets WHERE window_start<@now-interval '2 minutes' ORDER BY bucket_key LIMIT 100 FOR UPDATE SKIP LOCKED)",("now",now));
        if(!allowed) throw new ApiError(429,"RATE_LIMITED");
    }
}
