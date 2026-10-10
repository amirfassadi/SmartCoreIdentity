using Npgsql;

namespace SmartCore.Identity;

public sealed record StartPasswordReset(Guid OperationId,string? Email,string? Mobile,string BindingSecret);
public sealed record ResetPending(string ChallengeId,DateTimeOffset ExpiresAt,string Status="Accepted");

public sealed class PasswordResetInitiation(Database db,Secrets secrets,TimeProvider clock,IAuthenticationIssuanceGate gate)
{
    private static Task<Row?> Existing(NpgsqlConnection c,Guid operation)=>c.One("""
        SELECT id,client_id,request_mac,expires_at FROM auth_reset_challenges WHERE operation_id=@id
        UNION ALL SELECT id,client_id,request_mac,expires_at FROM auth_reset_decoys WHERE operation_id=@id
        """,("id",operation));
    private ResetPending Replay(Row row,string client,byte[] mac)
    {
        if(row.Get<string>("client_id")!=client || !Secrets.Equal(row.Get<byte[]>("request_mac"),mac)) throw new ApiError(409,"IDEMPOTENCY_CONFLICT");
        if(row.Time("expires_at")<=Timestamps.Now(clock)) throw new ApiError(401,"UNAUTHORIZED");
        return new(row.Get<string>("id"),row.Time("expires_at"));
    }
    private static Task<Row?> Eligible(NpgsqlConnection c,Guid person)=>c.One("""
        SELECT rc.version FROM auth_recovery_codes rc JOIN persons p ON p.id=rc.person_id
        JOIN credentials c ON c.person_id=p.id JOIN registrations r ON r.person_id=p.id
        JOIN initial_credential_winners w ON w.person_id=p.id
        WHERE p.id=@id AND p.status='Active' AND c.status='Active' AND r.status='Ready'
          AND w.phase='ReadyAcknowledged' AND rc.verifier IS NOT NULL AND rc.reserved_intent_id IS NULL
        """,("id",person));
    public async Task<ResetPending> Start(string client,StartPasswordReset request)
    {
        Input.Require(request.OperationId!=Guid.Empty && Input.Secret(request.BindingSecret));
        var (kind,contact)=Input.Contact(request.Email,request.Mobile);
        var binding=secrets.Mac("reset-binding-v1",client,request.BindingSecret);
        var mac=secrets.Mac("reset-initiation-v1",request.OperationId.ToString(),client,kind,contact,request.BindingSecret);
        await using var c=await db.Source.OpenConnectionAsync();
        var previous=await Existing(c,request.OperationId);
        if(previous is not null) return Replay(previous,client,mac);
        var person=await c.One("SELECT id FROM persons WHERE contact_kind=@kind AND contact=@contact",("kind",kind),("contact",contact));
        Guid? candidate=null;Row? eligible=null;
        if(person is not null) eligible=await Eligible(c,person.Get<Guid>("id"));
        // Autocommit reservation finishes BEFORE the Person gate. Denied/unsupported requests never take it.
        if(eligible is not null)
        {
            var id=person!.Get<Guid>("id");var now=Timestamps.Now(clock);
            var budget=(await c.One("""
                INSERT INTO auth_reset_initiation_budgets VALUES(@id,@now,1)
                ON CONFLICT(person_id) DO UPDATE SET
                  hits=CASE WHEN auth_reset_initiation_budgets.window_start<=@now-interval '15 minutes' THEN 1 ELSE LEAST(auth_reset_initiation_budgets.hits+1,4) END,
                  window_start=CASE WHEN auth_reset_initiation_budgets.window_start<=@now-interval '15 minutes' THEN @now ELSE auth_reset_initiation_budgets.window_start END
                RETURNING hits,window_start
                """,("id",id),("now",now)))!;
            if(budget.Get<int>("hits")<=3) candidate=id;
            else
            {
                // One non-secret account notice per window; never one outbox row per denied request.
                var bytes=secrets.Mac("reset-budget-notice",id.ToString(),budget.Time("window_start").ToString("O"));
                var notice=new Guid(bytes.AsSpan(0,16));
                await c.Execute("""
                    INSERT INTO auth_account_notifications(id,person_id,operation_id,kind,created_at)
                    VALUES(@id,@person,@op,'ResetRequestsLimited',@now) ON CONFLICT(operation_id,kind) DO NOTHING
                    """,("id",Guid.NewGuid()),("person",id),("op",notice),("now",now));
            }
        }
        // Equal cryptographic work for both shapes does not claim constant-time database paths.
        var challenge=Secrets.Token();var delivery=Guid.NewGuid();var code=Secrets.Code();
        var sealedCode=secrets.Seal(code,"reset-delivery:"+delivery);var verifier=secrets.Mac("reset-otp-v1",challenge,code);
        await using var tx=await c.BeginTransactionAsync();
        await c.Execute("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",("key","reset-initiation:"+request.OperationId));
        previous=await Existing(c,request.OperationId);
        if(previous is not null) return Replay(previous,client,mac);
        long? epoch=null;long? version=null;
        if(candidate is not null)
        {
            var state=await gate.Acquire(tx,candidate.Value);eligible=await Eligible(c,candidate.Value);
            if(state.AllowsIssuance && eligible is not null) {epoch=state.Epoch;version=eligible.Get<long>("version");}
            else candidate=null;
        }
        var created=Timestamps.Now(clock);var expiry=created.AddMinutes(10);
        if(candidate is null)
            await c.Execute("INSERT INTO auth_reset_decoys VALUES(@op,@id,@client,@mac,@now,@expiry)",
                ("op",request.OperationId),("id",challenge),("client",client),("mac",mac),("now",created),("expiry",expiry));
        else
        {
            await c.Execute("INSERT INTO auth_reset_challenges VALUES(@id,@op,@person,@client,@mac,@binding,@epoch,@version,@now,@expiry)",
                ("id",challenge),("op",request.OperationId),("person",candidate),("client",client),("mac",mac),("binding",binding),
                ("epoch",epoch),("version",version),("now",created),("expiry",expiry));
            await c.Execute("INSERT INTO auth_reset_delivery VALUES(@id,@challenge,@person,@sealed,@verifier,@now,@expiry)",
                ("id",delivery),("challenge",challenge),("person",candidate),("sealed",sealedCode),("verifier",verifier),("now",created),("expiry",expiry));
        }
        await tx.CommitAsync();return new(challenge,expiry);
    }
}
