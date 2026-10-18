using System.Text.Json;
using SmartCore.Identity;

// Explicit disposable CLI only. Proof/IDs are carried over private pipes, never HTTP diagnostics.
internal static class BrowserRegistrationFixture
{
    private sealed record Configuration(string Connection, string MacKey, string MaterialKey, string Action, string Contact);
    public static async Task Run()
    {
        if (Environment.GetEnvironmentVariable("SMARTCORE_TEST_ALLOW_RESET") != "yes") throw new Exception("Disposable opt-in required.");
        var input = JsonSerializer.Deserialize<Configuration>((await Console.In.ReadLineAsync())!)!;
        await using var db = new Database(input.Connection);
        var secrets = new Secrets(input.MacKey,input.MaterialKey);
        await using var sql = await db.Source.OpenConnectionAsync();
        if (input.Action == "SeedPending") {
            var service = new RegistrationService(db,secrets,TimeProvider.System);
            var binding = Secrets.Token();
            var start = await service.Start(new(input.Contact,null,"private abandoned registration password","Pending fixture",binding),Secrets.Token());
            var delivery = await sql.One("SELECT id,sealed_code FROM delivery_outbox WHERE verification_id=@id",("id",start.VerificationSessionId));
            var code = secrets.Open(delivery!.Get<byte[]>("sealed_code"),"delivery:"+delivery.Get<Guid>("id"));
            await service.Verify(new(start.VerificationSessionId,code,binding));
            // Expire original material so a distinct setup candidate can recover the unfinished registration.
            await sql.Execute("UPDATE registrations SET material_expires_at=@past WHERE person_id IN (SELECT id FROM persons WHERE contact=@contact)",("past",DateTimeOffset.UtcNow.AddMinutes(-1)),("contact",input.Contact));
            Console.WriteLine("{}");return;
        }
        if (input.Action is "Provision" or "ProvisionOriginal") {
            if (input.Action == "ProvisionOriginal") await sql.Execute("UPDATE registrations SET material_expires_at=@future WHERE person_id IN (SELECT id FROM persons WHERE contact=@contact)",("future",DateTimeOffset.UtcNow.AddMinutes(5)),("contact",input.Contact));
            var registration = await sql.One("SELECT r.id FROM registrations r JOIN persons p ON p.id=r.person_id WHERE p.contact=@contact",("contact",input.Contact));
            var worker = new Provisioning(db,secrets,TimeProvider.System);
            await worker.EnsureCredential(registration!.Get<Guid>("id"));
            await worker.MarkReady(registration.Get<Guid>("id"));
            await worker.Acknowledge(registration.Get<Guid>("id"));
            Console.WriteLine("{}");return;
        }
        var row = await sql.One("SELECT id,attempts FROM verification_sessions WHERE contact=@contact ORDER BY created_at DESC LIMIT 1",("contact",input.Contact));
        if (row is null) throw new Exception("Missing verification fixture.");
        var id = row.Get<string>("id");
        if (input.Action == "AllowResend") await sql.Execute("UPDATE verification_sessions SET last_sent_at=@past WHERE id=@id",("past",DateTimeOffset.UtcNow.AddMinutes(-2)),("id",id));
        else if (input.Action == "Expire") await sql.Execute("UPDATE verification_sessions SET expires_at=@past WHERE id=@id",("past",DateTimeOffset.UtcNow.AddMinutes(-1)),("id",id));
        else if (input.Action != "Inspect") throw new Exception("Unknown fixture action.");
        var setup = await sql.One("SELECT id FROM setup_challenges WHERE verification_id=@id",("id",id));
        var count = await sql.One("SELECT count(*) AS n FROM delivery_outbox WHERE verification_id=@id",("id",id));
        Console.WriteLine(JsonSerializer.Serialize(new {Verification=id,Attempts=row.Get<int>("attempts"),Deliveries=count!.Get<long>("n"),Setup=setup?.Get<string>("id")}));
    }
}
