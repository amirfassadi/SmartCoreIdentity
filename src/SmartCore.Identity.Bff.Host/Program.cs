using Microsoft.AspNetCore.DataProtection;
using SmartCore.Identity;
using SmartCore.Identity.Bff;
using SmartCore.Identity.Bff.Host;

var builder = WebApplication.CreateBuilder(args);
// Configuration failures report a safe category, never connection strings or cryptographic material.
HostConfiguration configuration;
try { configuration = HostConfiguration.Load(builder.Configuration, builder.Environment); }
catch (Exception failure) { Console.Error.WriteLine($"BFF startup configuration rejected ({failure.GetType().Name})."); Environment.ExitCode = 1; return; }
using var certificate = configuration.WrappingCertificate;
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16384);
builder.Services.AddSingleton(configuration.SessionSecrets);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new BffStore(configuration.Database));
builder.Services.AddSingleton(_ => new IdentityRefreshHttp(configuration.IdentityOrigin, configuration.ClientKey, configuration.ReceiptKey, developmentLoopback: true));
builder.Services.AddSingleton<IIdentityRefresh>(services => services.GetRequiredService<IdentityRefreshHttp>());
builder.Services.AddSingleton<IIdentityLogout>(services => services.GetRequiredService<IdentityRefreshHttp>());
builder.Services.AddSingleton<BffRefresh>();
builder.Services.AddSingleton<BffCircuit>();
builder.Services.AddHostedService<BffMaintenance>();
builder.Services.AddDataProtection().SetApplicationName("SmartCore.Identity.Browser.B1")
    .PersistKeysToFileSystem(new DirectoryInfo(configuration.KeyRing)).ProtectKeysWithCertificate(certificate);
builder.Services.AddAntiforgery(options => {
    options.HeaderName = "X-CSRF-Token";
    options.Cookie.Name = "__Host-smartcore-antiforgery";
    options.Cookie.Path = "/"; options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.SameSite = SameSiteMode.Lax;
});
var app = builder.Build();
if (args.Contains("--validate-config")) { Console.WriteLine("BFF scaffold configuration validated."); return; }
// Test-only same-purpose proof across two separate host processes, not an HTTP endpoint.
if (args.Contains("--keyring-proof")) {
    var protector = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("B1.shared-ring-proof.v1");
    var prior = await Console.In.ReadLineAsync();
    if (string.IsNullOrEmpty(prior)) Console.WriteLine(protector.Protect("shared-ring-proof"));
    else { if (protector.Unprotect(prior) != "shared-ring-proof") throw new InvalidOperationException("Key ring proof rejected."); Console.WriteLine("Shared encrypted key ring proof accepted."); }
    return;
}
await app.Services.GetRequiredService<BffStore>().Migrate();
if (args.Contains("--migrate")) return;
app.Use(async (context, next) => {
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});
app.MapGet("/health/ready", () => Results.NoContent());
// Scaffolding only. No session/authentication handler is registered yet; no opaque cookie is minted.
// Registered contract methods remain unavailable until tasks 5/6 attach the session-bound guards/handlers.
var routes = new Dictionary<string, string>(StringComparer.Ordinal) {
    ["/bff/csrf"] = "GET", ["/bff/login"] = "POST", ["/bff/me"] = "POST",
    ["/bff/me/action"] = "POST", ["/bff/logout"] = "POST"
};
app.MapFallback("/{**path}", async (HttpContext context) => {
    var path = context.Request.Path.Value ?? "";
    if (routes.TryGetValue(path, out var method)) {
        if (!string.Equals(context.Request.Method, method, StringComparison.Ordinal)) {
            context.Response.Headers.Allow = method;
            context.Response.StatusCode = 405;
            await context.Response.WriteAsJsonAsync(new {code = "METHOD_NOT_ALLOWED"});
        } else {
            context.Response.StatusCode = 503; context.Response.Headers.RetryAfter = "60";
            await context.Response.WriteAsJsonAsync(new {code = "BFF_UNAVAILABLE", retryAfterSeconds = 60});
        }
    } else {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsJsonAsync(new {code = "NOT_FOUND"});
    }
});
await app.RunAsync();
