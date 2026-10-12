using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using SmartCore.Identity;
using SmartCore.Identity.Bff;
namespace SmartCore.Identity.Bff.Host;

public sealed class BrowserRecovery(HostConfiguration configuration,IDataProtectionProvider protection,IdentityRecoveryHttp identity,IdentityAccountHttp accounts,BffRefresh core,TimeProvider clock)
{
    public const string CookieName="__Host-smartcore-reset";
    private readonly IDataProtector protector=protection.CreateProtector("SmartCore.Browser.Reset.v1");
    private sealed record Flow(string FlowId,string Challenge,string Binding,string Subject,DateTimeOffset ExpiresAt);
    private string Binding(string id)=>Convert.ToBase64String(configuration.SessionSecrets.Mac("browser-reset-binding-v1",id)).TrimEnd('=').Replace('+','-').Replace('/','_');
    private Guid Operation(string purpose,string id)=>new(configuration.SessionSecrets.Mac(purpose,id).AsSpan(0,16));
    public async Task Handle(HttpContext context,string path,JsonElement body)
    {
        var cancellation=context.RequestAborted;
        try {
            var flowId=body.GetProperty("flowId").GetString()!;
            if(path=="/bff/password/reset") {
                var email=body.TryGetProperty("email",out var e)?e.GetString()!.ToLowerInvariant():null;
                var mobile=body.TryGetProperty("mobile",out var m)?BrowserGuard.NormalizeMobile(m.GetString()!):null;
                var binding=Binding(flowId);var resetSubject=identity.Subject(email ?? mobile!);
                var pending=await identity.Start(new(Operation("browser-reset-start-v1",flowId),email,mobile,binding),resetSubject,cancellation);
                if(pending.ExpiresAt<=clock.GetUtcNow() || pending.ExpiresAt>clock.GetUtcNow().AddMinutes(11))throw new InvalidOperationException();
                var flow=new Flow(flowId,pending.ChallengeId,binding,resetSubject,pending.ExpiresAt);
                context.Response.Cookies.Append(CookieName,protector.Protect(JsonSerializer.Serialize(flow)),new CookieOptions {
                    Secure=true,HttpOnly=true,SameSite=SameSiteMode.Lax,Path="/",Expires=flow.ExpiresAt,MaxAge=flow.ExpiresAt-clock.GetUtcNow()});
                context.Response.StatusCode=202;
                await context.Response.WriteAsJsonAsync(new {status="Accepted",expiresAt=flow.ExpiresAt},cancellation);return;
            }
            if(path=="/bff/password/reset/complete") {
                var flow=Load(context,flowId);
                await identity.Complete(new(Operation("browser-reset-complete-v1",flowId),flow.Challenge,flow.Binding,
                    body.GetProperty("code").GetString()!,body.GetProperty("recoveryCode").GetString()!.Trim().ToUpperInvariant(),body.GetProperty("newPassword").GetString()!),flow.Subject,cancellation);
                context.Response.StatusCode=202;
                await context.Response.WriteAsJsonAsync(new {status="Accepted"},cancellation);return;
            }
            var cookie=context.Request.Cookies[OpaqueSessionHandler.CookieName] ?? throw new BffError("REAUTHENTICATION_REQUIRED");
            var access=await core.RefreshIfDue(cookie,BffActivity.UserAction,cancellation);
            try {await accounts.Me(access.AccessToken,access.SessionId,cancellation);}
            catch(AccountRejection failure)when(failure.Status==401) {
                await core.RejectAccess(cookie,access.SessionId,cancellation);throw new BffError("REAUTHENTICATION_REQUIRED");
            }
            var operation=Operation(path+":browser-operation-v1",access.SessionId.ToString("D")+":"+flowId);
            var subject=identity.Subject(access.SessionId.ToString("D"));
            var password=body.GetProperty("currentPassword").GetString()!;
            if(path=="/bff/recovery/enroll") {
                var result=await identity.Enroll(new(operation,password,true),access.AccessToken,subject,cancellation);
                if(await core.ResolveBrowserSession(cookie,cancellation:cancellation) is null)throw new BffError("REAUTHENTICATION_REQUIRED");
                // Exactly one exceptional browser secret, deliberate volatile one-time UX.
                // No trusted operation/version/subject fields escape this projection.
                if(result.Status=="Issued")await context.Response.WriteAsJsonAsync(new {status="Issued",recoveryCode=result.RecoveryCode},cancellation);
                else await context.Response.WriteAsJsonAsync(new {status="AlreadyIssued"},cancellation);
                return;
            }
            await identity.Change(new(operation,password,body.GetProperty("newPassword").GetString()!),access.AccessToken,subject,cancellation);
            // Upstream acceptance fenced all Identity sessions. A local failure is still
            // ambiguous to this browser, never a claimed successful password replacement.
            await core.RequestLogout(cookie,cancellation);
            BrowserGuard.ClearCookies(context);context.Response.StatusCode=202;
            await context.Response.WriteAsJsonAsync(new {status="Accepted"},cancellation);
        } catch(BffError failure) {
            if(failure.Code=="REAUTHENTICATION_REQUIRED") {BrowserGuard.ClearCookies(context);await BrowserGuard.Error(context,401,failure.Code);}
            else await BrowserGuard.Error(context,503,failure.Code=="BFF_BUSY"?"BFF_BUSY":"BFF_UNAVAILABLE",failure.RetryAfterSeconds ?? 60);
        } catch(AccountRejection failure) {await BrowserGuard.Error(context,failure.Status,failure.Code,failure.Retry ?? 60);}
    }
    private Flow Load(HttpContext context,string id) {
        try {
            var cookie=context.Request.Cookies[CookieName];if(cookie is null || cookie.Length>2048)throw new CryptographicException();
            var flow=JsonSerializer.Deserialize<Flow>(protector.Unprotect(cookie));
            if(flow is null || flow.ExpiresAt<=clock.GetUtcNow() || !Input.Secret(flow.FlowId) || !Input.Secret(flow.Challenge)
                || !Input.Secret(flow.Binding) || !Input.Secret(flow.Subject)
                || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(id),System.Text.Encoding.UTF8.GetBytes(flow.FlowId)))throw new CryptographicException();
            return flow;
        } catch(Exception failure)when(failure is CryptographicException or JsonException or ArgumentException) {throw new AccountRejection(400,"RESET_FLOW_EXPIRED");}
    }
}
