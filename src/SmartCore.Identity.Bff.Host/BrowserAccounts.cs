using SmartCore.Identity;
using SmartCore.Identity.Bff;

namespace SmartCore.Identity.Bff.Host;

public sealed class BrowserAccounts(BffRefresh core, IdentityAccountHttp identity)
{
    // Always construct the browser DTO explicitly: trusted token/profile records are never serialized.
    private static object Profile(SelfPerson person) => new {person = new {
        person.DisplayName, person.Email, person.Mobile, person.Status, person.RecoveryEnrollmentRequired
    }};
    public async Task Handle(HttpContext context, string path)
    {
        var cancellation = context.RequestAborted;
        var cookie = context.Request.Cookies[OpaqueSessionHandler.CookieName];
        try {
            if (path == "/bff/login") {
                var result = await identity.Login((LoginRequest)context.Items["browser-login"]!, cancellation);
                var fresh = await core.Create(result.Session, cancellation);
                // Only after persistence succeeds. The session's absolute deadline bounds the cookie.
                context.Response.Cookies.Append(OpaqueSessionHandler.CookieName, fresh, new CookieOptions {
                    Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/",
                    Expires = result.Session.ExpiresAt, MaxAge = result.Session.ExpiresAt - DateTimeOffset.UtcNow
                });
                context.Response.Cookies.Append("__Host-smartcore-antiforgery", "", new CookieOptions {
                    Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.Zero, Expires = DateTimeOffset.UnixEpoch
                });
                await context.Response.WriteAsJsonAsync(Profile(result.Person), cancellation); return;
            }
            if (cookie is null) throw new BffError("REAUTHENTICATION_REQUIRED");
            if (path == "/bff/logout") {
                // Maintenance owns upstream completion. A lost/failed local commit never claims success.
                await core.RequestLogout(cookie, cancellation);
                BrowserGuard.ClearCookies(context); context.Response.StatusCode = 204; return;
            }
            var access = await core.RefreshIfDue(cookie, path == "/bff/me/action" ? BffActivity.UserAction : BffActivity.Background, cancellation);
            SelfPerson person;
            try {person = await identity.Me(access.AccessToken, access.SessionId, cancellation);}
            catch (AccountRejection error) when (error.Status == 401) {
                await core.RejectAccess(cookie, access.SessionId, cancellation);
                throw new BffError("REAUTHENTICATION_REQUIRED");
            }
            // A lookup/HTTP read may race local logout. Do not return a profile from a closed row.
            if (await core.ResolveBrowserSession(cookie, cancellation: cancellation) is null) throw new BffError("REAUTHENTICATION_REQUIRED");
            await context.Response.WriteAsJsonAsync(Profile(person), cancellation);
        } catch (BffError error) {
            if (error.Code == "REAUTHENTICATION_REQUIRED") {
                BrowserGuard.ClearCookies(context); await BrowserGuard.Error(context, 401, error.Code);
            } else await BrowserGuard.Error(context, 503, error.Code is "BFF_BUSY" ? "BFF_BUSY" : "BFF_UNAVAILABLE", error.RetryAfterSeconds ?? 60);
        } catch (AccountRejection error) {
            await BrowserGuard.Error(context, error.Status, error.Code, error.Retry ?? 60);
        }
    }
}
