using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using MediaTypeHeaderValue = Microsoft.Net.Http.Headers.MediaTypeHeaderValue;
using SmartCore.Identity;

namespace SmartCore.Identity.Bff.Host;

public sealed class BrowserGuard(HostConfiguration configuration, IAntiforgery antiforgery, BrowserAccounts accounts, IConfiguration settings)
{
    private static readonly Dictionary<string, string> Routes = new(StringComparer.Ordinal) {
        ["/bff/csrf"] = "GET", ["/bff/login"] = "POST", ["/bff/me"] = "POST",
        ["/bff/me/action"] = "POST", ["/bff/logout"] = "POST"
    };
    public async Task Handle(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (!Routes.TryGetValue(path, out var method)) { await Error(context, 404, "NOT_FOUND"); return; }
        if (context.Request.Method != method) { context.Response.Headers.Allow = method; await Error(context, 405, "METHOD_NOT_ALLOWED"); return; }
        try {
            if (method == "GET") {
                if (!TrustedTransport(context)) { await Error(context, 503, "BFF_UNAVAILABLE"); return; }
                await Principal(context);
                var tokens = antiforgery.GetAndStoreTokens(context);
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XFrameOptions = "DENY";
                await context.Response.WriteAsJsonAsync(new {requestToken = tokens.RequestToken}, context.RequestAborted);
                return;
            }
            // Raw bounded body BEFORE type/origin/authentication, never parse JSON here.
            using var body = new MemoryStream(); var buffer = new byte[4096];
            try {
                int read;
                while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0) {
                    if (body.Length + read > 16384) { await Error(context, 413, "REQUEST_TOO_LARGE"); return; }
                    body.Write(buffer, 0, read);
                }
            } catch (BadHttpRequestException failure) when (failure.StatusCode == 413) { await Error(context, 413, "REQUEST_TOO_LARGE"); return; }
            if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType)
                || !contentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || contentType.Charset.HasValue && !contentType.Charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase)) {
                await Error(context, 415, "UNSUPPORTED_MEDIA_TYPE"); return;
            }
            var origin = context.Request.Headers.Origin;
            var fetch = context.Request.Headers["Sec-Fetch-Site"];
            if (!TrustedTransport(context) || origin.Count != 1 || origin[0] != configuration.ExternalOrigin.GetLeftPart(UriPartial.Authority)
                || fetch.Count > 1 || fetch.Count == 1 && fetch[0] is not ("same-origin" or "none")) {
                await Error(context, 403, "ORIGIN_REJECTED"); return;
            }
            await Principal(context);
            // Force a single header token. Never permit form fallback or duplicate token ambiguity.
            if (context.Request.Headers["X-CSRF-Token"].Count != 1) { await Error(context, 403, "CSRF_REJECTED"); return; }
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { await Error(context, 403, "CSRF_REJECTED"); return; }
            try {
                using var json = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions {MaxDepth = 16});
                if (!ValidBody(path, json.RootElement)) { await Error(context, 400, "VALIDATION_FAILED"); return; }
                if (path == "/bff/login") context.Items["browser-login"] = new LoginRequest(
                    json.RootElement.TryGetProperty("email", out var e) ? e.GetString()!.ToLowerInvariant() : null,
                    json.RootElement.TryGetProperty("mobile", out var m) ? NormalizeMobile(m.GetString()!) : null,
                    json.RootElement.GetProperty("password").GetString()!);
            } catch (JsonException) { await Error(context, 400, "VALIDATION_FAILED"); return; }
            if (path != "/bff/login" && context.User.Identity?.IsAuthenticated != true) {
                ClearCookies(context); await Error(context, 401, "REAUTHENTICATION_REQUIRED"); return;
            }
            if (path == "/bff/login" && context.User.Identity?.IsAuthenticated == true) {
                await Error(context, 409, "SESSION_ALREADY_ACTIVE"); return;
            }
            // Explicit opt-in for private account acceptance; public deployment remains rejected at startup.
            if (!settings.GetValue("Bff:AccountsEnabled", false)) { await Error(context, 503, "BFF_UNAVAILABLE"); return; }
            await accounts.Handle(context, path);
        } catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception) { if (!context.Response.HasStarted) await Error(context, 503, "BFF_UNAVAILABLE"); else context.Abort(); }
    }
    private bool TrustedTransport(HttpContext context) => context.Request.IsHttps
        && context.Request.Host.Value == configuration.ExternalOrigin.Authority;
    private static async Task Principal(HttpContext context)
    {
        var result = await context.AuthenticateAsync(OpaqueSessionHandler.SchemeName);
        if (result.Failure is not null) throw new InvalidOperationException("Session resolution unavailable.");
        context.User = result.Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
    }
    private static bool ValidBody(string path, JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return false;
        var names = body.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) return false;
        if (path != "/bff/login") return names.Length == 0;
        if (names.Any(name => name is not ("email" or "mobile" or "password")) || names.Length != 2
            || !body.TryGetProperty("password", out var password) || password.ValueKind != JsonValueKind.String
            || password.GetString() is not {Length: >= 1 and <= 128}) return false;
        if (body.TryGetProperty("email", out var email)) {
            var value = email.ValueKind == JsonValueKind.String ? email.GetString() : null;
            return value is {Length: >= 3 and <= 254} && value.Contains('@') && !value.Any(char.IsWhiteSpace)
                && MailAddress.TryCreate(value, out var parsed) && parsed.Address == value;
        }
        var mobile = body.GetProperty("mobile");
        if (mobile.ValueKind != JsonValueKind.String || mobile.GetString() is not {Length: >= 7 and <= 32} number
            || !Regex.IsMatch(number, @"\A[+0-9۰-۹٠-٩ ()-]+\z")) return false;
        var canonical = NormalizeMobile(number);
        return Regex.IsMatch(canonical, @"\A\+989[0-9]{9}\z");
    }
    private static string NormalizeMobile(string number)
    {
        var canonical = string.Concat(number.Select(character => character is >= '۰' and <= '۹' ? (char)('0' + character - '۰') : character is >= '٠' and <= '٩' ? (char)('0' + character - '٠') : character).Where(character => character is not (' ' or '(' or ')' or '-')));
        if (canonical.StartsWith("0098", StringComparison.Ordinal)) canonical = "+98" + canonical[4..];
        else if (canonical.StartsWith("09", StringComparison.Ordinal)) canonical = "+98" + canonical[1..];
        return canonical;
    }
    internal static void ClearCookies(HttpContext context)
    {
        foreach (var name in new[] {OpaqueSessionHandler.CookieName, "__Host-smartcore-antiforgery"})
            context.Response.Cookies.Append(name, "", new CookieOptions {Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.Zero, Expires = DateTimeOffset.UnixEpoch});
    }
    internal static async Task Error(HttpContext context, int status, string code, int retry = 60)
    {
        context.Response.StatusCode = status;
        if (status is 503 or 429) { context.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture); await context.Response.WriteAsJsonAsync(new {code, retryAfterSeconds = retry}, context.RequestAborted); }
        else await context.Response.WriteAsJsonAsync(new {code}, context.RequestAborted);
    }
}
