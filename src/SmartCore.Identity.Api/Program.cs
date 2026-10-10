using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using SmartCore.Identity;

var builder=WebApplication.CreateBuilder(args);
var connection=builder.Configuration.GetConnectionString("Identity") ?? throw new InvalidOperationException("ConnectionStrings__Identity is required.");
builder.Services.AddSingleton(new Database(connection));
if(args.Contains("--migrate"))
{
    await using var migrationDb=new Database(connection);
    await migrationDb.Migrate();
    return;
}
// This delivery has a deliberately enforced test-only launch boundary.
if(!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    throw new InvalidOperationException("Public deployment is disabled until delivery, recovery, session and release gates are implemented.");
builder.WebHost.ConfigureKestrel(options=>options.Limits.MaxRequestBodySize=16384);
builder.Services.AddSingleton(new Secrets(builder.Configuration["Identity:MacKey"] ?? "",builder.Configuration["Identity:MaterialKey"] ?? ""));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RegistrationService>();
builder.Services.AddSingleton<Provisioning>();
if(builder.Configuration.GetValue("Identity:WorkerEnabled",true)) builder.Services.AddHostedService<ProvisioningWorker>();
builder.Services.ConfigureHttpJsonOptions(o=>o.SerializerOptions.DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddRateLimiter(options=>
{
    options.RejectionStatusCode=429;
    options.OnRejected=async(context,_)=>await WriteError(context.HttpContext,429,"RATE_LIMITED");
    options.AddPolicy("registration",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _=>new FixedWindowRateLimiterOptions {PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
});
var app=builder.Build();
app.Use(async(context,next)=>
{
    context.Response.Headers.CacheControl="no-store";
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    try { await next(); }
    catch(ApiError error) { await WriteError(context,error.Status,error.Code); }
    catch(Exception error) when(error is JsonException or BadHttpRequestException)
    { await WriteError(context,400,"VALIDATION_FAILED"); }
    catch(NpgsqlException)
    { await WriteError(context,503,"TEMPORARILY_UNAVAILABLE"); }
});
app.UseRateLimiter();
app.MapGet("/health/live",()=>Results.Ok(new {status="live"}));
app.MapGet("/health/ready",async(Database db)=>
{
    await using var c=await db.Source.OpenConnectionAsync();
    var row=await c.One("SELECT version FROM schema_versions WHERE version=1");
    return row is null ? Results.StatusCode(503) : Results.Ok(new {status="ready",scope="internal-registration"});
});
app.MapPost("/auth/register",async(HttpRequest http,RegistrationService service)=>
{
    var body=await Read<StartRegistration>(http);
    var result=await service.Start(body,http.Headers["Idempotency-Key"].ToString());
    return Results.Json(result,statusCode:202);
}).RequireRateLimiting("registration");
app.MapPost("/auth/register/verify",async(HttpRequest http,RegistrationService service)=>
{
    var result=await service.Verify(await Read<VerifyRegistration>(http));
    return Results.Json(result.Result,statusCode:result.Created?201:200);
}).RequireRateLimiting("registration");
app.MapPost("/auth/register/resend",async(HttpRequest http,RegistrationService service)=>
{
    await service.Resend(await Read<ResendVerification>(http));
    return Results.Json(new {status="Accepted"},statusCode:202);
}).RequireRateLimiting("registration");
// Fake inbox is opt-in, loopback-only, authenticated and unavailable outside Development.
if(builder.Environment.IsDevelopment() && builder.Configuration["Identity:DevInboxKey"] is {Length: >= 32} inboxKey)
{
    app.MapGet("/dev/inbox/{id}",async(string id,HttpContext context,Database db,Secrets secrets)=>
    {
        var remote=context.Connection.RemoteIpAddress;
        if(remote is null || !System.Net.IPAddress.IsLoopback(remote)
            || !Secrets.Equal(secrets.Mac("dev",inboxKey),secrets.Mac("dev",context.Request.Headers["X-Dev-Inbox-Key"].ToString()))) return Results.NotFound();
        await using var c=await db.Source.OpenConnectionAsync();
        var row=await c.One("SELECT * FROM delivery_outbox WHERE verification_id=@id AND sealed_code IS NOT NULL AND expires_at>now() ORDER BY created_at DESC LIMIT 1",("id",id));
        return row is null ? Results.NotFound() : Results.Ok(new {code=secrets.Open(row.Get<byte[]>("sealed_code"),"delivery:"+row.Get<Guid>("id"))});
    });
}
app.Run();

static Task WriteError(HttpContext context,int status,string code)
{
    context.Response.StatusCode=status;
    return context.Response.WriteAsJsonAsync(new {error=new {code,message="Request could not be completed."},traceId=context.TraceIdentifier});
}

static async Task<T> Read<T>(HttpRequest request)
{
    Input.Require(request.HasJsonContentType());
    using var document=await JsonDocument.ParseAsync(request.Body);
    Input.Require(document.RootElement.ValueKind==JsonValueKind.Object);
    var seen=new HashSet<string>();
    foreach(var property in document.RootElement.EnumerateObject())
        Input.Require(property.Value.ValueKind!=JsonValueKind.Null && seen.Add(property.Name));
    return document.RootElement.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {PropertyNameCaseInsensitive=false,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow}) ?? throw new ApiError(400,"VALIDATION_FAILED");
}

public partial class Program { }
