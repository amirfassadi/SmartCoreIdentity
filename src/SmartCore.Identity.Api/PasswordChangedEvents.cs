using System.Text.Json;

namespace SmartCore.Identity;

public static class PasswordChangedEvents
{
    public static Dictionary<string,object> Create(Guid eventId,Guid credential,Row intent,DateTimeOffset now)
    {
        var context=new Dictionary<string,object>{{"CorrelationId",intent.Get<Guid>("id").ToString()}};
        var envelope=new Dictionary<string,object>{{"EventId",eventId},{"EventType","PasswordChanged"},{"AggregateType","Credential"},
            {"AggregateId",credential},{"OccurredAt",now},{"ActorIdentity",intent.Get<Guid>("person_id")},{"ExecutionContext",context},
            {"Payload",new {PersonId=intent.Get<Guid>("person_id"),CredentialId=credential}}};
        if(intent.Get<string>("kind")=="ResetPassword") context["RecoveryProofReference"]=intent.Get<Guid>("id");
        else if(intent.Get<string>("kind")=="ChangePassword" && intent.Has("session_id")) envelope["SessionReference"]=intent.Get<Guid>("session_id");
        else throw new InvalidOperationException("IntegrityConflict");
        ValidateContext(JsonSerializer.SerializeToElement(envelope));
        return envelope;
    }
    // Runtime guard for the owner-selected context union. Full envelope schema is checked in CI.
    public static void ValidateContext(JsonElement envelope)
    {
        var session=envelope.TryGetProperty("SessionReference",out var s);
        var recovery=envelope.TryGetProperty("ExecutionContext",out var context)
            && context.TryGetProperty("RecoveryProofReference",out _);
        if(session==recovery || (session && (!s.TryGetGuid(out var sessionId) || sessionId==Guid.Empty)))
            throw new InvalidOperationException("IntegrityConflict");
        if(recovery && (!context.GetProperty("RecoveryProofReference").TryGetGuid(out var resetId) || resetId==Guid.Empty))
            throw new InvalidOperationException("IntegrityConflict");
    }
}
