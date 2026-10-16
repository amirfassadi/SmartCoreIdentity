using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SmartCore.Identity.Bff;

namespace SmartCore.Identity.Bff.Host;

public sealed class OpaqueSessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, BffRefresh core)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "BffOpaque";
    public const string CookieName = "__Host-smartcore-session";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookie = Request.Cookies[CookieName];
        if (cookie is null) return AuthenticateResult.NoResult();
        // Only the exact logout command can keep the original identity during durable logout recovery.
        var logout = Request.Method == "POST" && Request.Path == "/bff/logout";
        var id = await core.ResolveBrowserSession(cookie, logout, Context.RequestAborted);
        if (id is null) return AuthenticateResult.NoResult();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.Value.ToString("D"))], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
