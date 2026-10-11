using Npgsql;

namespace SmartCore.Identity;

// Internal in-process adapter. It deliberately preserves three durable transaction boundaries.
// No public endpoint can invoke provisioning or acknowledgment.
public sealed class Provisioning(Database db, Secrets secrets, TimeProvider clock)
{
    public async Task EnsureCredential(Guid id,Guid? candidateOperation=null)
    {
        await using var connection=await db.Source.OpenConnectionAsync();
        await using var tx=await connection.BeginTransactionAsync();
        // No auth-person:* acquisition while this Credential transaction is alive (or inverse).
        await connection.Execute("SELECT pg_advisory_xact_lock(hashtextextended(@id,0))",("id","credential:"+id));
        var winner=await connection.One("SELECT * FROM initial_credential_winners WHERE registration_id=@id",("id",id));
        if(winner is not null) return;
        var registration=await connection.One("SELECT * FROM registrations WHERE id=@id FOR UPDATE",("id",id)) ?? throw new InvalidOperationException("IntegrityConflict");
        var now=Timestamps.Now(clock);
        var candidate=candidateOperation is { } selected
            ? await connection.One("SELECT * FROM setup_challenges WHERE registration_id=@id AND operation_id=@operation AND accepted_at IS NOT NULL FOR UPDATE",("id",id),("operation",selected))
            : await connection.One("SELECT * FROM setup_challenges WHERE registration_id=@id AND accepted_at IS NOT NULL AND sealed_password IS NOT NULL AND material_expires_at>@now ORDER BY accepted_at,operation_id LIMIT 1 FOR UPDATE",("id",id),("now",now));
        string hash; Guid operation;
        if(candidate is not null && candidate.Has("sealed_password") && candidate.Time("material_expires_at")>now)
        {
            operation=candidate.Get<Guid>("operation_id");
            hash=secrets.Open(candidate.Get<byte[]>("sealed_password"),"setup-candidate:"+operation);
        }
        else
        {
            if(candidateOperation is not null || registration.Time("material_expires_at")<=now || !registration.Has("sealed_password"))
                throw new InvalidOperationException("RequiresUserSetup");
            operation=id;
            hash=secrets.Open(registration.Get<byte[]>("sealed_password"),"registration:"+id);
        }
        var credential=Guid.NewGuid();
        await connection.Execute("INSERT INTO credentials VALUES(@id,@person,@hash,'Active')",("id",credential),("person",registration.Get<Guid>("person_id")),("hash",hash));
        await connection.Execute("""
            INSERT INTO initial_credential_winners(registration_id,person_id,credential_id,operation_id,provisioning_version,phase)
            VALUES(@id,@person,@credential,@operation,@version,'ProvisionedAwaitingReady')
            """,("id",id),("person",registration.Get<Guid>("person_id")),("credential",credential),("operation",operation),("version",Guid.NewGuid()));
        await connection.Execute("UPDATE setup_challenges SET sealed_password=NULL WHERE registration_id=@id",("id",id));
        await connection.Execute("UPDATE registrations SET sealed_password=NULL WHERE id=@id",("id",id));
        await tx.CommitAsync();
    }
    public async Task MarkReady(Guid id)
    {
        // Evidence is obtained outside Identity's Ready transaction. The permanent credential guard protects it.
        await using var connection=await db.Source.OpenConnectionAsync();
        var winner=await connection.One("""
            SELECT w.*,c.status FROM initial_credential_winners w JOIN credentials c ON c.id=w.credential_id
            WHERE w.registration_id=@id
            """,("id",id)) ?? throw new InvalidOperationException("IntegrityConflict");
        await using var tx=await connection.BeginTransactionAsync();
        var row=await connection.One("SELECT r.*,p.contact_kind,p.contact,p.display_name FROM registrations r JOIN persons p ON p.id=r.person_id WHERE r.id=@id FOR UPDATE OF r",("id",id))
            ?? throw new InvalidOperationException("IntegrityConflict");
        if(row.Get<string>("status")=="Ready") return;
        if(winner.Get<string>("phase")!="ProvisionedAwaitingReady" || winner.Get<string>("status")!="Active"
            || winner.Get<Guid>("person_id")!=row.Get<Guid>("person_id")) throw new InvalidOperationException("IntegrityConflict");
        var now=Timestamps.Now(clock); var fact=Guid.NewGuid();
        await connection.Execute("""
            UPDATE registrations SET status='Ready',ready_at=@now,ready_fact_id=@fact,winner_id=@winner,
              provisioning_version=@version,sealed_password=NULL WHERE id=@id
            """,("id",id),("now",now),("fact",fact),("winner",winner.Get<Guid>("credential_id")),("version",winner.Get<Guid>("provisioning_version")));
        var payload=new Dictionary<string,object> { ["PersonId"]=row.Get<Guid>("person_id"),["DisplayName"]=row.Get<string>("display_name"),
            ["OrganizationId"]=row.Get<Guid>("organization_id"),["MembershipId"]=row.Get<Guid>("membership_id"),["OwnershipCommittedAt"]=row.Time("ownership_committed_at") };
        if(row.Get<string>("contact_kind")=="email") payload["Email"]=row.Get<string>("contact");
        await RegistrationService.Event(connection,id,"PersonRegistered","Person",row.Get<Guid>("person_id"),now,payload,fact);
        // The durable incomplete workflow job is also the acknowledgment outbox; Ready never marks it completed.
        await tx.CommitAsync();
    }
    public async Task Acknowledge(Guid id)
    {
        await using var connection=await db.Source.OpenConnectionAsync();
        var ready=await connection.One("SELECT * FROM registrations WHERE id=@id",("id",id)) ?? throw new InvalidOperationException("IntegrityConflict");
        if(ready.Get<string>("status")!="Ready") throw new InvalidOperationException("IntegrityConflict");
        await using var tx=await connection.BeginTransactionAsync();
        var winner=await connection.One("SELECT * FROM initial_credential_winners WHERE registration_id=@id FOR UPDATE",("id",id)) ?? throw new InvalidOperationException("IntegrityConflict");
        if(winner.Get<Guid>("credential_id")!=ready.Get<Guid>("winner_id") || winner.Get<Guid>("person_id")!=ready.Get<Guid>("person_id")
            || winner.Get<Guid>("provisioning_version")!=ready.Get<Guid>("provisioning_version")) throw new InvalidOperationException("IntegrityConflict");
        // Matching historical acknowledgment wins before looking at any later credential state.
        if(winner.Has("acknowledged_fact_id"))
        {
            if(winner.Get<Guid>("acknowledged_fact_id")!=ready.Get<Guid>("ready_fact_id")) throw new InvalidOperationException("IntegrityConflict");
            return;
        }
        await connection.Execute("UPDATE initial_credential_winners SET phase='ReadyAcknowledged',acknowledged_fact_id=@fact WHERE registration_id=@id",("id",id),("fact",ready.Get<Guid>("ready_fact_id")));
        await tx.CommitAsync();
    }
    public async Task Tick()
    {
        await using var connection=await db.Source.OpenConnectionAsync();
        var jobs=await connection.Rows("SELECT registration_id FROM workflow_jobs WHERE NOT completed AND NOT recovery_needed AND next_attempt_at<=@now LIMIT 20",("now",Timestamps.Now(clock)));
        foreach(var job in jobs)
        {
            var id=job.Get<Guid>("registration_id");
            var locked=await connection.One("SELECT pg_try_advisory_lock(hashtextextended(@id,0)) AS acquired",("id","worker:"+id));
            if(!locked!.Get<bool>("acquired")) continue;
            try
            {
                var claimed=await connection.One("""
                    UPDATE workflow_jobs SET attempts=attempts+1,next_attempt_at=@now + (2 * power(2,attempts)) * interval '1 second'
                    WHERE registration_id=@id AND NOT completed AND NOT recovery_needed AND attempts<8 AND next_attempt_at<=@now RETURNING attempts
                    """,("id",id),("now",Timestamps.Now(clock)));
                if(claimed is null) continue;
                try
                {
                    await EnsureCredential(id);
                    await MarkReady(id);
                    await Acknowledge(id);
                    await connection.Execute("UPDATE workflow_jobs SET completed=true,last_error=NULL WHERE registration_id=@id",("id",id));
                }
                catch(Exception error) when(error is NpgsqlException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
                {
                    // Error classifications only: never persist provider text or secret-bearing SQL details.
                    var code=error is InvalidOperationException && error.Message is "RequiresUserSetup" or "IntegrityConflict" ? error.Message : "DependencyUnavailable";
                    await connection.Execute("UPDATE workflow_jobs SET last_error=@error,recovery_needed=(attempts>=8 OR @terminal) WHERE registration_id=@id",
                        ("id",id),("error",code),("terminal",code is "RequiresUserSetup" or "IntegrityConflict"));
                }
            }
            finally { await connection.Execute("SELECT pg_advisory_unlock(hashtextextended(@id,0))",("id","worker:"+id)); }
        }
        // A process crash after the eighth attempt must not leave an invisible, unclaimable job.
        await connection.Execute("UPDATE workflow_jobs SET recovery_needed=true WHERE attempts>=8 AND NOT completed AND next_attempt_at<=@now",("now",Timestamps.Now(clock)));
        await connection.Execute("UPDATE verification_sessions SET code_mac=NULL,binding_mac=NULL,request_mac=NULL,sealed_password=NULL WHERE expires_at<=@now",("now",Timestamps.Now(clock)));
        await connection.Execute("UPDATE registrations SET sealed_password=NULL WHERE material_expires_at<=@now",("now",Timestamps.Now(clock)));
        await connection.Execute("UPDATE delivery_outbox SET sealed_code=NULL WHERE expires_at<=@now",("now",Timestamps.Now(clock)));
        await connection.Execute("UPDATE setup_challenges SET code_mac=NULL,binding_mac=NULL,request_mac=NULL WHERE expires_at<=@now",("now",Timestamps.Now(clock)));
        await connection.Execute("UPDATE setup_challenges SET sealed_password=NULL WHERE material_expires_at<=@now OR EXISTS(SELECT 1 FROM initial_credential_winners w WHERE w.registration_id=setup_challenges.registration_id)",("now",Timestamps.Now(clock)));
        await connection.Execute("UPDATE setup_delivery_outbox SET sealed_code=NULL WHERE expires_at<=@now",("now",Timestamps.Now(clock)));
    }
}

public sealed class ProvisioningWorker(Provisioning provisioning, ILogger<ProvisioningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try { await provisioning.Tick(); }
            catch(Exception error) when(error is NpgsqlException or InvalidOperationException)
            { logger.LogWarning("Registration worker temporarily unavailable ({ErrorType})",error.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken);
        }
    }
}
