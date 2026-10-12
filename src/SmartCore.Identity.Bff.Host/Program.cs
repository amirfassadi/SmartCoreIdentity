using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
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
builder.Services.AddSingleton(configuration);
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
builder.Services.Configure<ForwardedHeadersOptions>(options => {
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 1; options.RequireHeaderSymmetry = true;
    options.KnownIPNetworks.Clear(); options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback); options.KnownProxies.Add(IPAddress.IPv6Loopback);
    options.KnownProxies.Add(IPAddress.Loopback.MapToIPv6());
    options.AllowedHosts.Add(configuration.ExternalOrigin.Host);
});
// Minimal apps auto-add authentication middleware. Suppress the single-scheme default
// so that middleware cannot resolve a cookie before the explicitly ordered guard.
AppContext.SetSwitch("Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme", true);
builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, OpaqueSessionHandler>(OpaqueSessionHandler.SchemeName, _ => { });
builder.Services.AddSingleton<IdentityAccountHttp>();
builder.Services.AddScoped<BrowserAccounts>();
builder.Services.AddSingleton<IdentityRegistrationHttp>();
builder.Services.AddScoped<BrowserRegistration>();
builder.Services.AddScoped<BrowserGuard>();
var app = builder.Build();
if (await app.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync() is not null)
    throw new InvalidOperationException("Browser authentication must be explicitly invoked after transport guards.");
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
app.UseForwardedHeaders();
app.Use(async (context, next) => {
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});
app.MapGet("/health/ready", () => Results.NoContent());
// Authentication is invoked by the guard after size/type/origin, never globally before it.
// Explicit RequestDelegate keeps routing/validation inside the ordered guard without body-binding inference.
app.MapFallback("/{**path}", (RequestDelegate)(context => context.RequestServices.GetRequiredService<BrowserGuard>().Handle(context)));
await app.RunAsync();
