using System.Security.Cryptography;
using System.Text.Json;
using SmartCore.Identity;

var connection=Environment.GetEnvironmentVariable("SMARTCORE_TEST_DB") ?? throw new Exception("Set SMARTCORE_TEST_DB to a dedicated, disposable PostgreSQL database.");
if(Environment.GetEnvironmentVariable("SMARTCORE_TEST_ALLOW_RESET")!="yes") throw new Exception("Explicit disposable database reset opt-in required.");
await using var db=new Database(connection);
await db.Migrate();
await using var sql=await db.Source.OpenConnectionAsync();
await sql.Execute("TRUNCATE verification_sessions,persons,organizations,memberships,registrations,credentials,initial_credential_winners,workflow_jobs,event_outbox,delivery_outbox CASCADE");
var clock=new TestClock();
var secrets=new Secrets(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
var service=new RegistrationService(db,secrets,clock);
var worker=new Provisioning(db,secrets,clock);
int passed=0;
void Check(bool condition,string name) { if(!condition) throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name); passed++; }
async Task Error(Func<Task> action,string code)
{
    try {await action();} catch(ApiError e) when(e.Code==code) {return;}
    throw new Exception("Expected "+code);
}
StartRegistration Request(string? email=null,string? mobile=null) => new(email ?? (mobile is null ? Guid.NewGuid()+"@example.test":null),mobile,"correct horse battery staple", "Amir",Secrets.Token());
async Task<string> Code(string id)
{
    var row=await sql.One("SELECT * FROM delivery_outbox WHERE verification_id=@id AND sealed_code IS NOT NULL ORDER BY created_at DESC LIMIT 1",("id",id));
    return secrets.Open(row!.Get<byte[]>("sealed_code"),"delivery:"+row.Get<Guid>("id"));
}
async Task<long> Count(string table) => (await sql.One("SELECT count(*) AS n FROM "+table))!.Get<long>("n");

await Error(async()=>{await service.Start(Request() with {Mobile="+989121234567"},Secrets.Token());},"VALIDATION_FAILED");
await Error(async()=>{await service.Start(Request() with {Password="short"},Secrets.Token());},"VALIDATION_FAILED");
Check(await Count("persons")==0,"invalid input creates no ownership");
var request=Request(); var key=Secrets.Token(); var started=await service.Start(request,key); var code=await Code(started.VerificationSessionId);
Check(await Count("persons")==0 && await Count("organizations")==0 && await Count("credentials")==0,"initiation creates no domain records");
Check((await service.Start(request,key))==started && await Count("delivery_outbox")==1,"identical start replay does not resend or extend expiry");
await Error(async()=>{await service.Start(request with {Password="another safe password"},key);},"IDEMPOTENCY_CONFLICT");
Check(true,"changed idempotent request rejected");
var verify=new VerifyRegistration(started.VerificationSessionId,code,request.BindingSecret);
await Error(async()=>{await service.Verify(verify with {BindingSecret=Secrets.Token()});},"VERIFICATION_FAILED");
Check((await sql.One("SELECT attempts FROM verification_sessions WHERE id=@id",("id",started.VerificationSessionId)))!.Get<int>("attempts")==1,"wrong binding commits an attempt");
var result=await service.Verify(verify);
Check(result.Created && result.Result.Status=="PendingCredential" && await Count("persons")==1 && await Count("organizations")==1 && await Count("memberships")==1,"atomic ownership triple commits pending");
var replay=await service.Verify(verify);
Check(!replay.Created && replay.Result==result.Result && await Count("persons")==1,"lost ownership response replays same registration");
Check((await sql.One("SELECT sealed_password FROM verification_sessions WHERE id=@id",("id",started.VerificationSessionId)))!.Has("sealed_password")==false,"material ownership transfers out of verification");
Check(await Count("event_outbox")==2 && await Count("workflow_jobs")==1,"ownership events and provisioning work commit with ownership");
await worker.EnsureCredential(result.Result.RegistrationId);
await worker.EnsureCredential(result.Result.RegistrationId);
Check(await Count("credentials")==1 && (await RegistrationService.Result(sql,result.Result.RegistrationId)).Status=="PendingCredential","credential commit replay preserves one winner before Ready");
clock.Advance(TimeSpan.FromSeconds(1));
await worker.MarkReady(result.Result.RegistrationId);
var ready=await RegistrationService.Result(sql,result.Result.RegistrationId);
await worker.MarkReady(result.Result.RegistrationId);
Check(ready.Status=="Ready" && ready.ReadyAt>ready.OwnershipCommittedAt && await Count("event_outbox")==3,"Ready has distinct timestamp and exactly one logical event");
await worker.Acknowledge(result.Result.RegistrationId);
await worker.Acknowledge(result.Result.RegistrationId);
Check((await sql.One("SELECT phase FROM initial_credential_winners WHERE registration_id=@id",("id",ready.RegistrationId)))!.Get<string>("phase")=="ReadyAcknowledged","acknowledgment replay is durable");
await worker.Tick();
Check((await sql.One("SELECT completed FROM workflow_jobs WHERE registration_id=@id",("id",ready.RegistrationId)))!.Get<bool>("completed"),"worker reconciles after crashes between every phase");

var duplicate=await service.Start(request with {BindingSecret=Secrets.Token()},Secrets.Token());
var duplicateRow=await sql.One("SELECT binding_mac FROM verification_sessions WHERE id=@id",("id",duplicate.VerificationSessionId));
// Use a known new binding to test a genuinely new verified conflicting attempt.
var duplicateRequest=request with {BindingSecret=Secrets.Token()};
var duplicate2=await service.Start(duplicateRequest,Secrets.Token());
await Error(async()=>{await service.Verify(new(duplicate2.VerificationSessionId,await Code(duplicate2.VerificationSessionId),duplicateRequest.BindingSecret));},"CONTACT_UNAVAILABLE");
Check(await Count("persons")==1 && !(await sql.One("SELECT sealed_password FROM verification_sessions WHERE id=@id",("id",duplicate2.VerificationSessionId)))!.Has("sealed_password"),"verified conflict cannot replace password and erases losing material");

var mobileRequest=Request(mobile:"+989123456789"); var mobile=await service.Start(mobileRequest,Secrets.Token());
var mobileResult=await service.Verify(new(mobile.VerificationSessionId,await Code(mobile.VerificationSessionId),mobileRequest.BindingSecret));
await worker.Tick();
var mobileEvent=(await sql.One("SELECT payload::text AS payload FROM event_outbox WHERE registration_id=@id AND event_type='PersonRegistered'",("id",mobileResult.Result.RegistrationId)))!;
using(var json=JsonDocument.Parse(mobileEvent.Get<string>("payload")))
    Check(!json.RootElement.GetProperty("Payload").TryGetProperty("Email",out _) && !json.RootElement.GetProperty("Payload").TryGetProperty("Mobile",out _),"mobile-only registration emits schema-compatible optional contact");

var limitedRequest=Request(); var limited=await service.Start(limitedRequest,Secrets.Token()); var correct=await Code(limited.VerificationSessionId);
for(var i=0;i<5;i++) await Error(async()=>{await service.Verify(new(limited.VerificationSessionId,correct=="000000"?"000001":"000000",limitedRequest.BindingSecret));},"VERIFICATION_FAILED");
await Error(async()=>{await service.Verify(new(limited.VerificationSessionId,correct,limitedRequest.BindingSecret));},"VERIFICATION_FAILED");
Check((await sql.One("SELECT attempts FROM verification_sessions WHERE id=@id",("id",limited.VerificationSessionId)))!.Get<int>("attempts")==5,"attempt budget survives failed requests and blocks correct sixth attempt");

var resendRequest=Request(); var resend=await service.Start(resendRequest,Secrets.Token());
clock.Advance(TimeSpan.FromSeconds(61));
await service.Resend(new(resend.VerificationSessionId,resendRequest.BindingSecret));
var resendRow=(await sql.One("SELECT * FROM verification_sessions WHERE id=@id",("id",resend.VerificationSessionId)))!;
Check(resendRow.Time("expires_at")==resend.ExpiresAt && resendRow.Get<int>("resends")==1,"resend preserves original expiry and cumulative counters");
var resendProof=await Code(resend.VerificationSessionId);
clock.Advance(TimeSpan.FromMinutes(10));
await Error(async()=>{await service.Verify(new(resend.VerificationSessionId,resendProof,resendRequest.BindingSecret));},"VERIFICATION_FAILED");
await worker.Tick();
var expired=(await sql.One("SELECT * FROM verification_sessions WHERE id=@id",("id",resend.VerificationSessionId)))!;
Check(!expired.Has("code_mac") && !expired.Has("binding_mac") && !expired.Has("request_mac") && !expired.Has("sealed_password"),"expired proof and password-derived replay material erased");

// Force failure after Person/Organization writes: PostgreSQL transaction must undo all writes.
var rollbackRequest=Request(); var rollback=await service.Start(rollbackRequest,Secrets.Token()); var rollbackCode=await Code(rollback.VerificationSessionId);
var before=await Count("persons");
await sql.Execute("CREATE OR REPLACE FUNCTION test_fail_membership() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected'; END $$; CREATE TRIGGER test_failure BEFORE INSERT ON memberships FOR EACH ROW EXECUTE FUNCTION test_fail_membership()");
try { await service.Verify(new(rollback.VerificationSessionId,rollbackCode,rollbackRequest.BindingSecret)); throw new Exception("Expected fault"); }
catch(Npgsql.PostgresException) { }
finally { await sql.Execute("DROP TRIGGER test_failure ON memberships; DROP FUNCTION test_fail_membership()"); }
Check(await Count("persons")==before && await Count("organizations")==before && await Count("registrations")==before,"mid-transaction fault rolls back entire ownership workflow");
var recovered=await service.Verify(new(rollback.VerificationSessionId,rollbackCode,rollbackRequest.BindingSecret));
Check(recovered.Created,"same proof recovers after actual transaction rollback");

// Native PostgreSQL CI runs this across independent connections. PGlite is preliminary evidence only.
var raceRequest=Request(); var race=await service.Start(raceRequest,Secrets.Token()); var raceProof=new VerifyRegistration(race.VerificationSessionId,await Code(race.VerificationSessionId),raceRequest.BindingSecret);
var raced=await Task.WhenAll(service.Verify(raceProof),service.Verify(raceProof));
Check(raced.Count(x=>x.Created)==1 && raced[0].Result.RegistrationId==raced[1].Result.RegistrationId,"concurrent valid confirmations produce one registration");
await worker.Tick();
var events=await sql.Rows("SELECT payload::text AS payload FROM event_outbox ORDER BY occurred_at,id");
var eventFile=Environment.GetEnvironmentVariable("SMARTCORE_EVENT_FIXTURES");
if(eventFile is not null) await File.WriteAllTextAsync(eventFile,"["+string.Join(',',events.Select(x=>x.Get<string>("payload")))+"]");
Console.WriteLine($"RESULT: {passed} checks passed. Backend: {Environment.GetEnvironmentVariable("SMARTCORE_TEST_ENGINE") ?? "PostgreSQL (caller-provided)"}");

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now=DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow()=>now;
    public void Advance(TimeSpan duration)=>now+=duration;
}
