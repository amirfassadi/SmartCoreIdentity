using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartCore.Identity;
namespace SmartCore.Identity.Bff.Host;

public sealed class IdentityRecoveryHttp(HostConfiguration configuration) : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler {AllowAutoRedirect=false,UseCookies=false}) {
        BaseAddress=configuration.IdentityOrigin,Timeout=TimeSpan.FromSeconds(5)
    };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    public string Subject(string value) => Convert.ToBase64String(configuration.SessionSecrets.Mac("browser-recovery-subject-v1",value)).TrimEnd('=').Replace('+','-').Replace('/','_');
    public async Task<RecoveryEnrollmentResult> Enroll(EnrollRecoveryCodeRequest body, string access, string subject, CancellationToken cancellation) {
        var value=await Send<RecoveryEnrollmentResult>("auth/recovery/enroll",body,access,subject,200,cancellation);
        if(value.OperationId!=body.OperationId || value.Version<1 || value.Status is not ("Issued" or "AlreadyIssued")
            || value.Status=="Issued" && (value.RecoveryCode is not {Length:32} || !value.RecoveryCode.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'F'))
            || value.Status=="AlreadyIssued" && value.RecoveryCode is not null) throw new InvalidOperationException();
        return value;
    }
    public async Task Change(ChangePasswordRequest body,string access,string subject,CancellationToken cancellation) => Validate(await Send<ChangeStatus>("auth/password/change",body,access,subject,202,cancellation),body.OperationId);
    public async Task<ResetPending> Start(StartPasswordReset body,string subject,CancellationToken cancellation) {
        var value=await Send<ResetPending>("auth/password/reset",body,null,subject,202,cancellation);
        if(value.Status!="Accepted" || !Input.Secret(value.ChallengeId)) throw new InvalidOperationException();return value;
    }
    public async Task Complete(CompletePasswordReset body,string subject,CancellationToken cancellation) => Validate(await Send<ChangeStatus>("auth/password/reset/complete",body,null,subject,202,cancellation),body.OperationId);
    private static void Validate(ChangeStatus value,Guid operation) {
        if(value.OperationId!=operation || value.Stage is not ("Fenced" or "AwaitingCredential" or "Reconciled" or "FailedClosed")) throw new InvalidOperationException();
    }
    private sealed record Failure(ErrorBody Error,string TraceId);
    private sealed record ErrorBody(string Code,string Message,string? NextAction);
    private async Task<T> Send<T>(string path,object body,string? access,string subject,int success,CancellationToken cancellation) {
        using var request=new HttpRequestMessage(HttpMethod.Post,path) {Content=JsonContent.Create(body,options:Json)};
        request.Headers.Add("X-Bff-Client-Key",configuration.ClientKey);request.Headers.Add("X-Bff-Subject",subject);
        if(access is not null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);
        if(response.Content.Headers.ContentType?.MediaType!="application/json")throw new InvalidOperationException();
        await using var stream=await response.Content.ReadAsStreamAsync(cancellation);
        var bytes=new byte[32769];var used=0;
        while(used<bytes.Length){var n=await stream.ReadAsync(bytes.AsMemory(used),cancellation);if(n==0)break;used+=n;}
        if(used>32768)throw new InvalidOperationException();
        if((int)response.StatusCode==success)return JsonSerializer.Deserialize<T>(bytes.AsSpan(0,used),Json) ?? throw new InvalidOperationException();
        var failure=JsonSerializer.Deserialize<Failure>(bytes.AsSpan(0,used),Json) ?? throw new InvalidOperationException();
        var status=(int)response.StatusCode;var code=failure.Error?.Code;
        // Wrong current password and reset factors use the same private 401 envelope
        // as other denials. It is NOT sufficient evidence to terminalize a BFF session.
        if(status==401 && code=="UNAUTHORIZED")throw new AccountRejection(400,path.StartsWith("auth/password/reset",StringComparison.Ordinal)?"RESET_PROOF_REJECTED":"CREDENTIALS_REJECTED");
        if(status==400 && code=="VALIDATION_FAILED")throw new AccountRejection(400,code);
        if(status==409 && code=="IDEMPOTENCY_CONFLICT")throw new AccountRejection(409,code);
        if(status==409 && code=="RECOVERY_VERSION_EXHAUSTED")throw new AccountRejection(409,"RECOVERY_UNAVAILABLE");
        if(status==429 && code=="RATE_LIMITED")throw new AccountRejection(429,code,60);
        if(status==503 && code is "AUTHENTICATION_BUSY" or "TEMPORARILY_UNAVAILABLE")throw new AccountRejection(503,"BFF_UNAVAILABLE",60);
        throw new InvalidOperationException("Private recovery unavailable.");
    }
    public void Dispose()=>client.Dispose();
}
