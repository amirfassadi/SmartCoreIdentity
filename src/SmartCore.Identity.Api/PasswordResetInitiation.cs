using Npgsql;

namespace SmartCore.Identity;

public sealed record StartPasswordReset(Guid OperationId,string? Email,string? Mobile,string BindingSecret);
public sealed record ResetPending(string ChallengeId,DateTimeOffset ExpiresAt,string Status="Accepted");

// Initiation only. Neither contact possession nor a queued OTP is reset authorization.
public sealed class PasswordResetInitiation(Database db,Secrets secrets,TimeProvider clock,IAuthenticationIssuanceGate gate)
{
    public async Task<ResetPending> Start(string client,StartPasswordReset request)
    {
        Input.Require(request.OperationId!=Guid.Empty && Input.Secret(request.BindingSecret));
        var (kind,contact)=Input.Contact(request.Email,request.Mobile);
        var binding=secrets.Mac("reset-binding-v1",client,request.BindingSecret);
        var mac=secrets.Mac("reset-initiation-v1",request.OperationId.ToString(),client,kind,contact,request.BindingSecret);
        await using var c=await db.Source.OpenConnectionAsync();
        // Request-specific lock, separate from Credential ownership. Never holds a Person gate first.
        await using var tx=await c.BeginTransactionAsync();
        await c.Execute("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",("key","reset-initiation:"+request.OperationId));
        var previous=await c.One("SELECT * FROM auth_reset_challenges WHERE operation_id=@id",("id",request.OperationId));
        if(previous is not null)
        {
            if(previous.Get<string>("client_id")!=client || !Secrets.Equal(previous.Get<byte[]>("request_mac"),mac)) throw new ApiError(409,"IDEMPOTENCY_CONFLICT");
            if(previous.Time("expires_at")<=Timestamps.Now(clock)) throw new ApiError(401,"UNAUTHORIZED");
            return new(previous.Get<string>("id"),previous.Time("expires_at")); // No extension or redelivery.
        }
        var person=await c.One("SELECT id FROM persons WHERE contact_kind=@kind AND contact=@contact",("kind",kind),("contact",contact));
        Guid? candidate=null;long? epoch=null;long? version=null;
        var now=Timestamps.Now(clock);
        if(person is not null)
        {
            var id=person.Get<Guid>("id");var state=await gate.Acquire(tx,id);
            var budget=(await c.One("""
                INSERT INTO auth_reset_initiation_budgets VALUES(@id,@now,1)
                ON CONFLICT(person_id) DO UPDATE SET
                  hits=CASE WHEN auth_reset_initiation_budgets.window_start<=@now-interval '15 minutes' THEN 1 ELSE LEAST(auth_reset_initiation_budgets.hits+1,4) END,
                  window_start=CASE WHEN auth_reset_initiation_budgets.window_start<=@now-interval '15 minutes' THEN @now ELSE auth_reset_initiation_budgets.window_start END
                RETURNING hits
                """,("id",id),("now",now)))!;
            var eligible=await c.One("""
                SELECT rc.version FROM auth_recovery_codes rc JOIN persons p ON p.id=rc.person_id
                JOIN credentials c ON c.person_id=p.id JOIN registrations r ON r.person_id=p.id
                JOIN initial_credential_winners w ON w.person_id=p.id
                WHERE p.id=@id AND p.status='Active' AND c.status='Active' AND r.status='Ready' AND w.phase='ReadyAcknowledged'
                """,("id",id));
            if(state.AllowsIssuance && eligible is not null && budget.Get<int>("hits")<=3)
            {candidate=id;epoch=state.Epoch;version=eligible.Get<long>("version");}
        }
        var challenge=Secrets.Token();var expiry=now.AddMinutes(10);
        await c.Execute("INSERT INTO auth_reset_challenges VALUES(@id,@op,@person,@client,@mac,@binding,@epoch,@version,@now,@expiry)",
            ("id",challenge),("op",request.OperationId),("person",candidate),("client",client),("mac",mac),("binding",binding),
            ("epoch",epoch),("version",version),("now",now),("expiry",expiry));
        if(candidate is not null)
        {
            var delivery=Guid.NewGuid();var code=Secrets.Code();
            await c.Execute("INSERT INTO auth_reset_delivery VALUES(@id,@challenge,@person,@sealed,@verifier,@now,@expiry)",
                ("id",delivery),("challenge",challenge),("person",candidate),("sealed",secrets.Seal(code,"reset-delivery:"+delivery)),
                ("verifier",secrets.Mac("reset-otp-v1",challenge,code)),("now",now),("expiry",expiry));
        }
        await tx.CommitAsync();
        return new(challenge,expiry);
    }
}
