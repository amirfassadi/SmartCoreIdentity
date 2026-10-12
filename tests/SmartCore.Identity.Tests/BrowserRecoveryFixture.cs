using System.Text.Json;
using SmartCore.Identity;

internal static class BrowserRecoveryFixture
{
    private sealed record Configuration(string Connection,string MacKey,string MaterialKey,string SigningKey,string Action,string Contact);
    public static async Task Run()
    {
        if(Environment.GetEnvironmentVariable("SMARTCORE_TEST_ALLOW_RESET")!="yes")throw new Exception("Disposable opt-in required.");
        var input=JsonSerializer.Deserialize<Configuration>((await Console.In.ReadLineAsync())!)!;
        await using var db=new Database(input.Connection);await using var sql=await db.Source.OpenConnectionAsync();
        var person=await sql.One("SELECT id FROM persons WHERE contact=@contact",("contact",input.Contact));
        if(person is null)throw new Exception("Missing private recovery fixture.");
        var id=person.Get<Guid>("id");
        var latest=await sql.One("SELECT id,stage FROM auth_credential_change_intents WHERE person_id=@id ORDER BY created_at DESC LIMIT 1",("id",id));
        if(input.Action=="Due") {
            var changed=await sql.Execute("UPDATE bff_sessions SET access_issued_at=@issued,access_expires_at=@expiry WHERE status='Active' AND identity_session_id IN (SELECT id FROM auth_sessions WHERE person_id=@person AND status='Active')",
                ("issued",DateTimeOffset.UtcNow.AddMinutes(-15)),("expiry",DateTimeOffset.UtcNow.AddMinutes(-1)),("person",id));
            if(changed!=1)throw new Exception("Expected one private active browser session.");
        } else if(input.Action=="Process") {
            if(latest is null)throw new Exception("Missing accepted private mutation.");
            var secrets=new Secrets(input.MacKey,input.MaterialKey);
            var changes=new PasswordChanges(db,secrets,TimeProvider.System,new PostgresAuthenticationIssuanceGate(),new AccessTokens(input.SigningKey,TimeProvider.System),new CredentialChanges(db,secrets,TimeProvider.System));
            await changes.Process(latest.Get<Guid>("id"));
            latest=await sql.One("SELECT id,stage FROM auth_credential_change_intents WHERE id=@id",("id",latest.Get<Guid>("id")));
        } else if(input.Action!="Inspect")throw new Exception("Unknown recovery fixture action.");
        var reset=await sql.One("SELECT id FROM auth_reset_challenges WHERE person_id=@id ORDER BY created_at DESC LIMIT 1",("id",id));
        var recovery=await sql.One("SELECT verifier FROM auth_recovery_codes WHERE person_id=@id",("id",id));
        var sessions=await sql.One("SELECT count(*) AS n FROM auth_sessions WHERE person_id=@id AND status='Active'",("id",id));
        var generation=await sql.One("SELECT coalesce(max(f.current_generation),0) AS n FROM auth_refresh_families f JOIN auth_sessions s ON s.id=f.session_id WHERE s.person_id=@id",("id",id));
        Console.WriteLine(JsonSerializer.Serialize(new {Generation=generation!.Get<long>("n"),Challenge=reset?.Get<string>("id"),Stage=latest?.Get<string>("stage"),HasRecovery=recovery?.Has("verifier") ?? false,ActiveSessions=sessions!.Get<long>("n")}));
    }
}
