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
builder.Services.AddSingleton<RegistrationSetupService>();
var authenticationEnabled=builder.Configuration.GetValue("Identity:AuthenticationEnabled",false);
string? bffKey=null;
var bffClient="development-bff";
if(authenticationEnabled)
{
    bffKey=builder.Configuration["Identity:BffClientKey"];
    if(bffKey is not {Length:>=32}) throw new InvalidOperationException("An authenticated development BFF client key is required.");
    var signingKey=builder.Configuration["Identity:AccessSigningKey"] ?? "";
    var bytes=Convert.FromBase64String(signingKey);
    if(bytes.SequenceEqual(Convert.FromBase64String(builder.Configuration["Identity:MacKey"] ?? ""))
        || bytes.SequenceEqual(Convert.FromBase64String(builder.Configuration["Identity:MaterialKey"] ?? "")))
        throw new InvalidOperationException("Access signing key must be distinct from MAC/material keys.");
    builder.Services.AddSingleton<IAuthenticationIssuanceGate,PostgresAuthenticationIssuanceGate>();
    builder.Services.AddSingleton(c=>new AccessTokens(signingKey,c.GetRequiredService<TimeProvider>()));
    builder.Services.AddSingleton<AuthenticationService>();
}
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
    catch(ApiError error) { await WriteError(context,error.Status,error.Code,error.NextAction); }
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
    var row=await c.One("SELECT version FROM schema_versions WHERE version=@version",("version",authenticationEnabled?4:2));
    return row is null ? Results.StatusCode(503) : Results.Ok(new {status="ready",scope=authenticationEnabled?"internal-authentication":"internal-registration"});
});
app.MapPost("/auth/register",async(HttpRequest http,RegistrationService service)=>
{
    var body=await Read<StartRegistration>(http);
    var result=await service.Start(body,http.Headers["Idempotency-Key"].ToString());
    return Results.Json(result,statusCode:202);
}).RequireRateLimiting("registration");
app.MapPost("/auth/register/setup",async(HttpRequest http,RegistrationSetupService service)=>
    Results.Json(await service.Request(await Read<VerifyRegistration>(http)),statusCode:202)).RequireRateLimiting("registration");
app.MapPost("/auth/register/complete",async(HttpRequest http,RegistrationSetupService service)=>
{
    var result=await service.Complete(await Read<CompleteRegistration>(http),http.Headers["Idempotency-Key"].ToString());
    return Results.Json(result,statusCode:result.Status=="Ready"?200:202);
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
if(authenticationEnabled)
{
    void Bff(HttpRequest request,Secrets secrets)
    {
        if(!Secrets.Equal(secrets.Mac("bff-client",bffKey!),secrets.Mac("bff-client",request.Headers["X-Bff-Client-Key"].ToString())))
            throw new ApiError(401,"UNAUTHORIZED");
    }
    AccessProof Proof(HttpRequest request,Secrets secrets,AccessTokens access)
    {
        Bff(request,secrets);
        var header=request.Headers.Authorization.ToString();
        if(!header.StartsWith("Bearer ",StringComparison.Ordinal) || header.Length>4103) throw new ApiError(401,"UNAUTHORIZED");
        return access.Read(header[7..],bffClient);
    }
    app.MapPost("/auth/login",async(HttpRequest request,Secrets secrets,AuthenticationService service)=>
    {
        Bff(request,secrets);
        return Results.Ok(await service.Login(await Read<LoginRequest>(request),bffClient));
    }).RequireRateLimiting("registration");
    app.MapPost("/auth/refresh",async(HttpRequest request,Secrets secrets,AuthenticationService service)=>
    {
        Bff(request,secrets);
        return Results.Ok(await service.Refresh(await Read<RefreshRequest>(request),bffClient));
    }).RequireRateLimiting("registration");
    app.MapGet("/me",async(HttpRequest request,Secrets secrets,AccessTokens access,AuthenticationService service)=>
        Results.Ok(await service.Self(Proof(request,secrets,access)))).RequireRateLimiting("registration");
    app.MapPost("/auth/logout",async(HttpRequest request,Secrets secrets,AccessTokens access,AuthenticationService service)=>
    {
        var proof=Proof(request,secrets,access);
        var body=await Read<LogoutRequest>(request);
        Input.Require(body.SessionId==proof.SessionId);
        await service.Logout(proof);
        return Results.NoContent();
    }).RequireRateLimiting("registration");
}
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
    app.MapGet("/dev/setup-inbox/{id}",async(string id,HttpContext context,Database db,Secrets secrets)=>
    {
        var remote=context.Connection.RemoteIpAddress;
        if(remote is null || !System.Net.IPAddress.IsLoopback(remote)
            || !Secrets.Equal(secrets.Mac("dev",inboxKey),secrets.Mac("dev",context.Request.Headers["X-Dev-Inbox-Key"].ToString()))) return Results.NotFound();
        await using var c=await db.Source.OpenConnectionAsync();
        var row=await c.One("SELECT * FROM setup_delivery_outbox WHERE setup_id=@id AND sealed_code IS NOT NULL AND expires_at>now()",("id",id));
        return row is null ? Results.NotFound() : Results.Ok(new {code=secrets.Open(row.Get<byte[]>("sealed_code"),"setup-delivery:"+row.Get<Guid>("id"))});
    });
}
app.Run();

static Task WriteError(HttpContext context,int status,string code,string? nextAction=null)
{
    context.Response.StatusCode=status;
    return context.Response.WriteAsJsonAsync(new {error=new {code,message="Request could not be completed.",nextAction},traceId=context.TraceIdentifier});
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
public sealed record LogoutRequest(Guid SessionId);
