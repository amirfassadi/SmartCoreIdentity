using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

namespace SmartCore.Identity.Bff.Host;

// Opt-in staging is distinct from Development/Testing and never enables Production.
public sealed class BrowserTransport
{
    public const string ProofHeader = "X-SmartCore-Bff-Gateway-Proof";
    public Uri ExternalOrigin { get; }
    public bool RestrictedStaging => proof is not null;
    private readonly byte[]? proof;

    private BrowserTransport(Uri origin, string? gatewayProof)
    { ExternalOrigin = origin; proof = gatewayProof is null ? null : Encoding.ASCII.GetBytes(gatewayProof); }

    public static BrowserTransport Load(IConfiguration settings, IHostEnvironment environment)
    {
        var staging = environment.IsEnvironment("RestrictedStaging");
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && !staging)
            throw new InvalidOperationException("Browser deployment release gates are not closed.");
        var origin = new Uri(settings["Bff:ExternalOrigin"] ?? "", UriKind.Absolute);
        if (origin.Scheme != "https" || origin.AbsolutePath != "/" || origin.UserInfo.Length != 0
            || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidOperationException("A fixed HTTPS browser origin is required.");
        if (!staging)
        {
            if (!origin.IsLoopback || settings["Bff:RestrictedStagingEnabled"] is not (null or "false"))
                throw new InvalidOperationException("Private browser hosting requires a loopback origin.");
            return new(origin, null);
        }
        if (settings["Bff:RestrictedStagingEnabled"] != "true" || origin.IsLoopback
            || Uri.CheckHostName(origin.Host) != UriHostNameType.Dns || !origin.Host.Contains('.')
            || origin.Host.EndsWith('.') || origin.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || settings["Bff:StagingBrowserOrigin"] != origin.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("Restricted staging requires an explicitly confirmed browser origin.");
        var gatewayProof = settings["Bff:GatewayProof"];
        if (gatewayProof is null || !Regex.IsMatch(gatewayProof, @"\A[A-Za-z0-9_-]{43}\z"))
            throw new InvalidOperationException("Restricted staging requires a dedicated gateway proof.");
        return new(origin, gatewayProof);
    }

    public bool AcceptsProof(StringValues values) => proof is not null && values.Count == 1
        && values[0] is {Length: 43} value
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value), proof);
}

// Before forwarded headers: possession of a forwarded Host/Origin alone confers no trust.
public sealed class GatewayTrustMiddleware(RequestDelegate next, BrowserTransport transport)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (transport.RestrictedStaging && context.Request.Path != "/health/ready"
            && (context.Connection.RemoteIpAddress is not { } address
                || !IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)
                || !transport.AcceptsProof(context.Request.Headers[BrowserTransport.ProofHeader])))
        {
            context.Response.StatusCode = 403;
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await context.Response.WriteAsJsonAsync(new {code = "ORIGIN_REJECTED"}, context.RequestAborted);
            return;
        }
        context.Request.Headers.Remove(BrowserTransport.ProofHeader);
        await next(context);
    }
}
