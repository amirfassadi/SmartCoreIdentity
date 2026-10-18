using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using SmartCore.Identity.Bff.Host;

var proof = new string('p', 43); // Synthetic test material only.
var basis = new Dictionary<string, string?> {
    ["Bff:ExternalOrigin"] = "https://accounts.example.test",
    ["Bff:RestrictedStagingEnabled"] = "true",
    ["Bff:StagingBrowserOrigin"] = "https://accounts.example.test",
    ["Bff:GatewayProof"] = proof
};
BrowserTransport Load(string environment, Dictionary<string, string?> settings) =>
    BrowserTransport.Load(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new TestEnvironment(environment));
void Check(bool passed, string label) { if (!passed) throw new Exception(label); }
void Reject(string environment, Dictionary<string, string?> settings)
{
    try { Load(environment, settings); }
    catch (Exception error) when (error is InvalidOperationException or UriFormatException) { return; }
    throw new Exception("Unsafe transport configuration accepted");
}
var staging = Load("RestrictedStaging", basis);
Check(staging.RestrictedStaging, "Staging control");
foreach (var environment in new[] {"Production", "Staging", "Development", "Testing", ""}) Reject(environment, basis);
foreach (var key in basis.Keys)
{
    var missing = new Dictionary<string, string?>(basis); missing.Remove(key);
    Reject("RestrictedStaging", missing);
}
foreach (var value in new[] {"false", "True", "1", ""})
    Reject("RestrictedStaging", new(basis) {["Bff:RestrictedStagingEnabled"] = value});
foreach (var origin in new[] {"http://accounts.example.test", "https://localhost", "https://127.0.0.1", "https://[::1]",
    "https://nested.localhost", "https://87.107.54.244", "https://*.example.test", "https://accounts.example.test/other",
    "https://user:pass@accounts.example.test", "https://accounts.example.test?x=1", "https://accounts.example.test#x",
    "https://accounts.example.test.", "https://singlelabel"})
    Reject("RestrictedStaging", new(basis) {["Bff:ExternalOrigin"] = origin, ["Bff:StagingBrowserOrigin"] = origin});
Reject("RestrictedStaging", new(basis) {["Bff:StagingBrowserOrigin"] = "https://alias.example.test"});
foreach (var value in new[] {"", new string('x', 42), new string('x', 44), new string('x', 42) + "!"})
    Reject("RestrictedStaging", new(basis) {["Bff:GatewayProof"] = value});
foreach (var environment in new[] {"Development", "Testing"})
{
    var local = Load(environment, new() {["Bff:ExternalOrigin"] = "https://localhost:7443"});
    Check(!local.RestrictedStaging, "Legacy private control");
    Reject(environment, new() {["Bff:ExternalOrigin"] = "https://accounts.example.test"});
}
Check(staging.AcceptsProof(new StringValues(proof)), "Valid proof");
foreach (var values in new[] {StringValues.Empty, new StringValues(""), new StringValues(new string('x', 43)), new StringValues(new[] {proof, proof})})
    Check(!staging.AcceptsProof(values), "Ambiguous or incorrect proof accepted");

async Task<(DefaultHttpContext Context, int Calls)> Request(string address, StringValues supplied, string path = "/bff/csrf")
{
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse(address);
    context.Request.Path = path;
    context.Request.Headers[BrowserTransport.ProofHeader] = supplied;
    context.Request.Headers["X-Forwarded-Host"] = "accounts.example.test";
    context.Request.Headers["X-Forwarded-Proto"] = "https";
    context.Response.Body = new MemoryStream();
    var calls = 0;
    var middleware = new GatewayTrustMiddleware(_ => {calls++; return Task.CompletedTask;}, staging);
    await middleware.InvokeAsync(context);
    return (context, calls);
}
foreach (var address in new[] {"127.0.0.1", "::1", "::ffff:127.0.0.1"})
{
    var accepted = await Request(address, proof);
    Check(accepted.Calls == 1 && !accepted.Context.Request.Headers.ContainsKey(BrowserTransport.ProofHeader), "Trusted loopback proof stripped before dispatch");
    foreach (var supplied in new[] {StringValues.Empty, new StringValues("wrong"), new StringValues(new[] {proof, proof})})
    {
        var denied = await Request(address, supplied);
        Check(denied.Calls == 0 && denied.Context.Response.StatusCode == 403, "Forged forwarded headers dispatched");
        Check(denied.Context.Response.Headers.CacheControl == "no-store", "Proof failure must not cache");
        var body = Encoding.UTF8.GetString(((MemoryStream)denied.Context.Response.Body).ToArray());
        Check(body == "{\"code\":\"ORIGIN_REJECTED\"}", "Proof failure exposed diagnostics");
    }
}
foreach (var address in new[] {"192.0.2.1", "2001:db8::1"})
{
    var denied = await Request(address, proof);
    Check(denied.Calls == 0 && denied.Context.Response.StatusCode == 403, "Public connection accepted");
}
var health = await Request("127.0.0.1", StringValues.Empty, "/health/ready");
Check(health.Calls == 1, "Private readiness probe blocked");
Console.WriteLine("Transport policy PASS: explicit staging opt-in, exact HTTPS origin, Production denial and gateway proof before forwarding.");
Console.WriteLine("Gateway middleware PASS: zero dispatch for missing/duplicate/incorrect proofs and non-loopback peers; valid internal proof stripped.");

sealed class TestEnvironment(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "TransportTests";
    public string ContentRootPath { get; set; } = "/";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
