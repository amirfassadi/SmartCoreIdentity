using Npgsql;

namespace SmartCore.Identity;

// Pending-registration completion only. This is never a forgotten-password/reset path.
public sealed class RegistrationSetupService(Database db, Secrets secrets, TimeProvider clock, Provisioning provisioning)
{
    public async Task<SetupPending> Request(VerifyRegistration request)
    {
        Input.Validate(request);
        await using var connection=await db.Source.OpenConnectionAsync();
        await using var tx=await connection.BeginTransactionAsync();
        var row=await connection.One("SELECT * FROM verification_sessions WHERE id=@id FOR UPDATE",("id",request.VerificationSessionId));
        var now=Timestamps.Now(clock);
        Input.Require(row is not null && !row.Get<bool>("invalidated") && row.Time("expires_at")>now
            && row.Get<int>("attempts")<5,"VERIFICATION_FAILED",400);
        var valid=Secrets.Equal(row!.Optional<byte[]>("binding_mac"),secrets.Mac("binding",request.VerificationSessionId,request.BindingSecret))
            & Secrets.Equal(row.Optional<byte[]>("code_mac"),secrets.Mac("otp",request.VerificationSessionId,request.Code));
        await connection.Execute("UPDATE verification_sessions SET attempts=attempts+1 WHERE id=@id",("id",request.VerificationSessionId));
        if(!valid || !row.Has("setup_registration_id"))
        {
            await tx.CommitAsync();
            throw new ApiError(400,"VERIFICATION_FAILED");
        }
        var previous=await connection.One("SELECT id,expires_at FROM setup_challenges WHERE verification_id=@id",("id",request.VerificationSessionId));
        if(previous is not null)
        {
            await tx.CommitAsync();
            return new(previous.Get<string>("id"),previous.Time("expires_at"));
        }
        var target=await connection.One("SELECT status FROM registrations WHERE id=@id",("id",row.Get<Guid>("setup_registration_id")));
        var id=Secrets.Token(); var expiry=now.AddMinutes(10);
        if(target?.Get<string>("status")!="PendingCredential")
        {
            // Same accepted shape, no deliverable proof and no authority to change a Ready password.
            await tx.CommitAsync();
            return new(id,expiry);
        }
        string code;
        do {code=Secrets.Code();} while(code==request.Code);
        var delivery=Guid.NewGuid();
        await connection.Execute("""
            INSERT INTO setup_challenges(id,verification_id,registration_id,operation_id,binding_mac,code_mac,expires_at,created_at)
            VALUES(@id,@verification,@registration,@operation,@binding,@code,@expiry,@now)
            """,("id",id),("verification",request.VerificationSessionId),("registration",row.Get<Guid>("setup_registration_id")),
            ("operation",Guid.NewGuid()),("binding",secrets.Mac("setup-binding",id,request.BindingSecret)),
            ("code",secrets.Mac("setup-otp",id,code)),("expiry",expiry),("now",now));
        // Destination is resolved through registration -> Person; no caller-supplied contact is accepted.
        await connection.Execute("INSERT INTO setup_delivery_outbox VALUES(@id,@setup,@code,@now,@expiry)",
            ("id",delivery),("setup",id),("code",secrets.Seal(code,"setup-delivery:"+delivery)),("now",now),("expiry",expiry));
        await tx.CommitAsync();
        return new(id,expiry);
    }

    public async Task<CompletionResult> Complete(CompleteRegistration request,string key)
    {
        Input.Require(Input.Id(request.SetupChallengeId) && Input.Secret(request.BindingSecret) && Input.Id(key)
            && request.Code is not null && System.Text.RegularExpressions.Regex.IsMatch(request.Code,@"\A[0-9]{6}\z")
            && request.NewPassword is {Length: >=15 and <=128});
        Guid registration,operation;
        await using(var connection=await db.Source.OpenConnectionAsync())
        await using(var tx=await connection.BeginTransactionAsync())
        {
            // Credential writes lock the registration before its challenges. Take the same
            // per-registration guard before locking a challenge: its update also takes a
            // foreign-key lock on registration, so the inverse order can deadlock.
            var target=await connection.One("SELECT registration_id FROM setup_challenges WHERE id=@id",("id",request.SetupChallengeId));
            Input.Require(target is not null,"VERIFICATION_FAILED",400);
            await connection.Execute("SELECT pg_advisory_xact_lock(hashtextextended(@id,0))",
                ("id","credential:"+target!.Get<Guid>("registration_id")));
            var row=await connection.One("SELECT * FROM setup_challenges WHERE id=@id FOR UPDATE",("id",request.SetupChallengeId));
            var now=Timestamps.Now(clock);
            Input.Require(row is not null && row.Time("expires_at")>now && row.Get<int>("attempts")<5,"VERIFICATION_FAILED",400);
            var valid=Secrets.Equal(row!.Optional<byte[]>("binding_mac"),secrets.Mac("setup-binding",request.SetupChallengeId,request.BindingSecret))
                & Secrets.Equal(row.Optional<byte[]>("code_mac"),secrets.Mac("setup-otp",request.SetupChallengeId,request.Code!));
            await connection.Execute("UPDATE setup_challenges SET attempts=attempts+1 WHERE id=@id",("id",request.SetupChallengeId));
            if(!valid)
            {
                await tx.CommitAsync();
                throw new ApiError(400,"VERIFICATION_FAILED");
            }
            var fingerprint=secrets.Mac("setup-request",request.SetupChallengeId,key,request.NewPassword,request.BindingSecret);
            if(row.Has("accepted_at"))
            {
                if(row.Get<string>("idempotency_key")!=key || !Secrets.Equal(row.Optional<byte[]>("request_mac"),fingerprint))
                {
                    await tx.CommitAsync();
                    throw new ApiError(409,"IDEMPOTENCY_CONFLICT");
                }
            }
            else
            {
                // Compute once for the consumed operation; only encrypted KDF output is retained.
                var hash=await Secrets.HashPassword(request.NewPassword);
                now=Timestamps.Now(clock);
                if(row.Time("expires_at")<=now)
                {
                    await tx.CommitAsync();
                    throw new ApiError(400,"VERIFICATION_FAILED");
                }
                await connection.Execute("""
                    UPDATE setup_challenges SET accepted_at=@now,idempotency_key=@key,request_mac=@request,
                      sealed_password=@material,material_expires_at=@expiry WHERE id=@id
                    """,("id",request.SetupChallengeId),("now",now),("key",key),("request",fingerprint),
                    ("material",secrets.Seal(hash,"setup-candidate:"+row.Get<Guid>("operation_id"))),("expiry",now.AddMinutes(15)));
                await connection.Execute("UPDATE setup_delivery_outbox SET sealed_code=NULL WHERE setup_id=@id",("id",request.SetupChallengeId));
                // Durable redrive, including an earlier exhausted normal attempt. Does not release Credential guards.
                await connection.Execute("UPDATE workflow_jobs SET recovery_needed=false,attempts=0,next_attempt_at=@now WHERE registration_id=@id AND NOT completed",
                    ("id",row.Get<Guid>("registration_id")),("now",now));
            }
            registration=row.Get<Guid>("registration_id"); operation=row.Get<Guid>("operation_id");
            await tx.CommitAsync();
        }
        try
        {
            await provisioning.EnsureCredential(registration,operation);
            await provisioning.MarkReady(registration);
            await provisioning.Acknowledge(registration);
        }
        catch(Exception error) when(error is NpgsqlException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            // Committed acceptance is not reported as rollback. Worker reconciles the same durable candidate.
        }
        await using var resultConnection=await db.Source.OpenConnectionAsync();
        var result=await RegistrationService.Result(resultConnection,registration);
        var winner=await resultConnection.One("SELECT operation_id FROM initial_credential_winners WHERE registration_id=@id",("id",registration));
        var outcome=winner is null ? "Undetermined" : winner.Get<Guid>("operation_id")==operation ? "CandidateSelected" : "ExistingWinner";
        if(result.Status=="Ready" && winner is null) throw new InvalidOperationException("IntegrityConflict");
        if(winner is not null)
            await resultConnection.Execute("UPDATE setup_challenges SET sealed_password=NULL WHERE registration_id=@id",("id",registration));
        return new(result.RegistrationId,result.Status,result.OwnershipCommittedAt,result.ReadyAt,outcome);
    }
}
