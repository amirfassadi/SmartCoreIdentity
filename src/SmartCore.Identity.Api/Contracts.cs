using System.Net.Mail;
using System.Text.RegularExpressions;

namespace SmartCore.Identity;

public sealed record StartRegistration(string? Email, string? Mobile, string Password, string DisplayName, string BindingSecret);
public sealed record VerifyRegistration(string VerificationSessionId, string Code, string BindingSecret);
public sealed record ResendVerification(string VerificationSessionId, string BindingSecret);
public sealed record CompleteRegistration(string SetupChallengeId, string Code, string BindingSecret, string NewPassword);
public sealed record SetupPending(string SetupChallengeId, DateTimeOffset ExpiresAt, string Status = "Accepted");
public sealed record CompletionResult(Guid RegistrationId, string Status, DateTimeOffset OwnershipCommittedAt,
    DateTimeOffset? ReadyAt, string CredentialOutcome);
public sealed record VerificationPending(string VerificationSessionId, DateTimeOffset ExpiresAt, string Status = "AwaitingVerification");
public sealed record RegistrationResult(Guid RegistrationId, string Status, DateTimeOffset OwnershipCommittedAt, DateTimeOffset? ReadyAt);
public sealed class ApiError(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string? NextAction { get; init; }
}

public static class Input
{
    public static void Require(bool valid, string code = "VALIDATION_FAILED", int status = 400)
    { if (!valid) throw new ApiError(status, code); }
    public static bool Secret(string? value) => value is not null && Regex.IsMatch(value, @"\A[A-Za-z0-9_-]{43}\z");
    public static bool Id(string? value) => value is not null && Regex.IsMatch(value, @"\A[A-Za-z0-9_-]{32,128}\z");
    public static (string Kind, string Contact) Validate(StartRegistration request, string idempotencyKey)
    {
        Require(Id(idempotencyKey) && Secret(request.BindingSecret));
        Require(request.Password is { Length: >= 15 and <= 128 });
        Require(!string.IsNullOrWhiteSpace(request.DisplayName) && request.DisplayName.Length <= 100);
        return Contact(request.Email,request.Mobile);
    }
    public static (string Kind,string Contact) Contact(string? emailInput,string? mobileInput)
    {
        Require((emailInput is null) != (mobileInput is null));
        if (emailInput is not null)
        {
            var email = emailInput.Trim().ToLowerInvariant();
            Require(email.Length <= 254 && MailAddress.TryCreate(email, out var parsed)
                && parsed.Address == email && email.Contains('@') && !email.Any(char.IsWhiteSpace));
            return ("email", email);
        }
        Require(Regex.IsMatch(mobileInput!, @"\A\+[1-9][0-9]{1,14}\z"));
        return ("mobile", mobileInput!);
    }
    public static void Validate(VerifyRegistration request)
    {
        Require(Id(request.VerificationSessionId) && Secret(request.BindingSecret));
        Require(request.Code is not null && Regex.IsMatch(request.Code, @"\A[0-9]{6}\z"));
    }
}

public static class Timestamps
{
    // PostgreSQL stores microseconds; round before returning first-write results so replay is identical.
    public static DateTimeOffset Now(TimeProvider clock)
    {
        var ticks=clock.GetUtcNow().UtcTicks;
        return new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero);
    }
}
