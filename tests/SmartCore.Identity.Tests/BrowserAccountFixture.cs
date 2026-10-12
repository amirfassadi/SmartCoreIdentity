using System.Text.Json;
using SmartCore.Identity;
using SmartCore.Identity.Bff;

// Private disposable CLI seam. No diagnostic HTTP route, no raw tokens returned to the browser.
internal static class BrowserAccountFixture
{
    private sealed record Configuration(string Connection, string IdentityConnection, string MacKey, string MaterialKey, string Action, string? Cookie);
    public static async Task Run()
    {
        if (Environment.GetEnvironmentVariable("SMARTCORE_TEST_ALLOW_RESET") != "yes") throw new Exception("Disposable opt-in required.");
        var tehran=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        foreach(var instant in new[]{"2026-01-01T12:00:00Z","2026-07-01T12:00:00Z"}) {
            var local=TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(instant,System.Globalization.CultureInfo.InvariantCulture),tehran);
            if(local.Hour!=15 || local.Minute!=30 || local.Offset!=TimeSpan.FromMinutes(210)) throw new Exception("Runtime Tehran timezone mismatch.");
        }
        var input = JsonSerializer.Deserialize<Configuration>((await Console.In.ReadLineAsync())!)!;
        await using var store = new BffStore(input.Connection);
        await using var db = new Database(input.IdentityConnection);
        var secrets = new Secrets(input.MacKey, input.MaterialKey);
        await using var sql = await store.Source.OpenConnectionAsync();
        var row = await sql.One("SELECT id,identity_session_id,status,version FROM bff_sessions WHERE cookie_mac=@mac", ("mac", secrets.Mac("bff-cookie-v1", input.Cookie!)));
        if (row is null) throw new Exception("Missing fixture session.");
        var id = row.Get<Guid>("id");
        if (input.Action == "Due")
            await sql.Execute("UPDATE bff_sessions SET access_issued_at=@issued,access_expires_at=@end WHERE id=@id", ("issued", DateTimeOffset.UtcNow.AddMinutes(-15)), ("end", DateTimeOffset.UtcNow.AddMinutes(-1)), ("id", id));
        else if (input.Action == "Revoke") {
            await using var identity = await db.Source.OpenConnectionAsync();
            await identity.Execute("UPDATE auth_sessions SET status='Closed',closed_at=@now,close_reason='Logout' WHERE id=@id", ("now", DateTimeOffset.UtcNow), ("id", row.Get<Guid>("identity_session_id")));
        } else if (input.Action != "Inspect") throw new Exception("Unknown fixture action.");
        await using var c = await db.Source.OpenConnectionAsync();
        var family = await c.One("SELECT current_generation FROM auth_refresh_families WHERE session_id=@id", ("id", row.Get<Guid>("identity_session_id")));
        Console.WriteLine(JsonSerializer.Serialize(new {Status=row.Get<string>("status"), Version=row.Get<long>("version"), Generation=family!.Get<long>("current_generation")}));
    }
}
