using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

namespace SmartCore.Identity;

public sealed record LoginRequest(string? Email,string? Mobile,string Password);
public sealed record RefreshRequest([property:JsonRequired] string RefreshToken,[property:JsonRequired] bool Foreground);
public sealed record SelfPerson(Guid PersonId,string DisplayName,string? Email,string? Mobile,string Status);
public sealed record LoginResult(SelfPerson Person,SessionTokens Session);

public sealed class AuthenticationService(Database db,Secrets secrets,TimeProvider clock,IAuthenticationIssuanceGate gate,AccessTokens access)
{
    private const string RefreshKey="development-v1";
    // One fixed dummy KDF encoding avoids skipping expensive work on unknown contacts.
    private const string Dummy="$argon2id$v=19$m=65536,t=3,p=4$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static ApiError Denied()=>new(401,"UNAUTHORIZED");
    private static SelfPerson Person(Row row)=>new(row.Get<Guid>("id"),row.Get<string>("display_name"),
        row.Get<string>("contact_kind")=="email"?row.Get<string>("contact"):null,
        row.Get<string>("contact_kind")=="mobile"?row.Get<string>("contact"):null,row.Get<string>("status"));
    private static Task<Row?> Evidence(NpgsqlConnection c,Guid person)=>c.One("""
        SELECT p.*,r.status AS registration_status,c.id AS credential_id,c.status AS credential_status,c.password_hash
        FROM persons p LEFT JOIN registrations r ON r.person_id=p.id LEFT JOIN credentials c ON c.person_id=p.id WHERE p.id=@id
        """,("id",person));
    private static string? Failure(Row? row)=>row is null ? "PersonNotFound" : row.Get<string>("status")!="Active" ? "PersonInactive"
        : !row.Has("registration_status") || row.Get<string>("registration_status")!="Ready" ? "RegistrationNotReady"
        : !row.Has("credential_id") || row.Get<string>("credential_status")!="Active" ? "CredentialMissing" : null;
    private byte[] Verifier(Guid family,long generation,string client,string secret)=>secrets.Mac("refresh-v1",family.ToString("N"),
        generation.ToString(CultureInfo.InvariantCulture),client,RefreshKey,secret);
    private static string RefreshToken(Guid family,long generation,string secret)=>$"{family:N}.{generation.ToString(CultureInfo.InvariantCulture)}.{secret}";
    private static (Guid Family,long Generation,string Secret) Parse(string value)
    {
        if(value is null || value.Length>180) throw Denied();
        var parts=value.Split('.');
        if(parts.Length!=3 || !Guid.TryParseExact(parts[0],"N",out var family)
            || !long.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out var generation)
            || generation<0 || !Input.Secret(parts[2])) throw Denied();
        return (family,generation,parts[2]);
    }
    public async Task<LoginResult> Login(LoginRequest request,string client)
    {
        // Reuse contact canonicalization, with login's 1..128 password input contract.
        Input.Require(request.Password is {Length:>=1 and <=128});
        var (kind,contact)=Input.Validate(new(request.Email,request.Mobile,"fixed validation password","Login",Secrets.Token()),Secrets.Token());
        await using var c=await db.Source.OpenConnectionAsync();
        var match=await c.One("SELECT id FROM persons WHERE contact_kind=@kind AND contact=@contact",("kind",kind),("contact",contact));
        var evidence=match is null ? null : await Evidence(c,match.Get<Guid>("id"));
        var observed=evidence is null ? null : await c.One("SELECT epoch FROM auth_issuance_state WHERE person_id=@id",("id",evidence.Get<Guid>("id")));
        var observedEpoch=observed?.Get<long>("epoch") ?? 0;
        var failures=new PasswordFailures(db,clock);
        var admitted=evidence is null || await failures.Allowed(evidence.Get<Guid>("id"));
        // Throttled and unknown contacts perform dummy work: no contact-enumeration timing shortcut.
        var verified=await PasswordWork.Verify(request.Password,admitted?evidence?.Optional<string>("password_hash") ?? Dummy:Dummy);
        if(!admitted) throw Denied(); // Admission refusal is not evidence of an invalid password.
        var valid=admitted && verified;
        if(evidence is not null && admitted) await failures.Record(evidence.Get<Guid>("id"),valid);
        var reason=Failure(evidence) ?? (!valid?"InvalidPassword":null);
        if(reason is not null)
        {
            await DomainEvent(c,"LoginFailed",evidence?.Get<Guid>("id"),null,Timestamps.Now(clock),new {ContactType=kind=="email"?"Email":"Mobile",ContactValue=contact,Reason=reason});
            throw Denied();
        }
        // No Credential/advisory/row locks: Person gate prevents admitted mutation and stale issuance.
        await using var tx=await c.BeginTransactionAsync();
        var person=evidence!.Get<Guid>("id"); var state=await gate.Acquire(tx,person);
        var current=await Evidence(c,person);
        if(!state.AllowsIssuance || state.Epoch!=observedEpoch || Failure(current) is not null
            || current!.Get<Guid>("credential_id")!=evidence.Get<Guid>("credential_id")
            || current.Get<string>("password_hash")!=evidence.Get<string>("password_hash")) throw Denied();
        var now=Timestamps.Now(clock); var deadline=now.AddSeconds(86400); var session=Guid.NewGuid(); var family=Guid.NewGuid(); var secret=Secrets.Token();
        var tokens=access.Issue(person,session,state.Epoch,client,RefreshToken(family,0,secret),deadline);
        await c.Execute("INSERT INTO auth_sessions VALUES(@id,@person,@epoch,@client,@now,@end,@now,'Active',NULL,NULL)",
            ("id",session),("person",person),("epoch",state.Epoch),("client",client),("now",now),("end",deadline));
        await c.Execute("INSERT INTO auth_refresh_families VALUES(@id,@session,@end,0,'Active',NULL)",("id",family),("session",session),("end",deadline));
        await Generation(c,family,0,client,secret,now,deadline);
        await DomainEvent(c,"LoginSucceeded",person,session,now,new {PersonId=person,SessionId=session});
        await DomainEvent(c,"SessionCreated",person,session,now,new {SessionId=session,PersonId=person,ExpiresAt=deadline});
        await tx.CommitAsync(); // No bearer response until durable commit.
        return new(Person(current),tokens);
    }
    private async Task Generation(NpgsqlConnection c,Guid family,long generation,string client,string secret,DateTimeOffset now,DateTimeOffset end)
        =>await c.Execute("INSERT INTO auth_refresh_generations VALUES(@id,@generation,@verifier,@key,'Current',@now,NULL,@end)",
            ("id",family),("generation",generation),("verifier",Verifier(family,generation,client,secret)),("key",RefreshKey),("now",now),("end",end));
    private static Task<Row?> Session(NpgsqlConnection c,Guid id)=>c.One("SELECT * FROM auth_sessions WHERE id=@id FOR UPDATE",("id",id));
    private static bool Eligible(Row? row,IssuanceGateState state,string client,DateTimeOffset now)
        =>row is not null && row.Get<string>("client_id")==client && row.Get<string>("status")=="Active"
          && state.AllowsSensitiveOperation(row.Get<long>("issuance_epoch")) && row.Time("expires_at")>now
          && row.Time("last_foreground_refresh_at").AddMinutes(30)>now;
    private static async Task Close(NpgsqlConnection c,Row session,DateTimeOffset now,string reason,string eventType)
    {
        if(session.Get<string>("status")!="Active") return;
        var id=session.Get<Guid>("id"); var person=session.Get<Guid>("person_id");
        await c.Execute("UPDATE auth_sessions SET status=@status,closed_at=@now,close_reason=@reason WHERE id=@id",
            ("status",eventType=="SessionExpired"?"Expired":"Closed"),("now",now),("reason",reason),("id",id));
        await c.Execute("UPDATE auth_refresh_families SET status='Revoked',revoked_at=@now WHERE session_id=@id AND status='Active'",("now",now),("id",id));
        if(eventType!="") await DomainEvent(c,eventType,person,id,now,new {SessionId=id,PersonId=person});
    }
    private static async Task Expire(NpgsqlConnection c,Row? session,DateTimeOffset now)
    {
        if(session is not null && session.Get<string>("status")=="Active")
        {
            if(session.Time("expires_at")<=now) await Close(c,session,now,"AbsoluteDeadline","SessionExpired");
            else if(session.Time("last_foreground_refresh_at").AddMinutes(30)<=now) await Close(c,session,now,"IdleDeadline","SessionExpired");
        }
    }
    public async Task<SessionTokens> Refresh(RefreshRequest request,string client)
    {
        var parsed=Parse(request.RefreshToken);
        await using var c=await db.Source.OpenConnectionAsync();
        // Authenticate secret and client before taking a Person gate or modifying unrelated state.
        var found=await c.One("""
            SELECT s.person_id,s.client_id,s.id AS session_id,g.verifier,g.key_id
            FROM auth_refresh_families f JOIN auth_sessions s ON s.id=f.session_id
            JOIN auth_refresh_generations g ON g.family_id=f.id WHERE f.id=@id AND g.generation=@generation
            """,("id",parsed.Family),("generation",parsed.Generation));
        if(found is null || found.Get<string>("client_id")!=client || found.Get<string>("key_id")!=RefreshKey
            || !Secrets.Equal(found.Get<byte[]>("verifier"),Verifier(parsed.Family,parsed.Generation,client,parsed.Secret))) throw Denied();
        await using var tx=await c.BeginTransactionAsync();
        var person=found.Get<Guid>("person_id"); var state=await gate.Acquire(tx,person);
        var session=await Session(c,found.Get<Guid>("session_id")); var now=Timestamps.Now(clock);
        var family=await c.One("SELECT * FROM auth_refresh_families WHERE id=@id FOR UPDATE",("id",parsed.Family));
        var generation=await c.One("SELECT * FROM auth_refresh_generations WHERE family_id=@id AND generation=@generation FOR UPDATE",("id",parsed.Family),("generation",parsed.Generation));
        await Expire(c,session,now);
        if(!Eligible(session,state,client,now) || Failure(await Evidence(c,person)) is not null || family?.Get<string>("status")!="Active")
        {
            await tx.CommitAsync(); throw Denied();
        }
        if(generation?.Get<string>("status")=="Consumed")
        {
            var eventId=Guid.NewGuid();
            await Close(c,session!,now,"RefreshReuse","");
            await SecurityEvent(c,eventId,session!.Get<Guid>("id"),parsed.Family,parsed.Generation,now,"RefreshTokenReuseDetected","PreviouslyConsumedGeneration");
            var delta=Math.Max(0,(long)(now-generation.Time("consumed_at")).TotalMilliseconds);
            await c.Execute("INSERT INTO auth_refresh_reuse_observations VALUES(@id,@delta)",("id",eventId),("delta",delta));
            await tx.CommitAsync(); throw Denied();
        }
        if(generation is null || generation.Get<string>("status")!="Current" || family!.Get<long>("current_generation")!=parsed.Generation || parsed.Generation==long.MaxValue)
            throw Denied();
        var next=parsed.Generation+1; var secret=Secrets.Token();
        var result=access.Issue(person,session!.Get<Guid>("id"),state.Epoch,client,RefreshToken(parsed.Family,next,secret),session.Time("expires_at"));
        await c.Execute("UPDATE auth_refresh_generations SET status='Consumed',consumed_at=@now WHERE family_id=@id AND generation=@generation",("now",now),("id",parsed.Family),("generation",parsed.Generation));
        await Generation(c,parsed.Family,next,client,secret,now,session.Time("expires_at"));
        await c.Execute("UPDATE auth_refresh_families SET current_generation=@generation WHERE id=@id",("generation",next),("id",parsed.Family));
        if(request.Foreground) await c.Execute("UPDATE auth_sessions SET last_foreground_refresh_at=@now WHERE id=@id",("now",now),("id",session.Get<Guid>("id")));
        await SecurityEvent(c,Guid.NewGuid(),session.Get<Guid>("id"),parsed.Family,next,now,"RefreshTokenRotated","RefreshAccepted");
        await tx.CommitAsync(); return result;
    }
    public async Task<SelfPerson> Self(AccessProof proof)
    {
        await using var c=await db.Source.OpenConnectionAsync(); await using var tx=await c.BeginTransactionAsync();
        var state=await gate.Acquire(tx,proof.PersonId); var session=await Session(c,proof.SessionId); var now=Timestamps.Now(clock);
        await Expire(c,session,now); var person=await Evidence(c,proof.PersonId);
        var family=await c.One("SELECT status FROM auth_refresh_families WHERE session_id=@id",("id",proof.SessionId));
        var valid=Eligible(session,state,proof.ClientId,now) && session!.Get<Guid>("person_id")==proof.PersonId
            && proof.Epoch==state.Epoch && Failure(person) is null && family?.Get<string>("status")=="Active";
        await tx.CommitAsync(); if(!valid) throw Denied(); return Person(person!);
    }
    public async Task Logout(AccessProof proof)
    {
        await using var c=await db.Source.OpenConnectionAsync(); await using var tx=await c.BeginTransactionAsync();
        var state=await gate.Acquire(tx,proof.PersonId); var session=await Session(c,proof.SessionId);
        if(session is null || session.Get<Guid>("person_id")!=proof.PersonId || session.Get<string>("client_id")!=proof.ClientId
            || session.Get<long>("issuance_epoch")!=proof.Epoch) throw Denied();
        // Bound signed proof can replay its own terminal logout; it never reopens issuance.
        if(session.Get<string>("status")=="Active")
        {
            if(!state.AllowsSensitiveOperation(proof.Epoch)) throw Denied();
            var now=Timestamps.Now(clock);
            if(session.Time("expires_at")<=now || session.Time("last_foreground_refresh_at").AddMinutes(30)<=now)
                await Expire(c,session,now);
            else await Close(c,session,now,"Logout","LogoutCompleted");
        }
        await tx.CommitAsync();
    }
    private static Task<int> SecurityEvent(NpgsqlConnection c,Guid id,Guid session,Guid family,long generation,DateTimeOffset now,string type,string reason)
        =>c.Execute("INSERT INTO auth_refresh_security_events VALUES(@id,@session,@family,@generation,@now,@type,@reason)",
            ("id",id),("session",session),("family",family),("generation",generation),("now",now),("type",type),("reason",reason));
    private static async Task DomainEvent(NpgsqlConnection c,string type,Guid? person,Guid? session,DateTimeOffset now,object payload)
    {
        var id=Guid.NewGuid(); var envelope=new Dictionary<string,object>{["EventId"]=id,["EventType"]=type,
            ["AggregateType"]=type is "LoginSucceeded" or "LoginFailed"?"Person":"Session",["OccurredAt"]=now,
            ["ExecutionContext"]=new {CorrelationId=id.ToString()},["Payload"]=payload};
        if(type=="LoginSucceeded") envelope["SessionReference"]=session!.Value;
        if(person is not null)
        {
            envelope["AggregateId"]=type is "LoginSucceeded" or "LoginFailed"?person.Value:session!.Value;
            envelope["ActorIdentity"]=type=="SessionExpired"?"System":person.Value.ToString();
        }
        await c.Execute("INSERT INTO auth_domain_event_outbox(id,person_id,session_id,event_type,payload,occurred_at) VALUES(@id,@person,@session,@type,CAST(@payload AS jsonb),@now)",
            ("id",id),("person",person),("session",session),("type",type),("payload",JsonSerializer.Serialize(envelope)),("now",now));
    }
}
