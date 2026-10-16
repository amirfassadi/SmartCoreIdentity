using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using SmartCore.Identity;

namespace SmartCore.Identity.Bff.Host;

// Proof stays in an authenticated, encrypted HttpOnly cookie shared by both hosts.
// It is not authentication authority: Identity still enforces expiry, proof and budgets.
public sealed class BrowserRegistration(HostConfiguration configuration, IDataProtectionProvider protection, IdentityRegistrationHttp identity, TimeProvider clock)
{
    public const string CookieName = "__Host-smartcore-registration";
    private readonly IDataProtector protector = protection.CreateProtector("SmartCore.Browser.Registration.v1");
    private sealed record Flow(string FlowId, string Verification, string Binding, DateTimeOffset ExpiresAt, string? Setup = null);
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public async Task Handle(HttpContext context, string path, JsonElement body)
    {
        try {
            var id = body.GetProperty("flowId").GetString()!;
            if (path == "/bff/register") {
                // Stable across an explicit retry of a lost start response. Never retain password.
                var binding = Encode(configuration.SessionSecrets.Mac("browser-register-binding-v1", id));
                var key = Encode(configuration.SessionSecrets.Mac("browser-register-start-v1", id));
                var pending = await identity.Start(new(
                    body.TryGetProperty("email", out var e) ? e.GetString()!.ToLowerInvariant() : null,
                    body.TryGetProperty("mobile", out var m) ? BrowserGuard.NormalizeMobile(m.GetString()!) : null,
                    body.GetProperty("password").GetString()!, body.GetProperty("displayName").GetString()!, binding), key, context.RequestAborted);
                CheckExpiry(pending.ExpiresAt);
                Store(context, new(id, pending.VerificationSessionId, binding, pending.ExpiresAt));
                context.Response.StatusCode = 202;
                await context.Response.WriteAsJsonAsync(new {status = "AwaitingVerification", expiresAt = pending.ExpiresAt, resendAfterSeconds = 60}, context.RequestAborted);
                return;
            }
            var flow = Load(context, id);
            if (path == "/bff/register/resend") {
                if (flow.Setup is not null) throw new AccountRejection(400, "REGISTRATION_FLOW_EXPIRED");
                await identity.Resend(new(flow.Verification, flow.Binding), context.RequestAborted);
                context.Response.StatusCode = 202;
                await context.Response.WriteAsJsonAsync(new {status = "Accepted", resendAfterSeconds = 60}, context.RequestAborted); return;
            }
            var code = body.GetProperty("code").GetString()!;
            if (path == "/bff/register/setup") {
                var setup = await identity.Setup(new(flow.Verification, code, flow.Binding), context.RequestAborted);
                CheckExpiry(setup.ExpiresAt);
                Store(context, flow with {Setup = setup.SetupChallengeId, ExpiresAt = setup.ExpiresAt});
                context.Response.StatusCode = 202;
                await context.Response.WriteAsJsonAsync(new {status = "AwaitingSetup", expiresAt = setup.ExpiresAt}, context.RequestAborted); return;
            }
            if (path == "/bff/register/complete") {
                if (flow.Setup is null) throw new AccountRejection(400, "REGISTRATION_FLOW_EXPIRED");
                var key = Encode(configuration.SessionSecrets.Mac("browser-register-complete-v1", id));
                var result = await identity.Complete(new(flow.Setup, code, flow.Binding, body.GetProperty("newPassword").GetString()!), key, context.RequestAborted);
                await context.Response.WriteAsJsonAsync(new {status = result.Status, credentialOutcome = result.CredentialOutcome}, context.RequestAborted); return;
            }
            if (flow.Setup is not null) throw new AccountRejection(400, "REGISTRATION_FLOW_EXPIRED");
            try {
                var result = await identity.Verify(new(flow.Verification, code, flow.Binding), context.RequestAborted);
                await context.Response.WriteAsJsonAsync(new {status = result.Status}, context.RequestAborted);
            } catch (RegistrationNextAction next) {
                // These outcomes are exposed only after Identity accepted ownership proof.
                await context.Response.WriteAsJsonAsync(new {status = next.Action == "RequestSetup" ? "SetupRequired" : "SignInRequired"}, context.RequestAborted);
            }
        } catch (AccountRejection failure) { await BrowserGuard.Error(context, failure.Status, failure.Code, failure.Retry ?? 60); }
    }
    private Flow Load(HttpContext context, string id)
    {
        try {
            var value = context.Request.Cookies[CookieName];
            if (value is null || value.Length > 2048) throw new CryptographicException();
            var flow = JsonSerializer.Deserialize<Flow>(protector.Unprotect(value));
            if (flow is null || flow.ExpiresAt <= clock.GetUtcNow() || !Input.Secret(flow.FlowId)
                || !Input.Id(flow.Verification) || !Input.Secret(flow.Binding) || flow.Setup is not null && !Input.Id(flow.Setup)
                || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(id), System.Text.Encoding.UTF8.GetBytes(flow.FlowId))) throw new CryptographicException();
            return flow;
        } catch (Exception failure) when (failure is CryptographicException or JsonException or ArgumentException) {
            throw new AccountRejection(400, "REGISTRATION_FLOW_EXPIRED");
        }
    }
    private void CheckExpiry(DateTimeOffset expiry)
    {
        if (expiry <= clock.GetUtcNow() || expiry > clock.GetUtcNow().AddMinutes(11)) throw new InvalidOperationException("Invalid private proof expiry.");
    }
    private void Store(HttpContext context, Flow flow) => context.Response.Cookies.Append(CookieName, protector.Protect(JsonSerializer.Serialize(flow)),
        new CookieOptions {Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = flow.ExpiresAt,
            MaxAge = flow.ExpiresAt - clock.GetUtcNow()});
}
