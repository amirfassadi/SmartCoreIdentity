using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using SmartCore.Identity;

namespace SmartCore.Identity.Bff;

public enum BffActivity {Background,UserAction}
public enum RefreshStage {BeforeDispatch,DispatchCommitted,ResponseReceived,SuccessorWritten,SuccessorCommitted}
// Development fault seam, never an HTTP request field.
public interface IRefreshProbe {Task Observe(RefreshStage stage,CancellationToken cancellation);}
public interface IIdentityRefresh {Task<SessionTokens> Refresh(string token,bool foreground,string subject,CancellationToken cancellation);}
public interface IIdentityLogout {Task Logout(string accessToken,Guid session,string subject,CancellationToken cancellation);}
// Own the handler: no redirect or automatic retry may expose/reuse refresh secrets.
public sealed class IdentityRefreshHttp:IIdentityRefresh,IIdentityLogout,IDisposable
{
    private readonly HttpClient client;private readonly string bffKey;private readonly string receiptKey;
    public IdentityRefreshHttp(Uri identity,string bffKey,string receiptKey,bool developmentLoopback=false)
    {
        if(!identity.IsAbsoluteUri || identity.UserInfo.Length!=0 || identity.Query.Length!=0 || identity.Fragment.Length!=0
            || !(identity.Scheme=="https" || developmentLoopback && identity.Scheme=="http" && identity.IsLoopback)
            || bffKey.Length<32 || receiptKey.Length<32 || receiptKey==bffKey)throw new ArgumentException("Trusted Identity origin/key/subject required");
        this.bffKey=bffKey;this.receiptKey=receiptKey;
        client=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false}){BaseAddress=identity,Timeout=Timeout.InfiniteTimeSpan};
    }
    public async Task<SessionTokens> Refresh(string token,bool foreground,string subject,CancellationToken cancellation)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"auth/refresh");
        var reference=Guid.NewGuid();request.Headers.Add("X-Refresh-Request",reference.ToString("D"));
        request.Headers.Add("X-Bff-Client-Key",bffKey);request.Headers.Add("X-Bff-Subject",subject);
        request.Content=JsonContent.Create(new RefreshRequest(token,foreground));
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);
        try
        {
        await using var stream=await response.Content.ReadAsStreamAsync(cancellation);
        var bytes=new byte[32769];var used=0;
        while(used<bytes.Length)
        {var n=await stream.ReadAsync(bytes.AsMemory(used),cancellation);if(n==0)break;used+=n;}
        if(used>32768)throw new InvalidOperationException("Oversized successor");
        if(!response.IsSuccessStatusCode)
        {
            if(response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest)
            {
                try
                {
                    var rejection=JsonSerializer.Deserialize<RefreshRejection>(bytes.AsSpan(0,used),new JsonSerializerOptions(JsonSerializerDefaults.Web)
                        {UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});
                    if(rejection is not null && rejection.Disposition=="NotProcessed" && RefreshRejection.Allowed((int)response.StatusCode,rejection.Code))
                    {
                        if(rejection.Verify(receiptKey,(int)response.StatusCode,reference))throw new RefreshNotProcessed(rejection.RetryAfterSeconds,response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest);
                        throw new RefreshReceiptUnverified((int)response.StatusCode);
                    }
                }
                catch(JsonException) { /* Generic proxy/malformed response has no admission proof. */ }
            }
            throw new HttpRequestException("Identity refresh rejected",null,response.StatusCode);
        }
        return JsonSerializer.Deserialize<SessionTokens>(bytes.AsSpan(0,used),new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow})??throw new InvalidOperationException("Missing successor");
        }
        catch(Exception failure) when(response.StatusCode==System.Net.HttpStatusCode.Forbidden && failure is not RefreshNotProcessed && failure is not HttpRequestException)
        {throw new HttpRequestException("Unverifiable BFF credential rejection",failure,response.StatusCode);}
    }
    public async Task Logout(string accessToken,Guid session,string subject,CancellationToken cancellation)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"auth/logout");
        request.Headers.Add("X-Bff-Client-Key",bffKey);request.Headers.Add("X-Bff-Subject",subject);
        request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",accessToken);
        request.Content=JsonContent.Create(new {sessionId=session});
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);
        if(response.StatusCode!=System.Net.HttpStatusCode.NoContent)
            throw new HttpRequestException("Identity logout rejected",null,response.StatusCode);
    }
    public void Dispose()=>client.Dispose();
}
public sealed record BffBudgets(TimeSpan Http,TimeSpan Owner,TimeSpan LockWait)
{
    public static BffBudgets Development {get;}=new(TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(17));
    public void Validate()
    {if(Http<=TimeSpan.Zero || TimeSpan.FromTicks(Owner.Ticks*4/5)<=Http || LockWait<=Owner || Owner>=TimeSpan.FromSeconds(60))throw new ArgumentException("HTTP must fit owner budget below the 60-second recovery interval; contenders need owner budget plus margin.");}
}
// Server-to-server value. Browser routes must never serialize it.
public sealed record BffAccess(string AccessToken,Guid SessionId,long Version,bool RefreshDue);
// Trusted adapters only: HTTP creates this after verifying the nonce-bound receipt.
public sealed class RefreshNotProcessed(int retryAfterSeconds,bool configuration=false):Exception("Refresh not processed")
{public bool Configuration {get;}=configuration;public int RetryAfterSeconds {get;}=Math.Clamp(retryAfterSeconds,1,60);}
public sealed class RefreshReceiptUnverified(int status):Exception("Refresh receipt cannot be verified") {public int Status {get;}=status;}
public sealed class BffError(string code,int? retryAfterSeconds=null):Exception(code)
{public string Code {get;}=code;public int? RetryAfterSeconds {get;}=retryAfterSeconds;}

public sealed class BffRefresh(BffStore store,Secrets secrets,TimeProvider clock,IIdentityRefresh identity,BffBudgets? budgets=null,IRefreshProbe? probe=null)
{
    private readonly BffBudgets limits=Validated(budgets??BffBudgets.Development);
    private static BffBudgets Validated(BffBudgets value){value.Validate();return value;}
    private static readonly Meter Meter=new("SmartCore.Identity.Bff");
    private static readonly Counter<long> Fast=Meter.CreateCounter<long>("bff.refresh.fast_access");
    private static readonly Counter<long> Dispatches=Meter.CreateCounter<long>("bff.refresh.dispatches");
    private static readonly Counter<long> Unknown=Meter.CreateCounter<long>("bff.refresh.uncertain_sessions");
    private static readonly Counter<long> LogoutLocalOnly=Meter.CreateCounter<long>("bff.logout_local_only");
    private static readonly Counter<long> ProbeLost=Meter.CreateCounter<long>("bff.refresh.probe_sessions_lost");
    private static readonly Counter<long> Coalesced=Meter.CreateCounter<long>("bff.refresh.coalesced");
    private static readonly Counter<long> Busy=Meter.CreateCounter<long>("bff.refresh.lock_timeout");
    private DateTimeOffset Now=>Timestamps.Now(clock);
    private byte[] Cookie(string value)
    {if(!Input.Secret(value))throw new BffError("REAUTHENTICATION_REQUIRED");return secrets.Mac("bff-cookie-v1",value);}
    private static string Purpose(Guid id,long version)=>"bff-tokens:"+id+":"+version.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private SessionTokens Tokens(Row row)=>JsonSerializer.Deserialize<SessionTokens>(secrets.Open(row.Get<byte[]>("sealed_tokens"),Purpose(row.Get<Guid>("id"),row.Get<long>("version"))))??throw new BffError("REAUTHENTICATION_REQUIRED");
    private bool Live(Row row)=>row.Get<string>("status")=="Active" && row.Time("expires_at")>Now && row.Time("last_foreground_at").AddMinutes(30)>Now;
    private bool Due(Row row,BffActivity activity)
    {
        var life=row.Time("access_expires_at")-row.Time("access_issued_at");
        return Now>=row.Time("access_issued_at")+TimeSpan.FromTicks(life.Ticks*4/5)
            || (activity==BffActivity.UserAction && row.Time("last_foreground_at").AddMinutes(12)<=Now);
    }
    private BffAccess Access(Row row)=>new(Tokens(row).AccessToken,row.Get<Guid>("identity_session_id"),row.Get<long>("version"),Due(row,BffActivity.Background));
    private Task Observe(RefreshStage stage,CancellationToken cancellation)=>probe?.Observe(stage,cancellation)??Task.CompletedTask;
    // Principal lookup only: stable BFF row UUID, no token decryption, locks, writes or refresh.
    public async Task<Guid?> ResolveBrowserSession(string cookie,bool allowLogoutRequested=false,CancellationToken cancellation=default)
    {
        if(!Input.Secret(cookie))return null;
        await using var c=await store.Source.OpenConnectionAsync(cancellation);
        var row=await BffStore.One(c,"SELECT id,status,expires_at,last_foreground_at FROM bff_sessions WHERE cookie_mac=@cookie",cancellation,("cookie",Cookie(cookie)));
        if(row is null || row.Time("expires_at")<=Now)return null;
        if(Live(row) || allowLogoutRequested && row.Get<string>("status")=="LogoutRequested")return row.Get<Guid>("id");
        return null;
    }
    public async Task<string> Create(SessionTokens tokens,CancellationToken cancellation=default)
    {
        if(tokens.SessionId==Guid.Empty || tokens.AccessExpiresAt<=Now || tokens.AccessExpiresAt>tokens.ExpiresAt)throw new ArgumentException("Invalid login result");
        var cookie=Secrets.Token();var id=Guid.NewGuid();
        await using var c=await store.Source.OpenConnectionAsync(cancellation);await using var tx=await c.BeginTransactionAsync(cancellation);
        await BffStore.Execute(c,"""
            INSERT INTO bff_sessions(id,cookie_mac,identity_session_id,version,status,sealed_tokens,access_issued_at,access_expires_at,expires_at,last_foreground_at)
            VALUES(@id,@cookie,@identity,0,'Active',@tokens,@now,@access,@end,@now)
            """,cancellation,("id",id),("cookie",Cookie(cookie)),("identity",tokens.SessionId),("tokens",secrets.Seal(JsonSerializer.Serialize(tokens),Purpose(id,0))),("now",Now),("access",tokens.AccessExpiresAt),("end",tokens.ExpiresAt));
        await BffStore.Execute(c,"INSERT INTO bff_refresh_journal(session_id) VALUES(@id)",cancellation,("id",id));
        await tx.CommitAsync(cancellation);return cookie;
    }
    // MVCC committed snapshot; no FOR UPDATE and no journal lock for cached access.
    // An access token can race logout/uncertainty within its bounded remaining lifetime.
    public async Task<BffAccess> GetAccess(string cookie,CancellationToken cancellation=default)
    {
        await using(var c=await store.Source.OpenConnectionAsync(cancellation))
        {
            var row=await BffStore.One(c,"SELECT * FROM bff_sessions WHERE cookie_mac=@cookie",cancellation,("cookie",Cookie(cookie)));
            if(row is null || !Live(row))throw new BffError("REAUTHENTICATION_REQUIRED");
            if(row.Time("access_expires_at")>Now.AddSeconds(2)){Fast.Add(1);return Access(row);}
        }
        // Only expired/near-expired access requires waiting for the refresh owner.
        return await RefreshIfDue(cookie,BffActivity.Background,cancellation);
    }
    public async Task<BffAccess> RefreshIfDue(string cookie,BffActivity activity,CancellationToken cancellation=default)
    {
        using var waiting=CancellationTokenSource.CreateLinkedTokenSource(cancellation);waiting.CancelAfter(limits.LockWait);
        await using var c=await store.Source.OpenConnectionAsync(waiting.Token);await using var tx=await c.BeginTransactionAsync(waiting.Token);
        BffStore.RequireServerVersion(c.PostgreSqlVersion);
        await BffStore.Execute(c,"SELECT set_config('lock_timeout',@wait,true),set_config('transaction_timeout',@total,true)",waiting.Token,
            ("wait",((long)limits.LockWait.TotalMilliseconds).ToString()+"ms"),("total",((long)(limits.LockWait+limits.Owner).TotalMilliseconds).ToString()+"ms"));
        Row? row;
        try {row=await BffStore.One(c,"SELECT * FROM bff_sessions WHERE cookie_mac=@cookie FOR UPDATE",waiting.Token,("cookie",Cookie(cookie)));}
        catch(Exception e) when(e is PostgresException {SqlState:"55P03"} || e is OperationCanceledException && !cancellation.IsCancellationRequested)
        {Busy.Add(1);throw new BffError("BFF_BUSY",1);}
        var cleanupBudget=TimeSpan.FromTicks(limits.Owner.Ticks/5);
        using var owner=CancellationTokenSource.CreateLinkedTokenSource(cancellation);owner.CancelAfter(limits.Owner-cleanupBudget);
        var ct=owner.Token;
        if(row is null || !Live(row))throw new BffError("REAUTHENTICATION_REQUIRED");
        var id=row.Get<Guid>("id");var version=row.Get<long>("version");
        // Read journal without locking it in A: B must be able to update it independently.
        var journal=(await BffStore.One(c,"SELECT * FROM bff_refresh_journal WHERE session_id=@id",ct,("id",id)))!;
        if(journal.Has("attempt_id"))
        {
            var attempt=journal.Get<Guid>("attempt_id");var source=journal.Get<long>("source_version");
            if(row.Has("completed_attempt_id") && row.Get<Guid>("completed_attempt_id")==attempt && version==source+1)
                await Clear(id,attempt,source,ct);
            else
            {
                if(await new BffCircuit(store,clock).IsOpen(ct))throw new BffError("BFF_UNAVAILABLE",60);
                await BffStore.Execute(c,"UPDATE bff_sessions SET status='ReauthenticationRequired',sealed_tokens=NULL,uncertain_at=@now WHERE id=@id AND status='Active'",ct,("now",Now),("id",id));
                var lost=await CountProbeLoss(c,id,ct);
                await tx.CommitAsync(ct);if(lost)ProbeLost.Add(1);Unknown.Add(1);throw new BffError("REAUTHENTICATION_REQUIRED");
            }
        }
        if(!Due(row,activity)){Coalesced.Add(1);await tx.CommitAsync(ct);return Access(row);}
        if(journal.Has("retry_not_before") && journal.Time("retry_not_before")>Now)
            throw new BffError("BFF_BUSY",Math.Max(1,(int)Math.Ceiling((journal.Time("retry_not_before")-Now).TotalSeconds)));
        var operation=Guid.NewGuid();var dispatched=false;var commitAttempted=false;
        try
        {
            await Observe(RefreshStage.BeforeDispatch,ct);
            var permit=await new BffCircuit(store,clock).Acquire(id,ct);
            if(!permit.Allowed)throw new BffError("BFF_UNAVAILABLE",60);
            await using(var marker=await store.Source.OpenConnectionAsync(ct))
            {
                await using var markerTx=await marker.BeginTransactionAsync(ct);
                var changed=await BffStore.Execute(marker,"""
                    UPDATE bff_refresh_journal SET attempt_id=@op,source_version=@version,probe_dispatch=@probe,probe_loss_recorded=false,probe_uncertain=true,
                     first_dispatched_at=coalesce(first_dispatched_at,@now),last_dispatched_at=@now
                    WHERE session_id=@id AND attempt_id IS NULL
                    """,ct,("op",operation),("version",version),("now",Now),("id",id),("probe",permit.Probe));
                if(changed!=1)throw new InvalidOperationException("Dispatch journal mismatch");
                await markerTx.CommitAsync(ct);dispatched=true;Dispatches.Add(1);
            }
            await Observe(RefreshStage.DispatchCommitted,ct);
            using var http=CancellationTokenSource.CreateLinkedTokenSource(ct);http.CancelAfter(limits.Http);
            var successor=await identity.Refresh(Tokens(row).RefreshToken,activity==BffActivity.UserAction,Subject(row.Get<Guid>("identity_session_id")),http.Token).WaitAsync(http.Token);
            await Observe(RefreshStage.ResponseReceived,ct);
            if(successor.SessionId!=row.Get<Guid>("identity_session_id") || successor.ExpiresAt!=row.Time("expires_at")
                || successor.AccessExpiresAt<=Now.AddSeconds(2) || successor.AccessExpiresAt>successor.ExpiresAt
                || successor.RefreshToken is not {Length:>=1 and <=180} || successor.RefreshToken==Tokens(row).RefreshToken
                || successor.AccessToken is not {Length:>=1 and <=4096})throw new InvalidOperationException("Invalid successor");
            await new BffCircuit(store,clock).Record(id,200,false,ct);
            await BffStore.Execute(c,"""
                UPDATE bff_sessions SET version=@next,sealed_tokens=@tokens,access_issued_at=@now,access_expires_at=@access,
                 last_foreground_at=CASE WHEN @fg THEN @now ELSE last_foreground_at END,completed_attempt_id=@op WHERE id=@id
                """,ct,("next",version+1),("tokens",secrets.Seal(JsonSerializer.Serialize(successor),Purpose(id,version+1))),
                ("now",Now),("access",successor.AccessExpiresAt),("fg",activity==BffActivity.UserAction),("op",operation),("id",id));
            await Observe(RefreshStage.SuccessorWritten,ct);
            commitAttempted=true;await tx.CommitAsync(ct);
            await Observe(RefreshStage.SuccessorCommitted,ct);
            await Clear(id,operation,version,ct);
            return new(successor.AccessToken,successor.SessionId,version+1,false);
        }
        catch(RefreshNotProcessed rejection) when(dispatched && !commitAttempted)
        {
            // B holds the durable retry deadline and CAS-clears ONLY this dispatch.
            // A still holds the session lock. Tokens/version/foreground remain unchanged.
            using var cleanup=new CancellationTokenSource(cleanupBudget);
            await using var marker=await store.Source.OpenConnectionAsync(cleanup.Token);
            var streak=Math.Min(6,journal.Get<int>("admission_rejections")+1);
            var retry=Math.Max(rejection.RetryAfterSeconds,Math.Min(30,1<<(streak-1)));
            var cleared=await BffStore.Execute(marker,"""
                UPDATE bff_refresh_journal SET attempt_id=NULL,source_version=NULL,retry_not_before=@retry,admission_rejections=@streak
                WHERE session_id=@id AND attempt_id=@op AND source_version=@version
                """,cleanup.Token,("retry",Now.AddSeconds(retry)),("streak",streak),("id",id),("op",operation),("version",version));
            if(cleared!=1)throw new BffError("REAUTHENTICATION_REQUIRED");
            if(rejection.Configuration)
            {
                await new BffCircuit(store,clock).Record(id,403,true,cleanup.Token);
                throw new BffError("BFF_UNAVAILABLE",60);
            }
            throw new BffError("BFF_BUSY",retry);
        }
        catch(RefreshReceiptUnverified rejection) when(dispatched && !commitAttempted)
        {
            using var cleanup=new CancellationTokenSource(cleanupBudget);
            await new BffCircuit(store,clock).Record(id,rejection.Status,rejection.Status!=403,cleanup.Token);
            throw new BffError("BFF_UNAVAILABLE",60); // Unproven receipt: preserve/quarantine, never CAS-clear.
        }
        catch(Exception failure) when(dispatched)
        {
            if(failure is HttpRequestException {StatusCode:System.Net.HttpStatusCode.Unauthorized} || failure is ApiError {Status:401})
            {
                using var certainty=new CancellationTokenSource(cleanupBudget);
                await using var evidence=await store.Source.OpenConnectionAsync(certainty.Token);
                await BffStore.Execute(evidence,"UPDATE bff_refresh_journal SET probe_uncertain=false WHERE session_id=@id AND attempt_id=@op AND source_version=@version",certainty.Token,("id",id),("op",operation),("version",version));
            }
            if(!commitAttempted && failure is HttpRequestException {StatusCode:var status} && status is not null)
            {
                using var circuitBudget=new CancellationTokenSource(cleanupBudget);
                var code=(int)status.Value;
                if(await new BffCircuit(store,clock).Record(id,code,false,circuitBudget.Token))
                    throw new BffError("BFF_UNAVAILABLE",60); // Keep unmatched marker and tokens quarantined, never retry.
                if(code==403)throw new BffError("BFF_UNAVAILABLE",60);
            }
            if(!commitAttempted)
            {
                using var checkBudget=new CancellationTokenSource(cleanupBudget);
                if(await new BffCircuit(store,clock).IsOpen(checkBudget.Token))throw new BffError("BFF_UNAVAILABLE",60);
            }
            // Never overwrite a possibly committed successor after losing its commit acknowledgment.
            // If commit was not attempted, use reserved owner time to terminalize the local cookie.
            if(!commitAttempted)
            {
                using var cleanup=new CancellationTokenSource(cleanupBudget);
                try
                {
                    var uncertain=failure is not HttpRequestException {StatusCode:System.Net.HttpStatusCode.Unauthorized}
                        && failure is not ApiError {Status:401};
                    await BffStore.Execute(c,"UPDATE bff_sessions SET status='ReauthenticationRequired',sealed_tokens=NULL,uncertain_at=@unknown WHERE id=@id AND status='Active'",
                        cleanup.Token,("unknown",uncertain?Now:null),("id",id));
                    var lost=await CountProbeLoss(c,id,cleanup.Token);
                    await tx.CommitAsync(cleanup.Token);if(lost)ProbeLost.Add(1);if(uncertain)Unknown.Add(1);
                }
                catch(Exception) { /* Broken/aborted transaction: durable journal is the fallback. */ }
            }
            // No retry and no marker clearing. A later lock owner resolves an unmatched marker.
            throw new BffError("REAUTHENTICATION_REQUIRED");
        }
    }
    private string Subject(Guid session)=>Convert.ToBase64String(secrets.Mac("bff-admission-session-v1",session.ToString("D"))).TrimEnd('=').Replace('+','-').Replace('/','_');
    private async Task Clear(Guid id,Guid attempt,long version,CancellationToken cancellation)
    {
        await using var c=await store.Source.OpenConnectionAsync(cancellation);
        await BffStore.Execute(c,"UPDATE bff_refresh_journal SET attempt_id=NULL,source_version=NULL,retry_not_before=NULL,admission_rejections=0 WHERE session_id=@id AND attempt_id=@op AND source_version=@version",cancellation,("id",id),("op",attempt),("version",version));
    }
    private static async Task<bool> CountProbeLoss(NpgsqlConnection c,Guid id,CancellationToken cancellation)=>
        await BffStore.Execute(c,"UPDATE bff_refresh_journal SET probe_loss_recorded=true WHERE session_id=@id AND probe_dispatch AND probe_uncertain AND NOT probe_loss_recorded",cancellation,("id",id))==1;
    private static string LogoutPurpose(Guid id,long version)=>"bff-logout-access:"+id+":"+version.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public async Task Logout(string cookie,CancellationToken cancellation=default)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(cancellation);budget.CancelAfter(limits.LockWait+limits.Owner);
        var id=await RequestLogout(cookie,budget.Token);
        if(id is not null)await CompleteLogout(id.Value,budget.Token);
    }
    // Browser response may acknowledge only this durable local intent, before any upstream work.
    public async Task<Guid?> RequestLogout(string cookie,CancellationToken cancellation=default)
    {
        if(identity is not IIdentityLogout)throw new InvalidOperationException("Identity logout adapter required");
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(cancellation);budget.CancelAfter(limits.LockWait+limits.Owner);
        Guid id;
        await using(var c=await store.Source.OpenConnectionAsync(budget.Token))
        {
            await using var tx=await c.BeginTransactionAsync(budget.Token);
            await BffStore.Execute(c,"SELECT set_config('lock_timeout',@wait,true)",budget.Token,("wait",((long)limits.LockWait.TotalMilliseconds).ToString()+"ms"));
            Row? row;
            try{row=await BffStore.One(c,"SELECT * FROM bff_sessions WHERE cookie_mac=@cookie FOR UPDATE",budget.Token,("cookie",Cookie(cookie)));}
            catch(Exception e)when(e is PostgresException {SqlState:"55P03"} || e is OperationCanceledException && !cancellation.IsCancellationRequested)
            {Busy.Add(1);throw new BffError("BFF_BUSY",1);}
            if(row is null || row.Get<string>("status") is not ("Active" or "LogoutRequested")){await tx.CommitAsync(budget.Token);return null;}
            id=row.Get<Guid>("id");
            if(row.Get<string>("status")=="Active")
                await BffStore.Execute(c,"UPDATE bff_sessions SET status='LogoutRequested',sealed_tokens=NULL,sealed_logout_access=@access,logout_retry_at=@now WHERE id=@id",budget.Token,
                    ("id",id),("now",Now),("access",secrets.Seal(Tokens(row).AccessToken,LogoutPurpose(id,row.Get<long>("version")))));
            await tx.CommitAsync(budget.Token);
        }
        return id;
    }
    // Trusted server/session id, not a browser assertion. Cookie is unnecessary after durable intent.
    public async Task CompleteLogout(Guid id,CancellationToken cancellation=default)
    {
        if(identity is not IIdentityLogout logout)throw new InvalidOperationException("Identity logout adapter required");
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(cancellation);budget.CancelAfter(limits.Owner);
        await using var c=await store.Source.OpenConnectionAsync(budget.Token);await using var tx=await c.BeginTransactionAsync(budget.Token);
        await BffStore.Execute(c,"SELECT set_config('transaction_timeout',@owner,true)",budget.Token,("owner",((long)limits.Owner.TotalMilliseconds).ToString()+"ms"));
        var row=await BffStore.One(c,"SELECT * FROM bff_sessions WHERE id=@id FOR UPDATE SKIP LOCKED",budget.Token,("id",id));
        if(row is null || row.Get<string>("status")!="LogoutRequested"){await tx.CommitAsync(budget.Token);return;}
        if(row.Time("expires_at")<=Now)
        {
            await FinishLogout(c,id,budget.Token);await tx.CommitAsync(budget.Token);
            LogoutLocalOnly.Add(1,new KeyValuePair<string,object?>("reason","absolute_expiry"));return;
        }
        // Convert legacy 004 intents before network work, permanently removing their refresh token.
        if(!row.Has("sealed_logout_access"))
        {
            var access=secrets.Seal(Tokens(row).AccessToken,LogoutPurpose(id,row.Get<long>("version")));
            await BffStore.Execute(c,"UPDATE bff_sessions SET sealed_tokens=NULL,sealed_logout_access=@access WHERE id=@id",budget.Token,("id",id),("access",access));
            await tx.CommitAsync(budget.Token);
            await CompleteLogout(id,budget.Token);return;
        }
        if(row.Has("logout_retry_at") && row.Time("logout_retry_at")>Now){await tx.CommitAsync(budget.Token);return;}
        using var http=CancellationTokenSource.CreateLinkedTokenSource(budget.Token);http.CancelAfter(limits.Http);
        var localOnly=false;
        try
        {
            var access=secrets.Open(row.Get<byte[]>("sealed_logout_access"),LogoutPurpose(id,row.Get<long>("version")));
            var session=row.Get<Guid>("identity_session_id");
            await logout.Logout(access,session,Subject(session),http.Token).WaitAsync(http.Token);
        }
        catch(HttpRequestException e) when(e.StatusCode==System.Net.HttpStatusCode.Unauthorized)
        {localOnly=true; /* Local closure does not certify upstream revocation with expired proof. */ }
        catch(Exception)
        {
            // Commit retry state even if the caller/network deadline has elapsed. Never restore ordinary access.
            using var cleanup=new CancellationTokenSource(TimeSpan.FromTicks(limits.Owner.Ticks/5));
            var attempts=Math.Min(30,row.Get<int>("logout_attempts")+1);var delay=Math.Min(60,1<<Math.Min(6,attempts-1));
            await BffStore.Execute(c,"UPDATE bff_sessions SET logout_attempts=@attempts,logout_retry_at=@retry WHERE id=@id",cleanup.Token,("attempts",attempts),("retry",Now.AddSeconds(delay)),("id",id));
            await tx.CommitAsync(cleanup.Token);throw new BffError("BFF_UNAVAILABLE",delay);
        }
        await FinishLogout(c,id,budget.Token);await tx.CommitAsync(budget.Token);
        if(localOnly)LogoutLocalOnly.Add(1,new KeyValuePair<string,object?>("reason","access_proof_rejected"));
    }
    private static Task<int> FinishLogout(NpgsqlConnection c,Guid id,CancellationToken cancellation)=>
        BffStore.Execute(c,"UPDATE bff_sessions SET status='Closed',sealed_tokens=NULL,sealed_logout_access=NULL,logout_retry_at=NULL WHERE id=@id",cancellation,("id",id));
    public async Task<int> SweepLogout(CancellationToken cancellation=default)
    {
        var ids=new List<Guid>();
        await using(var c=await store.Source.OpenConnectionAsync(cancellation))
        await using(var command=new NpgsqlCommand("SELECT id FROM bff_sessions WHERE status='LogoutRequested' AND (expires_at<=@now OR logout_retry_at IS NULL OR logout_retry_at<=@now) ORDER BY expires_at,id LIMIT 20",c))
        {
            command.Parameters.AddWithValue("now",Now);await using var reader=await command.ExecuteReaderAsync(cancellation);
            while(await reader.ReadAsync(cancellation))ids.Add(reader.GetGuid(0));
        }
        foreach(var id in ids)
        {
            cancellation.ThrowIfCancellationRequested();
            try{await CompleteLogout(id,cancellation);}catch(BffError e)when(e.Code=="BFF_UNAVAILABLE"){}
        }
        return ids.Count;
    }
    // Host calls this only for a verified Identity Session 401, never for BFF credential 403.
    public async Task RejectAccess(string cookie,Guid expectedSession,CancellationToken cancellation=default)
    {
        await using var c=await store.Source.OpenConnectionAsync(cancellation);await using var tx=await c.BeginTransactionAsync(cancellation);
        var row=await BffStore.One(c,"SELECT id,identity_session_id,status FROM bff_sessions WHERE cookie_mac=@cookie FOR UPDATE",cancellation,("cookie",Cookie(cookie)));
        if(row is not null && row.Get<string>("status")=="Active" && row.Get<Guid>("identity_session_id")==expectedSession)
        {
            if(await new BffCircuit(store,clock).Record(row.Get<Guid>("id"),401,false,cancellation))throw new BffError("BFF_UNAVAILABLE",60);
            await BffStore.Execute(c,"UPDATE bff_sessions SET status='ReauthenticationRequired',sealed_tokens=NULL WHERE id=@id AND status='Active'",cancellation,("id",row.Get<Guid>("id")));
        }
        await tx.CommitAsync(cancellation);
    }
    // Local-only cleanup; browser explicit logout must call Logout instead.
    public async Task Close(string cookie,CancellationToken cancellation=default)
    {
        await using var c=await store.Source.OpenConnectionAsync(cancellation);await using var tx=await c.BeginTransactionAsync(cancellation);
        var row=await BffStore.One(c,"SELECT id,status FROM bff_sessions WHERE cookie_mac=@cookie FOR UPDATE",cancellation,("cookie",Cookie(cookie)));
        if(row is not null && row.Get<string>("status")=="Active")
            await BffStore.Execute(c,"UPDATE bff_sessions SET status='Closed',sealed_tokens=NULL WHERE id=@id",cancellation,("id",row.Get<Guid>("id")));
        await tx.CommitAsync(cancellation);
    }
}
