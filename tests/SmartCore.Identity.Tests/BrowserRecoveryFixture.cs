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
        if(input.Action=="Process") {
            if(latest is null)throw new Exception("Missing accepted private mutation.");
            var secrets=new Secrets(input.MacKey,input.MaterialKey);
            var changes=new PasswordChanges(db,secrets,TimeProvider.System,new PostgresAuthenticationIssuanceGate(),new AccessTokens(input.SigningKey,TimeProvider.System),new CredentialChanges(db,secrets,TimeProvider.System));
            await changes.Process(latest.Get<Guid>("id"));
            latest=await sql.One("SELECT id,stage FROM auth_credential_change_intents WHERE id=@id",("id",latest.Get<Guid>("id")));
        } else if(input.Action!="Inspect")throw new Exception("Unknown recovery fixture action.");
        var reset=await sql.One("SELECT id FROM auth_reset_challenges WHERE person_id=@id ORDER BY created_at DESC LIMIT 1",("id",id));
        var recovery=await sql.One("SELECT verifier FROM auth_recovery_codes WHERE person_id=@id",("id",id));
        var sessions=await sql.One("SELECT count(*) AS n FROM auth_sessions WHERE person_id=@id AND status='Active'",("id",id));
        Console.WriteLine(JsonSerializer.Serialize(new {Challenge=reset?.Get<string>("id"),Stage=latest?.Get<string>("stage"),HasRecovery=recovery?.Has("verifier") ?? false,ActiveSessions=sessions!.Get<long>("n")}));
    }
}
