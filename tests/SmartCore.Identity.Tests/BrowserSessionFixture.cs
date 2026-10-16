using System.Text.Json;
using SmartCore.Identity;
using SmartCore.Identity.Bff;

// Disposable test executable, never a host/operator HTTP route. All IPC output is captured by the runner.
internal static class BrowserSessionFixture
{
    private sealed record Configuration(string Connection, string MacKey, string MaterialKey, string Status, Guid IdentitySession, string? Cookie = null);
    private sealed class Clock(DateTimeOffset now) : TimeProvider {public override DateTimeOffset GetUtcNow() => now;}
    private sealed class NoNetwork : IIdentityRefresh {public Task<SessionTokens> Refresh(string token,bool foreground,string subject,CancellationToken cancellation) => throw new Exception("Fixture must never call Identity");}
    public static async Task Run()
    {
        if(Environment.GetEnvironmentVariable("SMARTCORE_TEST_ALLOW_RESET") != "yes") throw new Exception("Disposable database opt-in required");
        var config=JsonSerializer.Deserialize<Configuration>((await Console.In.ReadLineAsync())!)!;
        if(config.Status is not ("Active" or "Closed" or "Expired" or "LogoutRequested" or "CloseExisting" or "LogoutExisting" or "Advance" or "HideLookup" or "RestoreLookup"))throw new Exception("Unknown fixture state");
        await using var store=new BffStore(config.Connection);await store.Migrate();
        if(config.Status is "HideLookup" or "RestoreLookup") {
            await using var sql=await store.Source.OpenConnectionAsync();
            await sql.Execute(config.Status=="HideLookup" ? "ALTER TABLE bff_sessions RENAME COLUMN cookie_mac TO cookie_mac_guard_probe" : "ALTER TABLE bff_sessions RENAME COLUMN cookie_mac_guard_probe TO cookie_mac");
            Console.WriteLine(JsonSerializer.Serialize(new {Probe=config.Status}));return;
        }
        var secrets=new Secrets(config.MacKey,config.MaterialKey);
        var now=DateTimeOffset.UtcNow.AddHours(config.Status=="Expired"?-2:0);
        var core=new BffRefresh(store,secrets,new Clock(now),new NoNetwork());
        if(config.Status is "CloseExisting" or "LogoutExisting" or "Advance") {
            if(!Input.Secret(config.Cookie))throw new Exception("Fixture cookie required");
            await using var sql=await store.Source.OpenConnectionAsync();
            var prior=(await sql.One("SELECT id,version,sealed_tokens FROM bff_sessions WHERE cookie_mac=@cookie AND status='Active'",("cookie",secrets.Mac("bff-cookie-v1",config.Cookie!))))!;
            var priorId=prior.Get<Guid>("id");var version=prior.Get<long>("version");
            if(config.Status=="CloseExisting")await core.Close(config.Cookie!);
            else if(config.Status=="Advance") {
                var tokens=secrets.Open(prior.Get<byte[]>("sealed_tokens"),"bff-tokens:"+priorId+":"+version);
                await sql.Execute("UPDATE bff_sessions SET version=version+1,sealed_tokens=@tokens WHERE id=@id",("tokens",secrets.Seal(tokens,"bff-tokens:"+priorId+":"+(version+1))),("id",priorId));
            } else await sql.Execute("UPDATE bff_sessions SET status='LogoutRequested',sealed_tokens=NULL,sealed_logout_access=@proof,logout_retry_at=@retry WHERE id=@id",("proof",secrets.Seal(Secrets.Token(),"fixture-only")),("retry",DateTimeOffset.UtcNow.AddHours(1)),("id",priorId));
            Console.WriteLine(JsonSerializer.Serialize(new {Cookie=config.Cookie,SessionId=priorId}));return;
        }
        var cookie=await core.Create(new SessionTokens(config.IdentitySession,Secrets.Token(),Secrets.Token(),now.AddMinutes(20),now.AddMinutes(10)));
        await using var connection=await store.Source.OpenConnectionAsync();
        var row=(await connection.One("SELECT id FROM bff_sessions WHERE cookie_mac=@cookie",("cookie",secrets.Mac("bff-cookie-v1",cookie))))!;
        var id=row.Get<Guid>("id");
        if(config.Status=="Closed")await core.Close(cookie);
        if(config.Status=="LogoutRequested")await connection.Execute("""
            UPDATE bff_sessions SET status='LogoutRequested',sealed_tokens=NULL,sealed_logout_access=@proof,logout_retry_at=@retry WHERE id=@id
            """,("proof",secrets.Seal(Secrets.Token(),"fixture-only")),("retry",DateTimeOffset.UtcNow.AddHours(1)),("id",id));
        Console.WriteLine(JsonSerializer.Serialize(new {Cookie=cookie,SessionId=id}));
    }
}
