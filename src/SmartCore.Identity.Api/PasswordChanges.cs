using System.Text.Json;
using Npgsql;

namespace SmartCore.Identity;

public sealed record ChangePasswordRequest(Guid OperationId,string CurrentPassword,string NewPassword);
public sealed record ChangeStatus(Guid OperationId,string Stage);
public sealed record RecoveryRequest(string Action);

// This adapter owns only Credential writes. Never call it from a live Session transaction.
public sealed class CredentialChanges(Database db,Secrets secrets,TimeProvider clock)
{
    public async Task<Row?> Outcome(Guid operation)
    {
        await using var c=await db.Source.OpenConnectionAsync();
        return await c.One("SELECT * FROM credential_change_receipts WHERE operation_id=@id",("id",operation));
    }
    public static bool Matches(Row receipt,Row intent)=>receipt.Get<Guid>("operation_id")==intent.Get<Guid>("credential_operation_id")
        && receipt.Get<Guid>("person_id")==intent.Get<Guid>("person_id")
        && receipt.Get<Guid>("credential_id")==intent.Get<Guid>("expected_credential_id")
        && Secrets.Equal(receipt.Get<byte[]>("request_mac"),intent.Get<byte[]>("request_mac"))
        && Secrets.Equal(receipt.Get<byte[]>("expected_hash_mac"),intent.Get<byte[]>("expected_hash_mac"))
        && Secrets.Equal(receipt.Get<byte[]>("replacement_hash_mac"),intent.Get<byte[]>("replacement_hash_mac"));

    public async Task<Row> Apply(Row intent,bool rejectWithoutApplying=false)
    {
        await using var c=await db.Source.OpenConnectionAsync();
        // Immutable Ready acknowledgment is checked without Session locks.
        var guard=await c.One("""
            SELECT w.registration_id,w.credential_id,w.phase,r.status FROM initial_credential_winners w
            JOIN registrations r ON r.id=w.registration_id WHERE w.person_id=@person
            """,("person",intent.Get<Guid>("person_id"))) ?? throw new InvalidOperationException("IntegrityConflict");
        if(guard.Get<string>("phase")!="ReadyAcknowledged" || guard.Get<string>("status")!="Ready"
            || guard.Get<Guid>("credential_id")!=intent.Get<Guid>("expected_credential_id")) throw new InvalidOperationException("IntegrityConflict");
        await using var tx=await c.BeginTransactionAsync();
        // Same namespace/key as initial provisioning. No auth-person:* lock is held.
        await c.Execute("SELECT pg_advisory_xact_lock(hashtextextended(@id,0))",("id","credential:"+guard.Get<Guid>("registration_id")));
        var operation=intent.Get<Guid>("credential_operation_id");
        var prior=await c.One("SELECT * FROM credential_change_receipts WHERE operation_id=@id",("id",operation));
        if(prior is not null)
        {
            if(!Matches(prior,intent)) throw new InvalidOperationException("IntegrityConflict");
            return prior; // Receipt survives later password changes; do not compare current hash on replay.
        }
        var credential=await c.One("SELECT * FROM credentials WHERE id=@id FOR UPDATE",("id",intent.Get<Guid>("expected_credential_id")))
            ?? throw new InvalidOperationException("IntegrityConflict");
        if(credential.Get<Guid>("person_id")!=intent.Get<Guid>("person_id") || credential.Get<string>("status")!="Active"
            || !Secrets.Equal(intent.Get<byte[]>("expected_hash_mac"),secrets.Mac("credential-evidence",credential.Get<string>("password_hash"))))
            throw new InvalidOperationException("IntegrityConflict");
        var now=Timestamps.Now(clock);
        if(!rejectWithoutApplying)
        {
            if(!intent.Has("sealed_hash") || intent.Time("expires_at")<=now) throw new InvalidOperationException("DefinitivelyRejected");
            var hash=secrets.Open(intent.Get<byte[]>("sealed_hash"),"password-change:"+intent.Get<Guid>("id"));
            if(!Secrets.Equal(intent.Get<byte[]>("replacement_hash_mac"),secrets.Mac("credential-evidence",hash)))
                throw new InvalidOperationException("IntegrityConflict");
            await c.Execute("UPDATE credentials SET password_hash=@hash WHERE id=@id",("hash",hash),("id",credential.Get<Guid>("id")));
        }
        var outcome=rejectWithoutApplying?"RejectedNotApplied":"Applied";
        await c.Execute("""
            INSERT INTO credential_change_receipts VALUES(@operation,@person,@credential,@request,@expected,@replacement,@outcome,@now)
            """,("operation",operation),("person",intent.Get<Guid>("person_id")),("credential",credential.Get<Guid>("id")),
            ("request",intent.Get<byte[]>("request_mac")),("expected",intent.Get<byte[]>("expected_hash_mac")),
            ("replacement",intent.Get<byte[]>("replacement_hash_mac")),("outcome",outcome),("now",now));
        if(!rejectWithoutApplying)
        {
            var eventId=Guid.NewGuid();
            var envelope=new {EventId=eventId,EventType="PasswordChanged",AggregateType="Credential",AggregateId=credential.Get<Guid>("id"),
                OccurredAt=now,ActorIdentity=intent.Get<Guid>("person_id"),SessionReference=intent.Get<Guid>("session_id"),
                ExecutionContext=new {CorrelationId=intent.Get<Guid>("id").ToString()},
                Payload=new {PersonId=intent.Get<Guid>("person_id"),CredentialId=credential.Get<Guid>("id")}};
            await c.Execute("INSERT INTO credential_change_outbox(id,operation_id,payload,occurred_at) VALUES(@id,@op,CAST(@payload AS jsonb),@now)",
                ("id",eventId),("op",operation),("payload",JsonSerializer.Serialize(envelope)),("now",now));
        }
        await tx.CommitAsync();
        return (await c.One("SELECT * FROM credential_change_receipts WHERE operation_id=@id",("id",operation)))!;
    }
}

// Session-owned admission/fencing and reconciliation, separated from Credential commits.
public interface IPasswordChangeRunner {Task Tick();}
public sealed class PasswordChanges(Database db,Secrets secrets,TimeProvider clock,IAuthenticationIssuanceGate gate,
    AccessTokens access,CredentialChanges credentials) : IPasswordChangeRunner
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
    private static Task<Row?> Load(NpgsqlConnection c,Guid id)=>c.One("""
        SELECT i.*,m.session_id,m.client_id,m.expected_hash_mac,m.replacement_hash_mac,m.sealed_hash,m.expires_at,m.sealed_replay_hash,m.replay_until
        FROM auth_credential_change_intents i JOIN auth_change_material m ON m.intent_id=i.id WHERE i.id=@id
        """,("id",id));
    public async Task<ChangeStatus> Status(Guid id)
    {
        await using var c=await db.Source.OpenConnectionAsync();
        var row=await Load(c,id) ?? throw new ApiError(404,"NOT_FOUND");
        return new(id,row.Get<string>("stage"));
    }
    public async Task<ChangeStatus> Accept(string bearer,string client,ChangePasswordRequest request)
    {
        // Policy and all KDF work precede fencing. No caller-supplied verified=true or epoch.
        Input.Require(request.OperationId!=Guid.Empty && request.CurrentPassword is {Length:>=1 and <=128}
            && request.NewPassword is {Length:>=15 and <=128});
        var proof=access.Read(bearer,client);
        // Metadata only: never create a fast offline oracle over either raw password.
        var requestMac=secrets.Mac("password-change-request-v2",proof.PersonId.ToString(),proof.SessionId.ToString(),client,request.OperationId.ToString());
        await using var c=await db.Source.OpenConnectionAsync();
        var prior=await Load(c,request.OperationId);
        if(prior is not null)
        {
            return await Replay(prior,proof,request,requestMac,bearer);
        }
        var evidence=await Evidence(c,proof.PersonId) ?? throw Denied();
        if(!Ready(evidence)) throw Denied();
        var failures=new PasswordFailures(db,clock);
        if(!await failures.Allowed(proof.PersonId)) throw Denied();
        var verified=await PasswordWork.Verify(request.CurrentPassword,evidence.Get<string>("password_hash"));
        await failures.Record(proof.PersonId,verified);
        if(!verified) throw Denied();
        var replacement=await PasswordWork.Hash(request.NewPassword);
        await using var tx=await c.BeginTransactionAsync();
        var state=await gate.Acquire(tx,proof.PersonId);
        // A concurrent identical admission must replay instead of advancing epoch twice.
        prior=await Load(c,request.OperationId);
        if(prior is not null)
        {
            // Release all Session locks before replay's Argon2 comparison.
            await tx.RollbackAsync();
            return await Replay(prior,proof,request,requestMac,bearer);
        }
        // KDF or lock wait can cross access expiry; admission requires a currently valid proof.
        _=access.Read(bearer,client);
        var current=await Evidence(c,proof.PersonId); var now=Timestamps.Now(clock);
        var session=await c.One("SELECT * FROM auth_sessions WHERE id=@id FOR UPDATE",("id",proof.SessionId));
        var family=await c.One("SELECT status FROM auth_refresh_families WHERE session_id=@id",("id",proof.SessionId));
        if(!state.AllowsSensitiveOperation(proof.Epoch) || current is null || !Ready(current)
            || current.Get<Guid>("id")!=evidence.Get<Guid>("id") || current.Get<string>("password_hash")!=evidence.Get<string>("password_hash")
            || session is null || session.Get<Guid>("person_id")!=proof.PersonId || session.Get<string>("client_id")!=client
            || session.Get<long>("issuance_epoch")!=proof.Epoch || session.Get<string>("status")!="Active"
            || session.Time("expires_at")<=now || session.Time("last_foreground_refresh_at").AddMinutes(30)<=now
            || family?.Get<string>("status")!="Active" || state.Epoch==long.MaxValue) throw Denied();
        var operation=Guid.NewGuid();
        await c.Execute("""
            INSERT INTO auth_credential_change_intents(id,person_id,kind,request_mac,verified_at,created_at,target_epoch,
              credential_operation_id,expected_credential_id,protected_material_reference,stage,next_attempt_at,request_version)
            VALUES(@id,@person,'ChangePassword',@mac,@now,@now,@epoch,@op,@credential,@reference,'Fenced',@now,2)
            """,("id",request.OperationId),("person",proof.PersonId),("mac",requestMac),("now",now),("epoch",state.Epoch+1),
            ("op",operation),("credential",current.Get<Guid>("id")),("reference","auth-change-material:"+request.OperationId));
        await c.Execute("INSERT INTO auth_change_material(intent_id,session_id,client_id,expected_hash_mac,replacement_hash_mac,sealed_hash,expires_at,sealed_replay_hash,replay_until) VALUES(@id,@session,@client,@expected,@replacement,@sealed,@expiry,@hash,@replay)",
            ("id",request.OperationId),("session",proof.SessionId),("client",client),
            ("expected",secrets.Mac("credential-evidence",current.Get<string>("password_hash"))),
            ("replacement",secrets.Mac("credential-evidence",replacement)),
            ("sealed",secrets.Seal(replacement,"password-change:"+request.OperationId)),("expiry",now.AddHours(24)),("hash",secrets.Seal(replacement,"password-change-replay:"+request.OperationId)),("replay",now.AddSeconds(900)));
        await c.Execute("UPDATE auth_issuance_state SET epoch=epoch+1,pending_operation_id=@op WHERE person_id=@person",("op",request.OperationId),("person",proof.PersonId));
        // Gate serializes all Session writers. Lock Session IDs in stable order before families.
        await c.Rows("SELECT id FROM auth_sessions WHERE person_id=@person AND status='Active' ORDER BY id FOR UPDATE",("person",proof.PersonId));
        await c.Execute("UPDATE auth_sessions SET status='Closed',closed_at=@now,close_reason='PasswordChanged' WHERE person_id=@person AND status='Active'",("now",now),("person",proof.PersonId));
        await c.Execute("""
            UPDATE auth_refresh_families SET status='Revoked',revoked_at=@now
            WHERE session_id IN (SELECT id FROM auth_sessions WHERE person_id=@person) AND status='Active'
            """,("now",now),("person",proof.PersonId));
        await tx.CommitAsync();
        return new(request.OperationId,"Fenced");
    }
    private async Task<ChangeStatus> Replay(Row prior,AccessProof proof,ChangePasswordRequest request,byte[] requestMac,string bearer)
    {
        if(prior.Get<short>("request_version")!=2 || prior.Get<Guid>("person_id")!=proof.PersonId || prior.Get<Guid>("session_id")!=proof.SessionId
            || prior.Get<string>("client_id")!=proof.ClientId || !Secrets.Equal(prior.Get<byte[]>("request_mac"),requestMac)) throw new ApiError(409,"IDEMPOTENCY_CONFLICT");
        if(!prior.Has("sealed_replay_hash") || prior.Time("replay_until")<=Timestamps.Now(clock)) throw Denied();
        var failures=new PasswordFailures(db,clock);
        if(!await failures.Allowed(proof.PersonId)) throw Denied();
        var verified=await PasswordWork.Verify(request.NewPassword,secrets.Open(prior.Get<byte[]>("sealed_replay_hash"),"password-change-replay:"+request.OperationId));
        await failures.Record(proof.PersonId,verified);
        if(!verified) throw new ApiError(409,"IDEMPOTENCY_CONFLICT");
        _=access.Read(bearer,proof.ClientId);
        if(prior.Time("replay_until")<=Timestamps.Now(clock)) throw Denied();
        return new(request.OperationId,prior.Get<string>("stage"));
    }
    public async Task Process(Guid id)
    {
        Row intent;
        await using(var c=await db.Source.OpenConnectionAsync())
        {
            intent=await Load(c,id) ?? throw new InvalidOperationException("IntegrityConflict");
            if(intent.Get<string>("stage") is "Reconciled" or "FailedClosed") return;
            await using var tx=await c.BeginTransactionAsync();
            var state=await gate.Acquire(tx,intent.Get<Guid>("person_id"));
            if(state.PendingOperationId!=id || state.Epoch!=intent.Get<long>("target_epoch")) throw new InvalidOperationException("IntegrityConflict");
            // Must not reopen a FailedClosed intent selected by a stale worker.
            if(await c.Execute("UPDATE auth_credential_change_intents SET stage='AwaitingCredential' WHERE id=@id AND stage IN ('Fenced','AwaitingCredential')",("id",id))==0) return;
            await tx.CommitAsync();
        }
        // Separate transaction owner, AFTER Session transaction disposal.
        var receipt=await credentials.Apply(intent);
        await Reconcile(intent,receipt);
    }
    private async Task Reconcile(Row intent,Row receipt,string? operatorAction=null)
    {
        if(!CredentialChanges.Matches(receipt,intent)) throw new InvalidOperationException("IntegrityConflict");
        await using var c=await db.Source.OpenConnectionAsync(); await using var tx=await c.BeginTransactionAsync();
        var state=await gate.Acquire(tx,intent.Get<Guid>("person_id"));
        var id=intent.Get<Guid>("id"); var current=await Load(c,id);
        if(current?.Get<string>("stage")=="Reconciled")
        {
            if(operatorAction is not null) {await Audit(c,id,operatorAction,receipt.Get<string>("outcome"));await tx.CommitAsync();}
            return;
        }
        if(state.PendingOperationId!=id || state.Epoch!=intent.Get<long>("target_epoch")) throw new InvalidOperationException("IntegrityConflict");
        await c.Execute("UPDATE auth_credential_change_intents SET stage='Reconciled',resolved_at=@now,last_classification=NULL WHERE id=@id",("now",Timestamps.Now(clock)),("id",id));
        await c.Execute("UPDATE auth_issuance_state SET pending_operation_id=NULL WHERE person_id=@person",("person",intent.Get<Guid>("person_id")));
        await c.Execute("UPDATE auth_change_material SET sealed_hash=NULL WHERE intent_id=@id",("id",id));
        if(operatorAction is not null) await Audit(c,id,operatorAction,receipt.Get<string>("outcome"));
        await tx.CommitAsync();
    }
    private Task<int> Audit(NpgsqlConnection c,Guid id,string action,string outcome)=>c.Execute("INSERT INTO auth_change_operator_audit VALUES(@audit,@intent,@action,@now,@outcome)",
        ("audit",Guid.NewGuid()),("intent",id),("action",action),("now",Timestamps.Now(clock)),("outcome",outcome));
    // Called only by a separately authenticated, loopback-only development operator adapter.
    // ResolveNotApplied obtains a Credential lock and durable terminal receipt. It is never a force unlock.
    public async Task<ChangeStatus> Recover(Guid id,string action)
    {
        Input.Require(action is "Retry" or "ResolveNotApplied");
        Row intent;
        await using(var c=await db.Source.OpenConnectionAsync()) intent=await Load(c,id) ?? throw new ApiError(404,"NOT_FOUND");
        if(intent.Get<string>("stage")=="Reconciled")
        {
            var outcome=await credentials.Outcome(intent.Get<Guid>("credential_operation_id")) ?? throw new InvalidOperationException("IntegrityConflict");
            await Reconcile(intent,outcome,action);return new(id,"Reconciled");
        }
        if(action=="ResolveNotApplied")
        {
            var receipt=await credentials.Apply(intent,rejectWithoutApplying:true);
            // Stale workers see this receipt and cannot subsequently replace the password.
            await Reconcile(intent,receipt,action);
        }
        else
        {
            await using var c=await db.Source.OpenConnectionAsync(); await using var tx=await c.BeginTransactionAsync();
            var state=await gate.Acquire(tx,intent.Get<Guid>("person_id"));
            if(state.PendingOperationId!=id || state.Epoch!=intent.Get<long>("target_epoch")) throw new ApiError(409,"RECOVERY_CONFLICT");
            await c.Execute("UPDATE auth_credential_change_intents SET stage='AwaitingCredential',resolved_at=NULL,attempts=0,next_attempt_at=@now,last_classification=NULL WHERE id=@id AND stage<>'Reconciled'",("now",Timestamps.Now(clock)),("id",id));
            await Audit(c,id,action,"Requeued");
            await tx.CommitAsync();
        }
        return await Status(id);
    }
    public async Task Tick()
    {
        await using var c=await db.Source.OpenConnectionAsync();
        var jobs=await c.Rows("SELECT id FROM auth_credential_change_intents WHERE stage IN ('Fenced','AwaitingCredential') AND next_attempt_at<=@now ORDER BY next_attempt_at LIMIT 20",("now",Timestamps.Now(clock)));
        foreach(var job in jobs)
        {
            var id=job.Get<Guid>("id");
            // Atomic persistent claim; an expired claim resumes the same operation after a process crash.
            var claim=await c.One("""
                UPDATE auth_credential_change_intents SET attempts=attempts+1,next_attempt_at=@now+interval '60 seconds'
                WHERE id=@id AND stage IN ('Fenced','AwaitingCredential') AND attempts<8 AND next_attempt_at<=@now RETURNING attempts
                """,("now",Timestamps.Now(clock)),("id",id));
            if(claim is null) continue;
            try {await Process(id);}
            catch(Exception error) when(error is not OperationCanceledException)
            {
                var classification=error is InvalidOperationException && error.Message is "IntegrityConflict" or "DefinitivelyRejected"?error.Message:"OutcomeUnknown";
                await c.Execute("""
                    UPDATE auth_credential_change_intents SET last_classification=@error,
                      stage=CASE WHEN @terminal OR attempts>=8 THEN 'FailedClosed' ELSE stage END,
                      resolved_at=CASE WHEN @terminal OR attempts>=8 THEN @now ELSE NULL END
                    WHERE id=@id AND stage IN ('Fenced','AwaitingCredential')
                    """,("error",classification),("terminal",classification is "IntegrityConflict" or "DefinitivelyRejected"),("now",Timestamps.Now(clock)),("id",id));
            }
        }
        await c.Execute("""
            UPDATE auth_credential_change_intents SET stage='FailedClosed',resolved_at=@now,last_classification='OutcomeUnknown'
            WHERE stage IN ('Fenced','AwaitingCredential') AND attempts>=8 AND next_attempt_at<=@now
            """,("now",Timestamps.Now(clock)));
        await c.Execute("UPDATE auth_reset_delivery SET sealed_code=NULL WHERE expires_at<=@now AND sealed_code IS NOT NULL",("now",Timestamps.Now(clock)));
        await c.Execute("UPDATE auth_change_material SET sealed_hash=NULL WHERE expires_at<=@now AND sealed_hash IS NOT NULL",("now",Timestamps.Now(clock)));
        await c.Execute("UPDATE auth_change_material SET sealed_replay_hash=NULL WHERE replay_until<=@now AND sealed_replay_hash IS NOT NULL",("now",Timestamps.Now(clock)));
    }
}

public sealed class PasswordChangeWorker(IPasswordChangeRunner changes,ILogger<PasswordChangeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try {await changes.Tick();}
            catch(Exception error) when(error is not OperationCanceledException)
            {logger.LogWarning("Password reconciliation OutcomeUnknown ({ErrorType})",error.GetType().Name);}
            await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken);
        }
    }
}
