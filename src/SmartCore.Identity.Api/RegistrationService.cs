using System.Text.Json;
using Npgsql;

namespace SmartCore.Identity;

public sealed class RegistrationService(Database db, Secrets secrets, TimeProvider clock)
{
    public async Task<VerificationPending> Start(StartRegistration request, string key)
    {
        var (kind, contact) = Input.Validate(request, key);
        var now = Timestamps.Now(clock);
        var fingerprint = secrets.Mac("start", kind, contact, request.DisplayName.Trim(), request.Password, request.BindingSecret);
        await using var connection = await db.Source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.Execute("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", ("key", "start:" + key));
        var previous = await connection.One("SELECT * FROM verification_sessions WHERE idempotency_key=@key FOR UPDATE",("key",key));
        if (previous is not null)
        {
            Input.Require(previous.Time("expires_at") > now && !previous.Get<bool>("invalidated"), "VERIFICATION_FAILED", 400);
            Input.Require(Secrets.Equal(previous.Optional<byte[]>("request_mac"), fingerprint),"IDEMPOTENCY_CONFLICT",409);
            return new(previous.Get<string>("id"), previous.Time("expires_at"));
        }
        // Serialize contact admission across API instances; never look up Person before proof.
        await connection.Execute("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",("key","admission:" + contact));
        var count = await connection.One("SELECT count(*) AS n FROM verification_sessions WHERE contact=@contact AND created_at>@since",("contact",contact),("since",now.AddHours(-1)));
        Input.Require(count!.Get<long>("n") < 5,"RATE_LIMITED",429);
        var id = Secrets.Token();
        var code = Secrets.Code();
        var expiry = now.AddMinutes(10);
        var hash = await Secrets.HashPassword(request.Password);
        await connection.Execute("""
            INSERT INTO verification_sessions(id,idempotency_key,request_mac,binding_mac,code_mac,contact_kind,contact,
              display_name,sealed_password,expires_at,created_at,last_sent_at)
            VALUES(@id,@key,@request,@binding,@code,@kind,@contact,@name,@password,@expiry,@now,@now)
            """,("id",id),("key",key),("request",fingerprint),("binding",secrets.Mac("binding",id,request.BindingSecret)),
            ("code",secrets.Mac("otp",id,code)),("kind",kind),("contact",contact),("name",request.DisplayName.Trim()),
            ("password",secrets.Seal(hash,"verification:"+id)),("expiry",expiry),("now",now));
        await EnqueueCode(connection,id,code,expiry,now);
        await transaction.CommitAsync();
        return new(id, expiry);
    }

    public async Task Resend(ResendVerification request)
    {
        Input.Require(Input.Id(request.VerificationSessionId) && Input.Secret(request.BindingSecret));
        await using var connection = await db.Source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var row = await connection.One("SELECT * FROM verification_sessions WHERE id=@id FOR UPDATE",("id",request.VerificationSessionId));
        var now = Timestamps.Now(clock);
        // Generic Accepted on all ineligible cases. Never disclose account existence.
        if (row is null || row.Get<bool>("invalidated") || row.Has("registration_id") || row.Has("setup_registration_id") || row.Time("expires_at") <= now
            || row.Get<int>("attempts") >= 5 || row.Get<int>("resends") >= 3
            || row.Time("last_sent_at").AddSeconds(60) > now
            || !Secrets.Equal(row.Optional<byte[]>("binding_mac"),secrets.Mac("binding",request.VerificationSessionId,request.BindingSecret))) return;
        var code = Secrets.Code();
        await connection.Execute("UPDATE verification_sessions SET code_mac=@code,resends=resends+1,last_sent_at=@now WHERE id=@id",
            ("id",request.VerificationSessionId),("code",secrets.Mac("otp",request.VerificationSessionId,code)),("now",now));
        await connection.Execute("UPDATE delivery_outbox SET sealed_code=NULL WHERE verification_id=@id",("id",request.VerificationSessionId));
        await EnqueueCode(connection,request.VerificationSessionId,code,row.Time("expires_at"),now);
        await transaction.CommitAsync();
    }

    public async Task<(RegistrationResult Result, bool Created)> Verify(VerifyRegistration request)
    {
        Input.Validate(request);
        await using var connection = await db.Source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var row = await connection.One("SELECT * FROM verification_sessions WHERE id=@id FOR UPDATE",("id",request.VerificationSessionId));
        var now = Timestamps.Now(clock); // Evaluate expiry after acquiring the authoritative lock.
        Input.Require(row is not null && !row.Get<bool>("invalidated") && row.Time("expires_at") > now
            && row.Get<int>("attempts") < 5,"VERIFICATION_FAILED",400);
        var valid = Secrets.Equal(row!.Optional<byte[]>("binding_mac"),secrets.Mac("binding",request.VerificationSessionId,request.BindingSecret))
            & Secrets.Equal(row.Optional<byte[]>("code_mac"),secrets.Mac("otp",request.VerificationSessionId,request.Code));
        await connection.Execute("UPDATE verification_sessions SET attempts=attempts+1 WHERE id=@id",("id",request.VerificationSessionId));
        if (!valid)
        {
            await transaction.CommitAsync(); // Wrong proof consumes budget even though the HTTP operation fails.
            throw new ApiError(400,"VERIFICATION_FAILED");
        }
        if (row.Has("registration_id"))
        {
            var existing = await Result(connection,row.Get<Guid>("registration_id"));
            await transaction.CommitAsync();
            return (existing,false);
        }
        var contact = row.Get<string>("contact");
        await connection.Execute("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",("key","ownership:" + contact));
        now = Timestamps.Now(clock);
        Input.Require(row.Time("expires_at") > now,"VERIFICATION_FAILED",400);
        var person = await connection.One("SELECT p.id,r.id AS registration_id,r.status FROM persons p JOIN registrations r ON r.person_id=p.id WHERE p.contact=@contact",("contact",contact));
        if (person is not null)
        {
            // A newly verified attempt NEVER substitutes its password into an existing registration.
            // Keep bounded proof only for the distinct setup request; never transfer the losing password.
            await connection.Execute("UPDATE verification_sessions SET setup_registration_id=@target,sealed_password=NULL,request_mac=NULL WHERE id=@id",("target",person.Get<Guid>("registration_id")),("id",request.VerificationSessionId));
            await connection.Execute("UPDATE delivery_outbox SET sealed_code=NULL WHERE verification_id=@id",("id",request.VerificationSessionId));
            await transaction.CommitAsync();
            throw new ApiError(409,"CONTACT_UNAVAILABLE") {NextAction=person.Get<string>("status")=="PendingCredential"?"RequestSetup":"SignIn"};
        }
        var registrationId=Guid.NewGuid(); var personId=Guid.NewGuid(); var organizationId=Guid.NewGuid(); var membershipId=Guid.NewGuid();
        var name=row.Get<string>("display_name");
        // Re-encrypt with the new owner's AAD before the atomic reference transfer.
        var material = secrets.Seal(secrets.Open(row.Get<byte[]>("sealed_password"),"verification:"+request.VerificationSessionId),"registration:"+registrationId);
        await connection.Execute("INSERT INTO persons VALUES(@id,@kind,@contact,@name,'Active',@now)",
            ("id",personId),("kind",row.Get<string>("contact_kind")),("contact",contact),("name",name),("now",now));
        await connection.Execute("INSERT INTO organizations VALUES(@id,@name,'Personal','Active')",("id",organizationId),("name",name));
        await connection.Execute("INSERT INTO memberships VALUES(@id,@person,@organization,'Owner','Active')",("id",membershipId),("person",personId),("organization",organizationId));
        await connection.Execute("""
            INSERT INTO registrations(id,person_id,organization_id,membership_id,ownership_committed_at,sealed_password,material_expires_at)
            VALUES(@id,@person,@organization,@membership,@now,@material,@expiry)
            """,("id",registrationId),("person",personId),("organization",organizationId),("membership",membershipId),
            ("now",now),("material",material),("expiry",now.AddMinutes(15)));
        await connection.Execute("UPDATE verification_sessions SET registration_id=@registration,sealed_password=NULL WHERE id=@id",("registration",registrationId),("id",request.VerificationSessionId));
        await connection.Execute("UPDATE delivery_outbox SET sealed_code=NULL WHERE verification_id=@id",("id",request.VerificationSessionId));
        await connection.Execute("INSERT INTO workflow_jobs(registration_id,next_attempt_at) VALUES(@id,@now)",("id",registrationId),("now",now));
        await Event(connection,registrationId,"OrganizationCreated","Organization",organizationId,now,
            new { OrganizationId=organizationId,Name=name,Category="Personal",Status="Active" },actor:personId.ToString());
        await Event(connection,registrationId,"MembershipCreated","Membership",membershipId,now,
            new { MembershipId=membershipId,PersonId=personId,OrganizationId=organizationId,Role="Owner",Status="Active" },actor:personId.ToString());
        await transaction.CommitAsync();
        return (new(registrationId,"PendingCredential",now,null),true);
    }

    private async Task EnqueueCode(NpgsqlConnection connection,string verification,string code,DateTimeOffset expiry,DateTimeOffset now)
    {
        var id=Guid.NewGuid();
        await connection.Execute("INSERT INTO delivery_outbox(id,verification_id,sealed_code,created_at,expires_at) VALUES(@id,@verification,@code,@now,@expiry)",
            ("id",id),("verification",verification),("code",secrets.Seal(code,"delivery:"+id)),("now",now),("expiry",expiry));
    }
    public static async Task<RegistrationResult> Result(NpgsqlConnection connection,Guid id)
    {
        var row=await connection.One("SELECT * FROM registrations WHERE id=@id",("id",id)) ?? throw new InvalidOperationException("Missing registration");
        return new(id,row.Get<string>("status"),row.Time("ownership_committed_at"),row.Has("ready_at")?row.Time("ready_at"):null);
    }
    internal static async Task Event(NpgsqlConnection connection,Guid registration,string type,string aggregate,Guid aggregateId,DateTimeOffset now,object payload,Guid? eventId=null,string actor="System")
    {
        var id=eventId ?? Guid.NewGuid();
        var envelope=JsonSerializer.Serialize(new {EventId=id,EventType=type,AggregateType=aggregate,AggregateId=aggregateId,
            OccurredAt=now,ActorIdentity=actor,ExecutionContext=new {CorrelationId=registration.ToString()},Payload=payload});
        await connection.Execute("INSERT INTO event_outbox(id,registration_id,event_type,payload,occurred_at) VALUES(@id,@registration,@type,CAST(@payload AS jsonb),@now)",
            ("id",id),("registration",registration),("type",type),("payload",envelope),("now",now));
    }
}
