using Npgsql;

namespace SmartCore.Identity;

public sealed record CompletePasswordReset(Guid OperationId,string ChallengeId,string BindingSecret,string Code,string RecoveryCode,string NewPassword);

public sealed class PasswordResetCompletion(Database db,Secrets secrets,TimeProvider clock,IAuthenticationIssuanceGate gate,PasswordChanges changes)
{
    private static ApiError Denied()=>new(401,"UNAUTHORIZED");
    private byte[] ProofMac(string client,CompletePasswordReset r)=>secrets.Mac("reset-proof-v1",client,r.ChallengeId,r.BindingSecret,r.Code,r.RecoveryCode);
    private static Task<Row?> Challenge(NpgsqlConnection c,string id)=>c.One("SELECT * FROM auth_reset_challenges WHERE id=@id",("id",id));
    private static Task<Row?> Acceptance(NpgsqlConnection c,Guid id)=>c.One("SELECT * FROM auth_reset_acceptances WHERE intent_id=@id",("id",id));
    private bool Bound(Row ch,string client,CompletePasswordReset r)=>ch.Get<string>("client_id")==client
        && ch.Time("expires_at")>Timestamps.Now(clock)
        && Secrets.Equal(ch.Get<byte[]>("binding_mac"),secrets.Mac("reset-binding-v1",client,r.BindingSecret));
    private async Task<bool> Factors(NpgsqlConnection c,Row ch,CompletePasswordReset r)
    {
        var delivery=await c.One("SELECT verifier,otp_digits FROM auth_reset_delivery WHERE challenge_id=@id",("id",r.ChallengeId));
        var rc=await c.One("SELECT * FROM auth_recovery_codes WHERE person_id=@id",("id",ch.Get<Guid>("person_id")));
        var otp=secrets.Mac("reset-otp-v1",r.ChallengeId,r.Code);
        var recovery=secrets.Mac("recovery-code-v1",ch.Get<Guid>("person_id").ToString("N"),
            ch.Get<long>("recovery_version").ToString(System.Globalization.CultureInfo.InvariantCulture),r.RecoveryCode);
        // Evaluate both constant-time comparisons; never report which proof failed.
        var correctOtp=Secrets.Equal(delivery is null?new byte[32]:delivery.Get<byte[]>("verifier"),otp);
        var correctCode=Secrets.Equal(rc is not null && rc.Has("verifier")?rc.Get<byte[]>("verifier"):new byte[32],recovery);
        return correctOtp & correctCode & (delivery is not null && delivery.Get<int>("otp_digits")==r.Code.Length) & (rc is not null && !rc.Has("reserved_intent_id") && rc.Get<long>("version")==ch.Get<long>("recovery_version"));
    }
    // This independent transaction never acquires the Person or Credential gate.
    // Five failures per Person window, escalating cooldown on each exhausted window.
    // No Person/Credential gate overlaps the counter transaction.
    private async Task<bool> VerifyBudgeted(Row ch,string client,CompletePasswordReset r,Row? accepted)
    {
        await using var c=await db.Source.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        var person=ch.Get<Guid>("person_id");var now=Timestamps.Now(clock);
        await c.Execute("INSERT INTO auth_reset_failures(person_id,window_start,failures) VALUES(@id,@now,0) ON CONFLICT DO NOTHING",("id",person),("now",now));
        var budget=(await c.One("SELECT * FROM auth_reset_failures WHERE person_id=@id FOR UPDATE",("id",person)))!;
        var quiet=budget.Has("last_failure_at") && budget.Time("last_failure_at").AddHours(24)<=now;
        var streak=quiet?0:budget.Get<int>("saturated_windows");
        if(!quiet && budget.Has("blocked_until") && budget.Time("blocked_until")>now) return false;
        var reset=quiet || budget.Time("window_start").AddMinutes(15)<=now;
        var failures=reset?0:budget.Get<int>("failures");var window=reset?now:budget.Time("window_start");
        if(failures>=5) return false;
        var current=await Challenge(c,r.ChallengeId);
        if(current is null) return false;
        // Fresh challenges cap proof presentations, but cannot reset the Person failure budget.
        if(accepted is null)
        {
            var attempt=await c.One("""
                INSERT INTO auth_reset_attempts VALUES(@id,1) ON CONFLICT(challenge_id)
                  DO UPDATE SET attempts=auth_reset_attempts.attempts+1 WHERE auth_reset_attempts.attempts<5 RETURNING attempts
                """,("id",r.ChallengeId));
            if(attempt is null) return false;
        }
        var valid=Bound(current,client,r) && (accepted is null?await Factors(c,current,r):
            accepted.Get<string>("challenge_id")==r.ChallengeId && Secrets.Equal(accepted.Get<byte[]>("proof_mac"),ProofMac(client,r)));
        var next=valid?failures:failures+1;
        DateTimeOffset? blocked=null;
        if(!valid && next==5)
        {
            streak=Math.Min(streak+1,6);
            blocked=now.AddMinutes(Math.Min(15*(1<<(streak-1)),360));
            var notice=new Guid(secrets.Mac("reset-proof-limit-notice",person.ToString(),window.ToString("O")).AsSpan(0,16));
            await c.Execute("""
                INSERT INTO auth_account_notifications(id,person_id,operation_id,kind,created_at)
                VALUES(@id,@person,@op,'ResetProofFailuresLimited',@now) ON CONFLICT(operation_id,kind) DO NOTHING
                """,("id",Guid.NewGuid()),("person",person),("op",notice),("now",now));
        }
        await c.Execute("""
            UPDATE auth_reset_failures SET failures=@n,window_start=@window,saturated_windows=@streak,
              blocked_until=@blocked,last_failure_at=CASE WHEN @valid THEN last_failure_at ELSE @now END
            WHERE person_id=@id
            """,("n",next),("window",window),("streak",streak),("blocked",blocked),("valid",valid),("now",now),("id",person));
        await tx.CommitAsync();return valid;
    }
    private async Task<ChangeStatus> Replay(NpgsqlConnection c,Row ch,Row accepted,string client,CompletePasswordReset r)
    {
        if(!await VerifyBudgeted(ch,client,r,accepted)) throw Denied();
        var prior=await PasswordChanges.Load(c,r.OperationId) ?? throw Denied();
        if(prior.Get<string>("kind")!="ResetPassword" || prior.Get<string>("client_id")!=client
            || !prior.Has("sealed_replay_hash") || prior.Time("replay_until")<=Timestamps.Now(clock)) throw Denied();
        if(!await PasswordWork.Verify(r.NewPassword,secrets.Open(prior.Get<byte[]>("sealed_replay_hash"),"password-change-replay:"+r.OperationId)))
            throw new ApiError(409,"IDEMPOTENCY_CONFLICT");
        if(!Bound(ch,client,r) || prior.Time("replay_until")<=Timestamps.Now(clock)) throw Denied();
        return new(r.OperationId,prior.Get<string>("stage"));
    }
    public Task<ChangeStatus> Accept(string client,CompletePasswordReset request)=>ResetResponseTiming.Run("completion",()=>AcceptCore(client,request));
    private async Task<ChangeStatus> AcceptCore(string client,CompletePasswordReset request)
    {
        Input.Require(request.RecoveryCode is {Length:>=1 and <=64});
        request=request with {RecoveryCode=request.RecoveryCode.Trim().ToUpperInvariant()};
        Input.Require(request.OperationId!=Guid.Empty && Input.Secret(request.ChallengeId) && Input.Secret(request.BindingSecret)
            && request.Code is {Length:6 or 8} && request.Code.All(char.IsAsciiDigit)
            && request.RecoveryCode is {Length:32} && request.RecoveryCode.All(char.IsAsciiHexDigit)
            && request.NewPassword is {Length:>=15 and <=128});
        await using var c=await db.Source.OpenConnectionAsync();
        var ch=await Challenge(c,request.ChallengeId);
        if(ch is null) {_=ProofMac(client,request);throw Denied();}
        var accepted=await Acceptance(c,request.OperationId);
        if(accepted is not null) return await Replay(c,ch,accepted,client,request);
        if(!await VerifyBudgeted(ch,client,request,null))
        {
            accepted=await Acceptance(c,request.OperationId);
            if(accepted is not null) return await Replay(c,ch,accepted,client,request);
            throw Denied();
        }
        var person=ch.Get<Guid>("person_id");
        var evidence=await PasswordChanges.Evidence(c,person);
        if(!PasswordChanges.Ready(evidence)) throw Denied();
        var replacement=await PasswordWork.Hash(request.NewPassword); // No locks during Argon2.
        await using var tx=await c.BeginTransactionAsync();var state=await gate.Acquire(tx,person);
        accepted=await Acceptance(c,request.OperationId);
        if(accepted is not null) {await tx.RollbackAsync();return await Replay(c,ch,accepted,client,request);}
        if(await PasswordChanges.Load(c,request.OperationId) is not null) throw new ApiError(409,"IDEMPOTENCY_CONFLICT");
        ch=await Challenge(c,request.ChallengeId);var current=await PasswordChanges.Evidence(c,person);
        if(ch is null || !Bound(ch,client,request) || !state.AllowsSensitiveOperation(ch.Get<long>("observed_epoch"))
            || !PasswordChanges.Ready(current) || current!.Get<Guid>("id")!=evidence!.Get<Guid>("id")
            || current.Get<string>("password_hash")!=evidence.Get<string>("password_hash") || !await Factors(c,ch,request)) throw Denied();
        // Both factors and epoch are rechecked under the gate before any fencing write.
        await changes.AdmitVerified(c,state,request.OperationId,person,client,null,current,replacement,
            secrets.Mac("password-reset-request-v1",request.OperationId.ToString(),person.ToString(),request.ChallengeId,client),
            "ResetPassword",ch.Time("expires_at"));
        await c.Execute("INSERT INTO auth_reset_acceptances VALUES(@id,@challenge,@version,@proof)",("id",request.OperationId),
            ("challenge",request.ChallengeId),("version",ch.Get<long>("recovery_version")),("proof",ProofMac(client,request)));
        await c.Execute("UPDATE auth_recovery_codes SET reserved_intent_id=@id WHERE person_id=@person",("id",request.OperationId),("person",person));
        await c.Execute("UPDATE auth_reset_delivery SET sealed_code=NULL WHERE challenge_id=@id",("id",request.ChallengeId));
        await tx.CommitAsync();return new(request.OperationId,"Fenced");
    }
}
