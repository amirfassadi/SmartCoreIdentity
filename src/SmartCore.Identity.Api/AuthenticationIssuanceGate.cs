using Npgsql;

namespace SmartCore.Identity;

// Session-storage adapter contract, not a domain Aggregate or cross-store transaction.
// Caller owns the transaction and must keep the gate until its Session writes commit.
public interface IAuthenticationIssuanceGate
{
    Task<IssuanceGateState> Acquire(NpgsqlTransaction sessionTransaction, Guid personId);
}

// Valid only within the acquiring transaction. Never cache or trust a caller-provided epoch.
public sealed record IssuanceGateState(long Epoch, Guid? PendingOperationId)
{
    public bool AllowsIssuance => PendingOperationId is null;
    public bool AllowsSensitiveOperation(long authenticatedEpoch) => AllowsIssuance && Epoch==authenticatedEpoch;
}

public sealed class PostgresAuthenticationIssuanceGate : IAuthenticationIssuanceGate
{
    public async Task<IssuanceGateState> Acquire(NpgsqlTransaction sessionTransaction, Guid personId)
    {
        var connection=sessionTransaction.Connection ?? throw new InvalidOperationException("Active Session transaction required.");
        // Always before Session/family/generation rows. A future multi-Person writer must sort Person IDs.
        // Never acquire credential:* while holding auth-person:* or vice versa.
        // Credential evidence is read without Credential locks and revalidated under this gate.
        // Creating this unfenced epoch-zero row is not proof admission or a password-change fence.
        await connection.Execute("SELECT pg_advisory_xact_lock(hashtextextended(@id,0))",("id","auth-person:"+personId));
        await connection.Execute("INSERT INTO auth_issuance_state(person_id) VALUES(@person) ON CONFLICT DO NOTHING",("person",personId));
        var row=await connection.One("SELECT epoch,pending_operation_id FROM auth_issuance_state WHERE person_id=@person FOR UPDATE",("person",personId));
        var pending=row!.Has("pending_operation_id") ? row.Get<Guid>("pending_operation_id") : (Guid?)null;
        return new(row.Get<long>("epoch"),pending);
    }
}
