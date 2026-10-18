using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartCore.Identity;

namespace SmartCore.Identity.Bff.Host;

// Private adapter; it owns the handler and never follows redirects or retries credentials.
public sealed class IdentityAccountHttp(HostConfiguration configuration) : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler {AllowAutoRedirect = false, UseCookies = false}) {
        BaseAddress = configuration.IdentityOrigin, Timeout = TimeSpan.FromSeconds(5)
    };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    public async Task<LoginResult> Login(LoginRequest body, CancellationToken cancellation)
    {
        using var request = Request(HttpMethod.Post, "auth/login");
        request.Headers.Add("X-Bff-Subject", Subject("login", body.Email ?? body.Mobile!));
        request.Content = JsonContent.Create(body, options: Json);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        await Check(response, true, cancellation);
        var result = await Read<LoginResult>(response, cancellation);
        Validate(result.Person);
        var tokens = result.Session;
        if (tokens is null || tokens.SessionId == Guid.Empty || tokens.AccessToken is not {Length: >= 1 and <= 4096}
            || tokens.RefreshToken is not {Length: >= 1 and <= 180} || tokens.ExpiresAt <= DateTimeOffset.UtcNow
            || tokens.AccessExpiresAt <= DateTimeOffset.UtcNow || tokens.AccessExpiresAt > tokens.ExpiresAt)
            throw new InvalidOperationException("Invalid private login result.");
        return result;
    }
    public async Task<SelfPerson> Me(string access, Guid session, CancellationToken cancellation)
    {
        using var request = Request(HttpMethod.Get, "me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add("X-Bff-Subject", Subject("bff-admission-session-v1", session.ToString("D")));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        await Check(response, false, cancellation);
        var person = await Read<SelfPerson>(response, cancellation); Validate(person); return person;
    }
    private string Subject(string purpose, string value) => Convert.ToBase64String(configuration.SessionSecrets.Mac(purpose, value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Bff-Client-Key", configuration.ClientKey);
        return request;
    }
    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken cancellation)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new InvalidOperationException("Private JSON response required.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        var bytes = new byte[32769]; var used = 0;
        while (used < bytes.Length) {var size = await stream.ReadAsync(bytes.AsMemory(used), cancellation); if (size == 0) break; used += size;}
        if (used > 32768) throw new InvalidOperationException("Oversized private response.");
        return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, used), Json) ?? throw new InvalidOperationException("Missing private response.");
    }
    private sealed record ErrorBody(string Code, string Message, string? NextAction);
    private sealed record Failure(ErrorBody Error, string TraceId);
    private static async Task Check(HttpResponseMessage response, bool login, CancellationToken cancellation)
    {
        if (response.StatusCode == HttpStatusCode.OK) return;
        // Recognize the private contract envelope, never serialize its message/trace to the browser.
        var failure = await Read<Failure>(response, cancellation);
        var code = failure.Error?.Code;
        if (response.StatusCode == HttpStatusCode.Unauthorized && code == "UNAUTHORIZED")
            throw new AccountRejection(401, login ? "UNAUTHORIZED" : "REAUTHENTICATION_REQUIRED");
        // Identity B1 admission uses a fixed one-minute window; it currently emits no retry field.
        if (response.StatusCode == HttpStatusCode.TooManyRequests && code == "RATE_LIMITED")
            throw new AccountRejection(429, "RATE_LIMITED", 60);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable && code is "AUTHENTICATION_BUSY" or "TEMPORARILY_UNAVAILABLE")
            throw new AccountRejection(503, login && code == "AUTHENTICATION_BUSY" ? code : "BFF_UNAVAILABLE", 60);
        throw new InvalidOperationException("Private account operation unavailable.");
    }
    private static void Validate(SelfPerson person)
    {
        if (person is null || person.PersonId == Guid.Empty || string.IsNullOrWhiteSpace(person.DisplayName)
            || person.DisplayName.Length > 100 || person.Status != "Active") throw new InvalidOperationException("Invalid private profile.");
        _ = Input.Contact(person.Email, person.Mobile);
    }
    public void Dispose() => client.Dispose();
}
public sealed class AccountRejection(int status, string code, int? retry = null) : Exception(code)
{
    public int Status {get;} = status;
    public string Code {get;} = code;
    public int? Retry {get;} = retry;
}
