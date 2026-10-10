using System.Security.Cryptography;
using SmartCore.Identity;

internal static class AuthenticationChecks
{
    public static async Task<int> Run(Database db,Secrets secrets,TimeProvider clock,Action<TimeSpan> advance,string email,string password)
    {
        var count=0;
        void Check(bool valid,string label) {if(!valid)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);count++;}
        async Task Denied(Func<Task> action)
        {
            try {await action();} catch(ApiError e) when(e.Status==401 && e.Code=="UNAUTHORIZED") {return;}
            throw new Exception("Expected uniform unauthorized");
        }
        var access=new AccessTokens(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),clock);
        var auth=new AuthenticationService(db,secrets,clock,new PostgresAuthenticationIssuanceGate(),access);
        await using var sql=await db.Source.OpenConnectionAsync();
        const string client="test-bff";
        Task<LoginResult> Login()=>auth.Login(new(email,null,password),client);
        AccessProof Proof(SessionTokens s)=>access.Read(s.AccessToken,client);
        await Denied(async()=>{await auth.Login(new(Guid.NewGuid()+"@example.test",null,password),client);});
        await Denied(async()=>{await auth.Login(new(email,null,"wrong password"),client);});
        Check(true,"unknown contact and wrong password share outward unauthorized result");
        var registration=new RegistrationService(db,secrets,clock);
        var pendingEmail=Guid.NewGuid()+"@example.test"; var binding=Secrets.Token();
        var started=await registration.Start(new(pendingEmail,null,password,"Pending test",binding),Secrets.Token());
        var delivery=(await sql.One("SELECT * FROM delivery_outbox WHERE verification_id=@id",("id",started.VerificationSessionId)))!;
        var code=secrets.Open(delivery.Get<byte[]>("sealed_code"),"delivery:"+delivery.Get<Guid>("id"));
        await registration.Verify(new(started.VerificationSessionId,code,binding));
        await Denied(async()=>{await auth.Login(new(pendingEmail,null,password),client);});
        Check(true,"PendingCredential cannot create a Session");
        var login=await Login(); var first=login.Session;
        Check(first.AccessExpiresAt<=first.ExpiresAt && first.AccessExpiresAt<=clock.GetUtcNow().AddSeconds(900)
            && (await auth.Self(Proof(first))).PersonId==login.Person.PersonId,"login commits a Ready-only Session and bounded access then authenticates self");
        Check((await sql.One("SELECT count(*) AS n FROM auth_domain_event_outbox WHERE session_id=@id",("id",first.SessionId)))!.Get<long>("n")==2,
            "login and Session facts commit with their Session");
        await Denied(()=>Task.Run(()=>access.Read(first.AccessToken+"tamper",client)));
        await Denied(()=>Task.Run(()=>access.Read(first.AccessToken,"wrong-client")));
        var wrongSigner=new AccessTokens(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),clock);
        await Denied(()=>Task.Run(()=>wrongSigner.Read(first.AccessToken,client)));
        Check(true,"tampered signature, untrusted signing key and wrong-client access claims are rejected");
        await Denied(async()=>{await auth.Refresh(new(first.RefreshToken,true),"wrong-client");});
        var forged=first.RefreshToken[..(first.RefreshToken.LastIndexOf('.')+1)]+Secrets.Token();
        await Denied(async()=>{await auth.Refresh(new(forged,true),client);});
        var rotated=await auth.Refresh(new(first.RefreshToken,true),client);
        Check(rotated.RefreshToken!=first.RefreshToken && rotated.SessionId==first.SessionId && rotated.ExpiresAt==first.ExpiresAt,
            "wrong-client and guessed secret do not revoke; successful refresh rotates without extending Session");
        advance(TimeSpan.FromSeconds(2));
        await Denied(async()=>{await auth.Refresh(new(first.RefreshToken,true),client);});
        await Denied(async()=>{await auth.Refresh(new(rotated.RefreshToken,true),client);});
        await Denied(async()=>{await auth.Self(Proof(rotated));});
        Check((await sql.One("SELECT status FROM auth_sessions WHERE id=@id",("id",first.SessionId)))!.Get<string>("status")=="Closed",
            "consumed-token lost-response retry closes family and successor with zero grace");
        Check((await sql.One("SELECT milliseconds_since_consumption AS delta FROM auth_refresh_reuse_observations o JOIN auth_refresh_security_events e ON e.id=o.event_id WHERE e.session_id=@id",("id",first.SessionId)))!.Get<long>("delta")==2000,
            "reuse latency is measured without changing internal event payload or grace policy");
        var race=(await Login()).Session;
        async Task<bool> Present()
        {
            try {await auth.Refresh(new(race.RefreshToken,true),client);return true;}
            catch(ApiError e) when(e.Status==401) {return false;}
        }
        var raced=await Task.WhenAll(Present(),Present());
        Check(raced.Count(x=>x)==1 && (await sql.One("SELECT status FROM auth_sessions WHERE id=@id",("id",race.SessionId)))!.Get<string>("status")=="Closed",
            "same-generation race has one successor and strict reuse closes final family");
        var logout=(await Login()).Session; var logoutProof=Proof(logout);
        await auth.Logout(logoutProof); await auth.Logout(logoutProof);
        await Denied(async()=>{await auth.Refresh(new(logout.RefreshToken,true),client);});
        Check((await sql.One("SELECT count(*) AS n FROM auth_domain_event_outbox WHERE session_id=@id AND event_type='LogoutCompleted'",("id",logout.SessionId)))!.Get<long>("n")==1,
            "logout and lost-response replay preserve one closure/event and deny refresh");
        var expiry=(await Login()).Session;
        advance(TimeSpan.FromSeconds(900));
        await Denied(()=>Task.Run(()=>Proof(expiry)));
        var afterAccessExpiry=await auth.Refresh(new(expiry.RefreshToken,true),client);
        Check(afterAccessExpiry.SessionId==expiry.SessionId,"expired access is rejected exactly; live refresh creates access within original Session");
        var background=(await Login()).Session;
        advance(TimeSpan.FromMinutes(20));
        background=await auth.Refresh(new(background.RefreshToken,false),client);
        advance(TimeSpan.FromMinutes(10));
        await Denied(async()=>{await auth.Refresh(new(background.RefreshToken,true),client);});
        Check((await sql.One("SELECT close_reason FROM auth_sessions WHERE id=@id",("id",background.SessionId)))!.Get<string>("close_reason")=="IdleDeadline",
            "background rotation does not extend foreground idle; exact 30-minute boundary expires");
        var foreground=(await Login()).Session;
        advance(TimeSpan.FromMinutes(29));
        foreground=await auth.Refresh(new(foreground.RefreshToken,true),client);
        advance(TimeSpan.FromMinutes(2));
        foreground=await auth.Refresh(new(foreground.RefreshToken,true),client);
        Check((await auth.Self(Proof(foreground))).PersonId==login.Person.PersonId,"successful trusted foreground refresh advances idle evidence");
        var absolute=(await Login()).Session;
        advance(TimeSpan.FromSeconds(86400));
        await Denied(async()=>{await auth.Refresh(new(absolute.RefreshToken,true),client);});
        Check((await sql.One("SELECT close_reason FROM auth_sessions WHERE id=@id",("id",absolute.SessionId)))!.Get<string>("close_reason")=="AbsoluteDeadline",
            "immutable absolute deadline expires refresh independently of foreground activity");
        // Internal already-verified admission fixture; no public change/reset path exists.
        var fenced=(await Login()).Session; var proof=Proof(fenced); var operation=Guid.NewGuid();
        await using(var c=await db.Source.OpenConnectionAsync())
        await using(var tx=await c.BeginTransactionAsync())
        {
            var state=await new PostgresAuthenticationIssuanceGate().Acquire(tx,proof.PersonId); var now=Timestamps.Now(clock);
            await c.Execute("""
                INSERT INTO auth_credential_change_intents(id,person_id,kind,request_mac,verified_at,created_at,target_epoch,
                  credential_operation_id,expected_credential_id,protected_material_reference,stage,next_attempt_at)
                VALUES(@id,@person,'ChangePassword',@mac,@now,@now,@epoch,@credential,@expected,'fixture-only','Fenced',@now)
                """,("id",operation),("person",proof.PersonId),("mac",secrets.Mac("fixture",operation.ToString())),("now",now),
                ("epoch",state.Epoch+1),("credential",Guid.NewGuid()),("expected",Guid.NewGuid()));
            await c.Execute("UPDATE auth_issuance_state SET epoch=epoch+1,pending_operation_id=@id WHERE person_id=@person",("id",operation),("person",proof.PersonId));
            await tx.CommitAsync();
        }
        await Denied(async()=>{await Login();});
        await Denied(async()=>{await auth.Self(proof);});
        await Denied(async()=>{await auth.Refresh(new(fenced.RefreshToken,true),client);});
        Check(true,"persistent pending fence uniformly denies login, self and refresh without exposing mutation state");
        await using(var c=await db.Source.OpenConnectionAsync())
        await using(var tx=await c.BeginTransactionAsync())
        {
            await new PostgresAuthenticationIssuanceGate().Acquire(tx,proof.PersonId);
            await c.Execute("UPDATE auth_credential_change_intents SET stage='Reconciled',resolved_at=@now WHERE id=@id",("id",operation),("now",Timestamps.Now(clock)));
            await c.Execute("UPDATE auth_issuance_state SET pending_operation_id=NULL WHERE person_id=@person",("person",proof.PersonId));
            await tx.CommitAsync();
        }
        await Denied(async()=>{await auth.Self(proof);});
        Check((await Login()).Person.PersonId==proof.PersonId,"new login uses resolved epoch; old access proof remains invalid online");
        return count;
    }
}
