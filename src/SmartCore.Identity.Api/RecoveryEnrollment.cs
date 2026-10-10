using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Npgsql;

namespace SmartCore.Identity;

public sealed record EnrollRecoveryCodeRequest(Guid OperationId,string CurrentPassword,
    [property:JsonRequired] bool AcceptLossRisk);
public sealed record RecoveryEnrollmentResult(Guid OperationId,long Version,string Status,string? RecoveryCode=null);

// Only authenticated current-password enrollment. This is not an administrative reset route.
public sealed class RecoveryEnrollment(Database db,Secrets secrets,TimeProvider clock,IAuthenticationIssuanceGate gate,AccessTokens access)
{
    private static ApiError Denied()=>new(401,"UNAUTHORIZED");
    private static Task<Row?> Evidence(NpgsqlConnection c,Guid person)=>c.One("""
        SELECT c.*,p.status AS person_status,r.status AS registration_status,w.phase FROM credentials c
        JOIN persons p ON p.id=c.person_id JOIN registrations r ON r.person_id=p.id
        JOIN initial_credential_winners w ON w.person_id=p.id WHERE p.id=@id
        """,("id",person));
    private static bool Ready(Row? row)=>row is not null && row.Get<string>("status")=="Active"
        && row.Get<string>("person_status")=="Active" && row.Get<string>("registration_status")=="Ready"
        && row.Get<string>("phase")=="ReadyAcknowledged";
    public byte[] Verifier(Guid person,long version,string code)=>secrets.Mac("recovery-code-v1",person.ToString("N"),version.ToString(System.Globalization.CultureInfo.InvariantCulture),code);
    public async Task<RecoveryEnrollmentResult> Enroll(string bearer,string client,EnrollRecoveryCodeRequest request)
    {
        Input.Require(request.OperationId!=Guid.Empty && request.AcceptLossRisk && request.CurrentPassword is {Length:>=1 and <=128});
        var proof=access.Read(bearer,client);
        var binding=secrets.Mac("recovery-enrollment-v1",request.OperationId.ToString(),proof.PersonId.ToString(),proof.SessionId.ToString(),client);
        await using var c=await db.Source.OpenConnectionAsync();
        var previous=await c.One("SELECT * FROM auth_recovery_enrollments WHERE operation_id=@id",("id",request.OperationId));
        var evidence=await Evidence(c,proof.PersonId);
        if(!Ready(evidence)) throw Denied();
        // Returning non-secret receipt status never requires or repeats a new code.
        if(previous is null)
        {
            var failures=new PasswordFailures(db,clock);
            if(!await failures.Allowed(proof.PersonId)) throw Denied();
            var verified=await PasswordWork.Verify(request.CurrentPassword,evidence!.Get<string>("password_hash"));
            await failures.Record(proof.PersonId,verified);
            if(!verified) throw Denied();
        }
        await using var tx=await c.BeginTransactionAsync();
        var state=await gate.Acquire(tx,proof.PersonId);var now=Timestamps.Now(clock);
        _=access.Read(bearer,client); // Queue/KDF or gate wait may cross access expiry.
        var current=await Evidence(c,proof.PersonId);
        var session=await c.One("SELECT * FROM auth_sessions WHERE id=@id FOR UPDATE",("id",proof.SessionId));
        var family=await c.One("SELECT status FROM auth_refresh_families WHERE session_id=@id",("id",proof.SessionId));
        if(!state.AllowsSensitiveOperation(proof.Epoch) || !Ready(current)
            || current!.Get<Guid>("id")!=evidence!.Get<Guid>("id") || current.Get<string>("password_hash")!=evidence.Get<string>("password_hash")
            || session is null || session.Get<Guid>("person_id")!=proof.PersonId || session.Get<string>("client_id")!=client
            || session.Get<long>("issuance_epoch")!=proof.Epoch || session.Get<string>("status")!="Active"
            || session.Time("expires_at")<=now || session.Time("last_foreground_refresh_at").AddMinutes(30)<=now
            || family?.Get<string>("status")!="Active") throw Denied();
        previous=await c.One("SELECT * FROM auth_recovery_enrollments WHERE operation_id=@id",("id",request.OperationId));
        if(previous is not null)
        {
            if(previous.Get<Guid>("person_id")!=proof.PersonId || previous.Get<Guid>("session_id")!=proof.SessionId
                || previous.Get<string>("client_id")!=client || !Secrets.Equal(previous.Get<byte[]>("request_mac"),binding))
                throw new ApiError(409,"IDEMPOTENCY_CONFLICT");
            return new(request.OperationId,previous.Get<long>("version"),"AlreadyIssued");
        }
        var old=await c.One("SELECT version FROM auth_recovery_codes WHERE person_id=@id FOR UPDATE",("id",proof.PersonId));
        if(old?.Get<long>("version")==long.MaxValue) throw new ApiError(409,"RECOVERY_VERSION_EXHAUSTED");
        var version=(old?.Get<long>("version") ?? 0)+1;
        var code=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); // 128 random bits, shown once.
        await c.Execute("""
            INSERT INTO auth_recovery_codes(person_id,version,enrolled_epoch,enrollment_session_id,verifier,enrolled_at) VALUES(@person,@version,@epoch,@session,@verifier,@now)
            ON CONFLICT(person_id) DO UPDATE SET version=EXCLUDED.version,enrolled_epoch=EXCLUDED.enrolled_epoch,
              enrollment_session_id=EXCLUDED.enrollment_session_id,verifier=EXCLUDED.verifier,enrolled_at=EXCLUDED.enrolled_at,
              consumed_at=NULL,consumed_operation_id=NULL,reserved_intent_id=NULL
            """,("person",proof.PersonId),("version",version),("epoch",state.Epoch),("session",proof.SessionId),
            ("verifier",Verifier(proof.PersonId,version,code)),("now",now));
        await c.Execute("INSERT INTO auth_recovery_enrollments VALUES(@id,@person,@session,@client,@version,@epoch,@mac,@now)",
            ("id",request.OperationId),("person",proof.PersonId),("session",proof.SessionId),("client",client),
            ("version",version),("epoch",state.Epoch),("mac",binding),("now",now));
        await c.Execute("INSERT INTO auth_account_notifications(id,person_id,operation_id,kind,created_at) VALUES(@id,@person,@op,@kind,@now)",
            ("id",Guid.NewGuid()),("person",proof.PersonId),("op",request.OperationId),("kind",old is null?"RecoveryCodeRegistered":"RecoveryCodeReplaced"),("now",now));
        await tx.CommitAsync();
        return new(request.OperationId,version,"Issued",code);
    }
}
