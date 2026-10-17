using System.Net.Http.Json;
using System.Text.Json;
using SmartCore.Identity;
namespace SmartCore.Identity.Bff.Host;

// Exact trusted operations, bounded body/deadline, no redirects/cookies/replay.
public sealed class IdentityRegistrationHttp(HostConfiguration configuration) : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler {AllowAutoRedirect = false, UseCookies = false}) {
        BaseAddress = configuration.IdentityOrigin, Timeout = TimeSpan.FromSeconds(5)
    };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    public async Task<VerificationPending> Start(StartRegistration body, string key, CancellationToken cancellation) {
        var value = await Send<VerificationPending>("auth/register", body, key, [202], cancellation);
        if (value.Status != "AwaitingVerification" || !Input.Id(value.VerificationSessionId)) throw new InvalidOperationException();
        return value;
    }
    public async Task<RegistrationResult> Verify(VerifyRegistration body, CancellationToken cancellation) {
        var value = await Send<RegistrationResult>("auth/register/verify", body, null, [200,201], cancellation); Validate(value); return value;
    }
    public async Task Resend(ResendVerification body, CancellationToken cancellation) {
        var value = await Send<Accepted>("auth/register/resend", body, null, [202], cancellation);
        if (value.Status != "Accepted") throw new InvalidOperationException();
    }
    public async Task<SetupPending> Setup(VerifyRegistration body, CancellationToken cancellation) {
        var value = await Send<SetupPending>("auth/register/setup", body, null, [202], cancellation);
        if (value.Status != "Accepted" || !Input.Id(value.SetupChallengeId)) throw new InvalidOperationException(); return value;
    }
    public async Task<CompletionResult> Complete(CompleteRegistration body, string key, CancellationToken cancellation) {
        var value = await Send<CompletionResult>("auth/register/complete", body, key, [200,202], cancellation);
        Validate(new(value.RegistrationId,value.Status,value.OwnershipCommittedAt,value.ReadyAt));
        if (value.CredentialOutcome is not ("CandidateSelected" or "ExistingWinner" or "Undetermined")
            || value.Status == "Ready" && value.CredentialOutcome == "Undetermined") throw new InvalidOperationException(); return value;
    }
    private sealed record Accepted(string Status);
    private sealed record Failure(ErrorBody Error, string TraceId);
    private sealed record ErrorBody(string Code, string Message, string? NextAction);
    private async Task<T> Send<T>(string path, object body, string? key, int[] success, CancellationToken cancellation) {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        cancellation=deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post,path) {Content = JsonContent.Create(body, options: Json)};
        if (key is not null) request.Headers.Add("Idempotency-Key",key);
        using var response = await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);
        if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new InvalidOperationException();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        var bytes = new byte[32769]; var used = 0;
        while (used < bytes.Length) {var size = await stream.ReadAsync(bytes.AsMemory(used),cancellation);if(size==0)break;used+=size;}
        if (used > 32768) throw new InvalidOperationException();
        if (success.Contains((int)response.StatusCode)) return JsonSerializer.Deserialize<T>(bytes.AsSpan(0,used),Json) ?? throw new InvalidOperationException();
        var failure = JsonSerializer.Deserialize<Failure>(bytes.AsSpan(0,used),Json) ?? throw new InvalidOperationException();
        var status = (int)response.StatusCode; var code = failure.Error?.Code;
        if (path == "auth/register/verify" && status == 409 && code == "CONTACT_UNAVAILABLE" && failure.Error?.NextAction is "RequestSetup" or "SignIn")
            throw new RegistrationNextAction(failure.Error.NextAction);
        if (status == 400 && code is "VALIDATION_FAILED" or "VERIFICATION_FAILED") throw new AccountRejection(status,code);
        if (status == 409 && code == "IDEMPOTENCY_CONFLICT") throw new AccountRejection(status,code);
        if (status == 429 && code == "RATE_LIMITED") {
            var seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds;
            if (seconds is null || seconds < 1 || seconds > 3600 || seconds != Math.Floor(seconds.Value)) throw new InvalidOperationException();
            throw new AccountRejection(429,code,(int)seconds.Value);
        }
        throw new InvalidOperationException("Private registration unavailable.");
    }
    private static void Validate(RegistrationResult value) {
        if (value.RegistrationId == Guid.Empty || value.Status is not ("PendingCredential" or "Ready")
            || value.OwnershipCommittedAt == default || (value.Status == "Ready") != (value.ReadyAt is not null)) throw new InvalidOperationException();
    }
    public void Dispose() => client.Dispose();
}
public sealed class RegistrationNextAction(string action) : Exception {public string Action {get;} = action;}
