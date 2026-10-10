using Npgsql;
using SmartCore.Identity;

internal static class AuthenticationStorageChecks
{
    public static async Task<int> Run(Database db,TimeProvider clock,Guid person)
    {
        var checks=0;
        void Check(bool ok,string label)
        {
            if(!ok) throw new Exception("FAIL: "+label);
            Console.WriteLine("PASS: "+label); checks++;
        }
        async Task Rejected(string statement,string state,params (string,object?)[] args)
        {
            await using var c=await db.Source.OpenConnectionAsync();
            await using var tx=await c.BeginTransactionAsync();
            try {await c.Execute(statement,args);}
            catch(PostgresException e) when(e.SqlState==state) {return;}
            throw new Exception("Expected storage constraint "+state);
        }
        IAuthenticationIssuanceGate gate=new PostgresAuthenticationIssuanceGate();
        await db.Migrate();
        await using var sql=await db.Source.OpenConnectionAsync();
        Check((await sql.One("SELECT count(*) AS n FROM schema_versions WHERE version=3"))!.Get<long>("n")==1,"authentication storage migration replays once");
        await using(var c=await db.Source.OpenConnectionAsync())
        await using(var tx=await c.BeginTransactionAsync())
        {
            var state=await gate.Acquire(tx,person);
            Check(state.Epoch==0 && state.AllowsIssuance && state.AllowsSensitiveOperation(0),"Person gate initialization creates no active mutation fence");
            // Dispose without commit: initialization and lock belong to caller's Session transaction.
        }
        Check(await sql.One("SELECT person_id FROM auth_issuance_state WHERE person_id=@person",("person",person)) is null,
            "gate initialization rolls back with the Session transaction");
        await using(var c=await db.Source.OpenConnectionAsync())
        await using(var tx=await c.BeginTransactionAsync())
        {
            await gate.Acquire(tx,person);
            await tx.CommitAsync();
        }
        var now=Timestamps.Now(clock); var expiry=now.AddSeconds(86400);
        var session=Guid.NewGuid(); var family=Guid.NewGuid(); var operation=Guid.NewGuid();
        await sql.Execute("INSERT INTO auth_sessions VALUES(@id,@person,0,'test-bff',@now,@expiry,@now,'Active',NULL,NULL)",
            ("id",session),("person",person),("now",now),("expiry",expiry));
        await sql.Execute("INSERT INTO auth_refresh_families VALUES(@id,@session,@expiry,0,'Active',NULL)",("id",family),("session",session),("expiry",expiry));
        var verifier=new byte[32]; System.Security.Cryptography.RandomNumberGenerator.Fill(verifier);
        await sql.Execute("INSERT INTO auth_refresh_generations VALUES(@family,0,@verifier,'test-key','Current',@now,NULL,@expiry)",
            ("family",family),("verifier",verifier),("now",now),("expiry",expiry));
        await Rejected("INSERT INTO auth_refresh_generations VALUES(@family,1,@verifier,'test-key','Current',@now,NULL,@expiry)","23505",
            ("family",family),("verifier",verifier),("now",now),("expiry",expiry));
        Check(true,"storage rejects two current refresh generations in one family");
        var secondSession=Guid.NewGuid();
        await sql.Execute("INSERT INTO auth_sessions VALUES(@id,@person,0,'test-bff',@now,@expiry,@now,'Active',NULL,NULL)",
            ("id",secondSession),("person",person),("now",now),("expiry",expiry));
        await Rejected("INSERT INTO auth_refresh_families VALUES(@id,@session,@expiry,0,'Active',NULL)","23503",
            ("id",Guid.NewGuid()),("session",secondSession),("expiry",expiry.AddSeconds(1)));
        Check(true,"family deadline cannot diverge from its Session");
        await Rejected("""
            INSERT INTO auth_refresh_security_events VALUES(@id,@session,@family,0,@now,'RefreshTokenReuseDetected','PreviouslyConsumedGeneration')
            ""","23503",("id",Guid.NewGuid()),("session",secondSession),("family",family),("now",now));
        Check(true,"internal refresh event cannot attribute a family to another Session");
        await Rejected("UPDATE auth_sessions SET expires_at=expires_at+interval '1 second' WHERE id=@id","23514",("id",session));
        Check(true,"Session absolute deadline cannot be extended");
        await using(var c=await db.Source.OpenConnectionAsync())
        await using(var tx=await c.BeginTransactionAsync())
        {
            await gate.Acquire(tx,person);
            await c.Execute("UPDATE auth_refresh_generations SET status='Consumed',consumed_at=@now WHERE family_id=@id AND generation=0",("id",family),("now",now));
            await c.Execute("INSERT INTO auth_refresh_generations VALUES(@family,1,@verifier,'test-key','Current',@now,NULL,@expiry)",
                ("family",family),("verifier",verifier),("now",now),("expiry",expiry));
            await c.Execute("UPDATE auth_refresh_families SET current_generation=1 WHERE id=@id",("id",family));
            await tx.CommitAsync();
        }
        await Rejected("UPDATE auth_refresh_generations SET status='Current',consumed_at=NULL WHERE family_id=@id AND generation=0","23514",("id",family));
        Check((await sql.One("SELECT recognition_until FROM auth_refresh_generations WHERE family_id=@id AND generation=0",("id",family)))!.Time("recognition_until")==expiry,
            "spent generation remains recognizable through fixed deadline and cannot become current");
        await Rejected("UPDATE auth_refresh_families SET current_generation=0 WHERE id=@id","23514",("id",family));
        Check(true,"family generation cannot move backward");
        await Rejected("UPDATE auth_refresh_generations SET recognition_until=@expiry WHERE family_id=@id AND generation=0","23514",
            ("id",family),("expiry",expiry.AddSeconds(-1)));
        Check(true,"recognition horizon is immutable and is not a replay grace window");
        // Internal fixture simulates already verified admission. No external proof bypass is exposed.
        await using(var c=await db.Source.OpenConnectionAsync())
        await using(var tx=await c.BeginTransactionAsync())
        {
            await gate.Acquire(tx,person);
            await c.Execute("""
                INSERT INTO auth_credential_change_intents(id,person_id,kind,request_mac,verified_at,created_at,
                  target_epoch,credential_operation_id,expected_credential_id,protected_material_reference,stage,next_attempt_at)
                VALUES(@id,@person,'ChangePassword',@mac,@now,@now,1,@credential_operation,@expected,'test-protected-reference','Fenced',@now)
                """,("id",operation),("person",person),("mac",verifier),("now",now),("credential_operation",Guid.NewGuid()),("expected",Guid.NewGuid()));
            await c.Execute("UPDATE auth_issuance_state SET epoch=1,pending_operation_id=@id WHERE person_id=@person",("id",operation),("person",person));
            await c.Execute("UPDATE auth_sessions SET status='Closed',closed_at=@now,close_reason='PasswordChanged' WHERE person_id=@person AND status='Active'",("now",now),("person",person));
            await c.Execute("UPDATE auth_refresh_families SET status='Revoked',revoked_at=@now WHERE session_id=@id",("now",now),("id",session));
            await tx.CommitAsync();
        }
        await using(var c=await db.Source.OpenConnectionAsync())
        await using(var tx=await c.BeginTransactionAsync())
        {
            var blocked=await gate.Acquire(tx,person);
            Check(blocked.Epoch==1 && blocked.PendingOperationId==operation && !blocked.AllowsIssuance
                && !blocked.AllowsSensitiveOperation(0) && !blocked.AllowsSensitiveOperation(1),
                "committed mutation fence denies issuance and sensitive operations at every epoch");
        }
        Check((await sql.One("SELECT stage,next_attempt_at,attempts FROM auth_credential_change_intents WHERE id=@id",("id",operation)))!.Get<string>("stage")=="Fenced",
            "unresolved fence retains durable operation and scheduled recovery metadata");
        await Rejected("UPDATE auth_issuance_state SET pending_operation_id=NULL WHERE person_id=@person","23514",("person",person));
        Check(true,"unreconciled Credential result cannot release issuance fence");
        await Rejected("UPDATE auth_sessions SET status='Active',closed_at=NULL,close_reason=NULL WHERE id=@id","23514",("id",session));
        await Rejected("UPDATE auth_refresh_families SET status='Active',revoked_at=NULL WHERE id=@id","23514",("id",family));
        Check(true,"closed Session and revoked family cannot be resurrected");
        await using(var c=await db.Source.OpenConnectionAsync())
        await using(var tx=await c.BeginTransactionAsync())
        {
            await gate.Acquire(tx,person);
            await c.Execute("UPDATE auth_credential_change_intents SET stage='Reconciled',resolved_at=@now WHERE id=@id",("now",now),("id",operation));
            await c.Execute("UPDATE auth_issuance_state SET pending_operation_id=NULL WHERE person_id=@person",("person",person));
            await tx.CommitAsync();
        }
        await using(var c=await db.Source.OpenConnectionAsync())
        await using(var tx=await c.BeginTransactionAsync())
        {
            var released=await gate.Acquire(tx,person);
            Check(released.AllowsIssuance && released.AllowsSensitiveOperation(1) && !released.AllowsSensitiveOperation(0),
                "reconciled fence admits new epoch while old authenticated epoch remains invalid");
        }
        await Rejected("UPDATE auth_issuance_state SET epoch=0 WHERE person_id=@person","23514",("person",person));
        Check(true,"issuance epoch cannot be lowered after reconciliation");
        // Internal verified fixtures: each writer advances and resolves its own operation.
        // Without serialization both can choose the same target epoch and collide.
        async Task<long> Advance()
        {
            await using var c=await db.Source.OpenConnectionAsync();
            await using var tx=await c.BeginTransactionAsync();
            var state=await gate.Acquire(tx,person);
            if(!state.AllowsIssuance) throw new Exception("Unexpected fence");
            var next=state.Epoch+1; var id=Guid.NewGuid();
            await c.Execute("""
                INSERT INTO auth_credential_change_intents(id,person_id,kind,request_mac,verified_at,created_at,
                  target_epoch,credential_operation_id,expected_credential_id,protected_material_reference,stage,next_attempt_at)
                VALUES(@id,@person,'ChangePassword',@mac,@now,@now,@epoch,@credential_operation,@expected,'test-protected-reference','Fenced',@now)
                """,("id",id),("person",person),("mac",verifier),("now",now),("epoch",next),("credential_operation",Guid.NewGuid()),("expected",Guid.NewGuid()));
            await c.Execute("UPDATE auth_issuance_state SET epoch=@epoch,pending_operation_id=@id WHERE person_id=@person",("epoch",next),("id",id),("person",person));
            await c.Execute("UPDATE auth_credential_change_intents SET stage='Reconciled',resolved_at=@now WHERE id=@id",("now",now),("id",id));
            await c.Execute("UPDATE auth_issuance_state SET pending_operation_id=NULL WHERE person_id=@person",("person",person));
            await tx.CommitAsync();
            return next;
        }
        Check((await Task.WhenAll(Advance(),Advance())).Order().SequenceEqual(new long[]{2,3}),
            "concurrent gate writers serialize distinct epoch advances without a lost update");
        return checks;
    }
}
