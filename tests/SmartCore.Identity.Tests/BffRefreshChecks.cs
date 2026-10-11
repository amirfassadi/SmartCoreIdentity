using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using Npgsql;
using SmartCore.Identity;
using SmartCore.Identity.Bff;

internal static class BffRefreshChecks
{
    public static async Task<int> Run(Database db,Secrets identitySecrets,TimeProvider clock,Action<TimeSpan> advance,string connection)
    {
        var count=0;
        void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: BFF "+label);count++;}
        async Task Reauth(Func<Task> work)
        {try{await work();}catch(BffError e)when(e.Code=="REAUTHENTICATION_REQUIRED"){return;}throw new Exception("Expected BFF reauthentication");}
        try{BffStore.RequireServerVersion(new Version(16,9));throw new Exception("Accepted PostgreSQL 16");}
        catch(InvalidOperationException e)when(e.Message.Contains("PostgreSQL 17",StringComparison.Ordinal)){}
        BffStore.RequireServerVersion(new Version(17,0));
        Check(true,"PostgreSQL 16 is rejected with an explicit startup error and 17 is accepted");
        var aConfig=new NpgsqlConnectionStringBuilder(connection){ApplicationName="bff-a-test"};
        var bConfig=new NpgsqlConnectionStringBuilder(connection){ApplicationName="bff-b-test"};
        await using var storeA=new BffStore(aConfig.ConnectionString);await using var storeB=new BffStore(bConfig.ConnectionString);
        await storeA.Migrate();await storeB.Migrate();
        await using var sql=await db.Source.OpenConnectionAsync();
        await sql.Execute("TRUNCATE bff_sessions CASCADE");
        var macKey=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));var materialKey=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var secrets=new Secrets(macKey,materialKey);
        var signer=new AccessTokens(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),clock);
        var auth=new AuthenticationService(db,identitySecrets,clock,new PostgresAuthenticationIssuanceGate(),signer);
        const string client="bff-core-tests",password="bff native fixture password";
        var contact=Guid.NewGuid()+"@example.test";var binding=Secrets.Token();
        var registrations=new RegistrationService(db,identitySecrets,clock);var provisioning=new Provisioning(db,identitySecrets,clock);
        var start=await registrations.Start(new(contact,null,password,"BFF",binding),Secrets.Token());
        var delivery=(await sql.One("SELECT * FROM delivery_outbox WHERE verification_id=@id",("id",start.VerificationSessionId)))!;
        var otp=identitySecrets.Open(delivery.Get<byte[]>("sealed_code"),"delivery:"+delivery.Get<Guid>("id"));
        var registered=await registrations.Verify(new(start.VerificationSessionId,otp,binding));
        await provisioning.EnsureCredential(registered.Result.RegistrationId);await provisioning.MarkReady(registered.Result.RegistrationId);await provisioning.Acknowledge(registered.Result.RegistrationId);
        async Task<(string Cookie,SessionTokens Tokens)> Fixture()
        {
            var tokens=(await auth.Login(new(contact,null,password),client)).Session;
            var bootstrap=new BffRefresh(storeA,secrets,clock,new Transport(auth,client));
            return(await bootstrap.Create(tokens),tokens);
        }
        BffRefresh Core(BffStore store,Transport transport,IRefreshProbe? probe=null,BffBudgets? budgets=null)=>new(store,secrets,clock,transport,budgets,probe);
        async Task<Row> State(SessionTokens tokens)=>(await sql.One("SELECT * FROM bff_sessions WHERE identity_session_id=@id",("id",tokens.SessionId)))!;
        async Task<long> Generation(SessionTokens tokens)=>(await sql.One("SELECT current_generation FROM auth_refresh_families WHERE session_id=@id",("id",tokens.SessionId)))!.Get<long>("current_generation");
        var parallel=await Fixture();advance(TimeSpan.FromMinutes(13));
        var transport=new Transport(auth,client);var first=Core(storeA,transport);var second=Core(storeB,transport);
        var results=await Task.WhenAll(Enumerable.Range(0,20).Select(i=>(i%2==0?first:second).RefreshIfDue(parallel.Cookie,BffActivity.UserAction)));
        Check(transport.Calls==1 && results.All(r=>r.Version==1 && r.AccessToken==results[0].AccessToken) && await Generation(parallel.Tokens)==1,
            "20 parallel refresh decisions across two independent PostgreSQL pools produce exactly one real Identity rotation");
        Check((await sql.One("SELECT count(*) AS n FROM auth_refresh_security_events WHERE session_id=@id AND type='RefreshTokenReuseDetected'",("id",parallel.Tokens.SessionId)))!.Get<long>("n")==0,
            "coalesced requests never submit a spent predecessor or close its family");
        var row=await State(parallel.Tokens);
        Check(row.Get<byte[]>("sealed_tokens").Length>0 && !System.Text.Encoding.UTF8.GetString(row.Get<byte[]>("sealed_tokens")).Contains(parallel.Tokens.RefreshToken,StringComparison.Ordinal)
            && !Secrets.Equal(row.Get<byte[]>("cookie_mac"),System.Text.Encoding.UTF8.GetBytes(parallel.Cookie)),
            "tokens are sealed and opaque cookie is stored only as a purpose-bound verifier");

        var fast=await Fixture();advance(TimeSpan.FromMinutes(13));
        var blocked=new Transport(auth,client){Pause=true};var owner=Core(storeA,blocked).RefreshIfDue(fast.Cookie,BffActivity.Background);
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cached=await Core(storeB,blocked).GetAccess(fast.Cookie).WaitAsync(TimeSpan.FromSeconds(1));
        Check(cached.Version==0 && cached.AccessToken==fast.Tokens.AccessToken && blocked.Calls==1,
            "valid committed access remains available without waiting behind an in-flight refresh row lock");
        blocked.Release.SetResult();await owner;

        var expired=await Fixture();advance(TimeSpan.FromMinutes(16));
        var waitingTransport=new Transport(auth,client){Pause=true};
        var refreshing=Core(storeA,waitingTransport).GetAccess(expired.Cookie);
        await waitingTransport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var contender=Core(storeB,waitingTransport).GetAccess(expired.Cookie);
        await Task.Delay(2100);
        Check(!contender.IsCompleted,"expired-access contender waits past two seconds within the owner budget plus margin");
        waitingTransport.Release.SetResult();var waits=await Task.WhenAll(refreshing,contender);
        Check(waitingTransport.Calls==1 && waits.All(r=>r.Version==1),"expired-access waiter re-reads the committed successor and avoids a second rotation");

        foreach(var stage in Enum.GetValues<RefreshStage>())
        {
            var fixture=await Fixture();advance(TimeSpan.FromMinutes(13));var faultyTransport=new Transport(auth,client);
            var crashing=Core(storeA,faultyTransport,new Probe(stage,(_,_)=>throw new IOException("Injected owner loss")));
            try{await crashing.RefreshIfDue(fixture.Cookie,BffActivity.UserAction);throw new Exception("Expected injected loss");}
            catch(IOException)when(stage==RefreshStage.BeforeDispatch){}
            catch(BffError e)when(e.Code=="REAUTHENTICATION_REQUIRED"){}
            var recovery=Core(storeB,faultyTransport);
            if(stage==RefreshStage.BeforeDispatch)
            {
                var resumed=await recovery.RefreshIfDue(fixture.Cookie,BffActivity.UserAction);
                Check(faultyTransport.Calls==1 && resumed.Version==1,"owner loss before dispatch evidence safely permits the first dispatch");
            }
            else if(stage==RefreshStage.SuccessorCommitted)
            {
                var committed=await recovery.RefreshIfDue(fixture.Cookie,BffActivity.UserAction);
                var committedState=await State(fixture.Tokens);
                Check(faultyTransport.Calls==1 && committed.Version==1 && (await sql.One("SELECT attempt_id FROM bff_refresh_journal WHERE session_id=@id",("id",committedState.Get<Guid>("id"))))!.Has("attempt_id")==false,
                    "lost commit acknowledgment or crash before journal cleanup recognizes the exact committed attempt and successor");
            }
            else
            {
                var calls=faultyTransport.Calls;
                await Reauth(async()=>{await recovery.RefreshIfDue(fixture.Cookie,BffActivity.UserAction);});
                await Reauth(async()=>{await recovery.GetAccess(fixture.Cookie);});
                var unknown=await State(fixture.Tokens);
                Check(faultyTransport.Calls==calls && unknown.Get<string>("status")=="ReauthenticationRequired" && !unknown.Has("sealed_tokens"),
                    "owner loss at "+stage+" retains dispatch evidence, erases tokens on recovery and never resends the predecessor");
            }
        }
        var disconnected=await Fixture();advance(TimeSpan.FromMinutes(13));var disconnectedTransport=new Transport(auth,client);
        var kill=new Probe(RefreshStage.ResponseReceived,async(_,_)=>
        {
            var killed=await sql.Rows("SELECT pg_terminate_backend(pid) AS killed FROM pg_stat_activity WHERE application_name='bff-a-test' AND state='idle in transaction'");
            if(killed.Count!=1 || !killed[0].Get<bool>("killed"))throw new Exception("Expected one owner connection");
        });
        await Reauth(async()=>{await Core(storeA,disconnectedTransport,kill).RefreshIfDue(disconnected.Cookie,BffActivity.UserAction);});
        await Reauth(async()=>{await Core(storeB,disconnectedTransport).RefreshIfDue(disconnected.Cookie,BffActivity.UserAction);});
        Check(disconnectedTransport.Calls==1 && await Generation(disconnected.Tokens)==1,
            "actual PostgreSQL owner connection termination after Identity commit preserves independent dispatch evidence and prevents reuse");

        foreach(var mode in new[]{"lost","malformed","timeout"})
        {
            var fixture=await Fixture();advance(TimeSpan.FromMinutes(13));var broken=new Transport(auth,client){Mode=mode};
            var shortBudgets=new BffBudgets(TimeSpan.FromMilliseconds(200),TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(3));
            await Reauth(async()=>{await Core(storeA,broken,budgets:shortBudgets).RefreshIfDue(fixture.Cookie,BffActivity.UserAction);});
            await Reauth(async()=>{await Core(storeB,broken).RefreshIfDue(fixture.Cookie,BffActivity.UserAction);});
            Check(broken.Calls==1,"HTTP "+mode+" outcome has no automatic refresh retry across instances");
        }
        var cancelFixture=await Fixture();advance(TimeSpan.FromMinutes(13));var cancelTransport=new Transport(auth,client){Pause=true};
        using(var cancellation=new CancellationTokenSource())
        {
            var cancelling=Core(storeA,cancelTransport).RefreshIfDue(cancelFixture.Cookie,BffActivity.UserAction,cancellation.Token);
            await cancelTransport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));cancellation.Cancel();
            await Reauth(async()=>{await cancelling;});
            await Reauth(async()=>{await Core(storeB,cancelTransport).RefreshIfDue(cancelFixture.Cookie,BffActivity.UserAction);});
        }
        Check(cancelTransport.Calls==1,"caller cancellation after dispatch preserves the marker and cannot reopen a retry");

        var activity=await Fixture();var initial=(await State(activity.Tokens)).Time("last_foreground_at");advance(TimeSpan.FromMinutes(13));
        var activityTransport=new Transport(auth,client);var activityA=Core(storeA,activityTransport);var activityB=Core(storeB,activityTransport);
        await activityA.RefreshIfDue(activity.Cookie,BffActivity.Background);
        Check((await State(activity.Tokens)).Time("last_foreground_at")==initial && activityTransport.Foregrounds.SequenceEqual(new[]{false}),
            "background refresh never advances foreground idle evidence");
        await Task.WhenAll(Enumerable.Range(0,20).Select(i=>(i%2==0?activityA:activityB).RefreshIfDue(activity.Cookie,BffActivity.UserAction)));
        Check(activityTransport.Calls==2 && activityTransport.Foregrounds.SequenceEqual(new[]{false,true}),
            "fresh background access does not hide due foreground activity; parallel user actions coalesce once");
        advance(TimeSpan.FromMinutes(13));await activityA.RefreshIfDue(activity.Cookie,BffActivity.Background);
        advance(TimeSpan.FromMinutes(13));await activityB.RefreshIfDue(activity.Cookie,BffActivity.Background);
        advance(TimeSpan.FromMinutes(4));await Reauth(async()=>{await activityA.GetAccess(activity.Cookie);});
        Check((await State(activity.Tokens)).Time("last_foreground_at").AddMinutes(30)==Timestamps.Now(clock),
            "polling beyond thirty minutes cannot keep the foreground idle deadline alive");
        var revokedFixture=await Fixture();advance(TimeSpan.FromMinutes(13));
        await auth.Logout(signer.Read(revokedFixture.Tokens.AccessToken,client));
        var revokedTransport=new Transport(auth,client);
        await Reauth(async()=>{await Core(storeA,revokedTransport).RefreshIfDue(revokedFixture.Cookie,BffActivity.UserAction);});
        Check(revokedTransport.Calls==1 && !(await State(revokedFixture.Tokens)).Has("uncertain_at"),
            "definitive Identity revocation requires login but is excluded from uncertain-outcome ratio");
        // Exercise the concrete non-redirecting HTTP adapter through a loopback Identity bridge.
        // This is not the complete Identity middleware/BFF browser host.
        var logoutCalls=0;var logoutMode="normal";var httpActive=0;var subjects=new ConcurrentQueue<string>();var httpCalls=0;var trapCalls=0;var httpMode="normal";var httpKey=Secrets.Token();var receiptKey=Secrets.Token();
        var httpBuilder=WebApplication.CreateSlimBuilder(new WebApplicationOptions {EnvironmentName="Development"});
        httpBuilder.Logging.ClearProviders();httpBuilder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,0));
        await using(var host=httpBuilder.Build())
        {
            host.Use(async(context,next)=>{Interlocked.Increment(ref httpActive);try{await next();}finally{Interlocked.Decrement(ref httpActive);}});
            host.MapPost("/auth/refresh",async(HttpContext context)=>
            {
                Interlocked.Increment(ref httpCalls);
                subjects.Enqueue(context.Request.Headers["X-Bff-Subject"].ToString());
                if(context.Request.Headers["X-Bff-Client-Key"]!=httpKey || !Input.Secret(context.Request.Headers["X-Bff-Subject"].ToString()))
                {
                    context.Response.StatusCode=403;
                    await context.Response.WriteAsJsonAsync(RefreshRejection.Create(receiptKey,403,"BFF_CREDENTIALS_REJECTED",Guid.Parse(context.Request.Headers["X-Refresh-Request"].ToString()),2));return;
                }
                if(httpMode=="null-proof403")
                {
                    context.Response.StatusCode=403;
                    var invalid=RefreshRejection.Create(receiptKey,403,"BFF_CREDENTIALS_REJECTED",Guid.Parse(context.Request.Headers["X-Refresh-Request"].ToString()),2) with {Proof=null!};
                    await context.Response.WriteAsJsonAsync(invalid);return;
                }
                if(httpMode=="raw401"){context.Response.StatusCode=401;return;}
                if(httpMode=="random-kill")await Task.Delay(Random.Shared.Next(20,100));
                if(httpMode=="redirect"){context.Response.StatusCode=307;context.Response.Headers.Location="/trap";return;}
                if(httpMode is "rate" or "busy" or "unsigned" or "wrong-nonce" or "bad-proof" or "proxy502" or "proxy504")
                {
                    var status=httpMode=="rate"?429:httpMode=="proxy502"?502:httpMode=="proxy504"?504:503;
                    context.Response.StatusCode=status;
                    if(httpMode is "unsigned" or "proxy502" or "proxy504")
                    {await context.Response.WriteAsJsonAsync(new {code="AUTHENTICATION_BUSY"});return;}
                    var reference=Guid.Parse(context.Request.Headers["X-Refresh-Request"].ToString());
                    var receipt=RefreshRejection.Create(receiptKey,status,status==429?"RATE_LIMITED":"AUTHENTICATION_BUSY",httpMode=="wrong-nonce"?Guid.NewGuid():reference,2);
                    if(httpMode=="bad-proof")receipt=receipt with {Proof=Convert.ToBase64String(new byte[32])};
                    await context.Response.WriteAsJsonAsync(receipt);return;
                }
                var req=await context.Request.ReadFromJsonAsync<RefreshRequest>(cancellationToken:context.RequestAborted);
                if(req is null){context.Response.StatusCode=400;return;}
                try
                {
                    var rotated=await auth.Refresh(req,client);
                    if(httpMode=="lost"){context.Abort();return;}
                    await context.Response.WriteAsJsonAsync(rotated,cancellationToken:context.RequestAborted);
                }
                catch(ApiError error){context.Response.StatusCode=error.Status;}
            });
            host.MapPost("/auth/logout",async(HttpContext context)=>
            {
                Interlocked.Increment(ref logoutCalls);
                if(context.Request.Headers["X-Bff-Client-Key"]!=httpKey || !Input.Secret(context.Request.Headers["X-Bff-Subject"].ToString()))
                {context.Response.StatusCode=403;return;}
                var header=context.Request.Headers.Authorization.ToString();
                try
                {
                    var proof=signer.Read(header[7..],client);await auth.Logout(proof);
                    if(logoutMode=="lost"){context.Abort();return;}
                    context.Response.StatusCode=204;
                }
                catch(ApiError e){context.Response.StatusCode=e.Status;}
            });
            host.MapPost("/trap",()=>{Interlocked.Increment(ref trapCalls);return Results.NoContent();});
            await host.StartAsync();
            var address=host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http=new IdentityRefreshHttp(new Uri(address+"/"),httpKey,receiptKey,developmentLoopback:true);
            var network=await Fixture();advance(TimeSpan.FromMinutes(13));
            var networkA=new BffRefresh(storeA,secrets,clock,http);var networkB=new BffRefresh(storeB,secrets,clock,http);
            var networkResults=await Task.WhenAll(Enumerable.Range(0,20).Select(i=>(i%2==0?networkA:networkB).RefreshIfDue(network.Cookie,BffActivity.UserAction)));
            Check(httpCalls==1 && networkResults.All(r=>r.Version==1) && await Generation(network.Tokens)==1,
                "concrete HTTP adapter and two BFF instances coalesce twenty requests to exactly one authenticated Identity HTTP rotation");
            httpMode="lost";var lostHttp=await Fixture();advance(TimeSpan.FromMinutes(13));var beforeLost=httpCalls;
            await Reauth(async()=>{await networkA.RefreshIfDue(lostHttp.Cookie,BffActivity.UserAction);});
            await Reauth(async()=>{await networkB.RefreshIfDue(lostHttp.Cookie,BffActivity.UserAction);});
            Check(httpCalls==beforeLost+1 && await Generation(lostHttp.Tokens)==1,
                "actual HTTP connection abort after Identity commit causes reauthentication with no replay");
            httpMode="redirect";var redirected=await Fixture();advance(TimeSpan.FromMinutes(13));var beforeRedirect=httpCalls;
            await Reauth(async()=>{await networkA.RefreshIfDue(redirected.Cookie,BffActivity.UserAction);});
            Check(httpCalls==beforeRedirect+1 && trapCalls==0 && await Generation(redirected.Tokens)==0,
                "HTTP redirect is not followed and never forwards refresh secrets to another route");
            foreach(var mode in new[]{"rate","busy"})
            {
                httpMode=mode;var limited=await Fixture();advance(TimeSpan.FromMinutes(13));var before=httpCalls;
                async Task Busy(BffRefresh core)
                {
                    try{await core.RefreshIfDue(limited.Cookie,BffActivity.UserAction);}
                    catch(BffError e)when(e.Code=="BFF_BUSY" && e.RetryAfterSeconds>=2){return;}
                    throw new Exception("Expected retryable admission response");
                }
                await Busy(networkA);var preserved=await State(limited.Tokens);
                Check(preserved.Get<string>("status")=="Active" && preserved.Get<long>("version")==0 && preserved.Has("sealed_tokens")
                    && await Generation(limited.Tokens)==0 && (await sql.One("SELECT attempt_id FROM bff_refresh_journal WHERE session_id=@id",("id",preserved.Get<Guid>("id"))))!.Has("attempt_id")==false,
                    mode+" verified NotProcessed CAS clears dispatch without erasing or consuming tokens");
                await Task.WhenAll(Enumerable.Range(0,20).Select(i=>Busy(i%2==0?networkA:networkB)));
                Check(httpCalls==before+1,"shared admission retry deadline prevents a twenty-request cross-instance hot loop");
                advance(TimeSpan.FromSeconds(3));await Busy(networkB);
                advance(TimeSpan.FromSeconds(3));httpMode="normal";
                var retry=await networkA.RefreshIfDue(limited.Cookie,BffActivity.UserAction);
                Check(retry.Version==1 && await Generation(limited.Tokens)==1,"bounded later retry after "+mode+" rotates the unconsumed predecessor exactly once");
            }
            foreach(var mode in new[]{"unsigned","wrong-nonce","bad-proof","proxy502","proxy504"})
            {
                httpMode=mode;var untrusted=await Fixture();advance(TimeSpan.FromMinutes(13));var before=httpCalls;
                await Reauth(async()=>{await networkA.RefreshIfDue(untrusted.Cookie,BffActivity.UserAction);});
                await Reauth(async()=>{await networkB.RefreshIfDue(untrusted.Cookie,BffActivity.UserAction);});
                Check(httpCalls==before+1 && (await State(untrusted.Tokens)).Has("uncertain_at"),mode+" rejection remains uncertain and is never retried");
            }
            httpMode="normal";
            var circuit=new BffCircuit(storeA,clock);
            var badConfig=await Fixture();advance(TimeSpan.FromMinutes(13));var beforeBad=httpCalls;
            using(var badClient=new IdentityRefreshHttp(new Uri(address+"/"),Secrets.Token(),receiptKey,true))
            {
                try{await new BffRefresh(storeA,secrets,clock,badClient).RefreshIfDue(badConfig.Cookie,BffActivity.UserAction);throw new Exception("Expected config rejection");}
                catch(BffError e)when(e.Code=="BFF_UNAVAILABLE"){}
            }
            var safeBad=await State(badConfig.Tokens);
            Check(safeBad.Get<string>("status")=="Active" && safeBad.Has("sealed_tokens") && await Generation(badConfig.Tokens)==0,
                "wrong BFF credential with independent receipt key preserves active tokens and opens shared circuit");
            try{await networkB.RefreshIfDue(badConfig.Cookie,BffActivity.UserAction);throw new Exception("Expected open circuit");}
            catch(BffError e)when(e.Code=="BFF_UNAVAILABLE"){}
            Check(httpCalls==beforeBad+1,"configuration circuit prevents dispatch from another pool");
            await circuit.Reset();advance(TimeSpan.FromSeconds(3));await networkB.RefreshIfDue(badConfig.Cookie,BffActivity.UserAction);
            Check(await Generation(badConfig.Tokens)==1,"operator reset after credential repair safely rotates known non-consumed predecessor");
            Check(subjects.Count>1 && subjects.All(Input.Secret) && subjects.Distinct().Count()>1,
                "one HTTP adapter derives opaque admission subjects per session rather than per instance");
            await circuit.Reset();
            var keyMismatch=await Fixture();advance(TimeSpan.FromMinutes(13));var beforeMismatch=httpCalls;
            using(var mismatched=new IdentityRefreshHttp(new Uri(address+"/"),Secrets.Token(),Secrets.Token(),true))
            {
                try{await new BffRefresh(storeA,secrets,clock,mismatched).RefreshIfDue(keyMismatch.Cookie,BffActivity.UserAction);throw new Exception("Expected mismatched receipt quarantine");}
                catch(BffError e)when(e.Code=="BFF_UNAVAILABLE"){}
            }
            var mismatchState=await State(keyMismatch.Tokens);
            Check(mismatchState.Has("sealed_tokens") && (await sql.One("SELECT attempt_id FROM bff_refresh_journal WHERE session_id=@id",("id",mismatchState.Get<Guid>("id"))))!.Has("attempt_id"),
                "both credential and receipt key mismatch quarantines material and durable dispatch instead of clearing or retrying");
            await circuit.Reset();await Reauth(async()=>{await networkB.RefreshIfDue(keyMismatch.Cookie,BffActivity.UserAction);});
            Check(httpCalls==beforeMismatch+1,"operator circuit reset does not retry a rejection without verified non-consumption");
            await circuit.Reset();httpMode="null-proof403";
            var malformed403=await Fixture();advance(TimeSpan.FromMinutes(13));
            try{await networkA.RefreshIfDue(malformed403.Cookie,BffActivity.UserAction);throw new Exception("Expected invalid receipt quarantine");}
            catch(BffError e)when(e.Code=="BFF_UNAVAILABLE"){}
            Check((await State(malformed403.Tokens)).Has("sealed_tokens") && await circuit.IsOpen(),
                "malformed 403 receipt with null proof preserves material and opens circuit without clearing dispatch");
            await circuit.Reset();httpMode="raw401";
            var cohort=new List<(string Cookie,SessionTokens Tokens)>();
            for(var n=0;n<6;n++){cohort.Add(await Fixture());advance(TimeSpan.FromMinutes(13));}
            // Keep the earlier sessions live while advancing the test clock.
            foreach(var f in cohort)await sql.Execute("UPDATE bff_sessions SET last_foreground_at=@now WHERE identity_session_id=@id",("now",Timestamps.Now(clock)),("id",f.Tokens.SessionId));
            for(var n=0;n<4;n++)await Reauth(async()=>{await networkA.RefreshIfDue(cohort[n].Cookie,BffActivity.UserAction);});
            try{await networkA.RefreshIfDue(cohort[4].Cookie,BffActivity.UserAction);throw new Exception("Expected rejection breaker");}
            catch(BffError e)when(e.Code=="BFF_UNAVAILABLE"){}
            var quarantined=await State(cohort[4].Tokens);var beforeStop=httpCalls;
            try{await networkB.RefreshIfDue(cohort[5].Cookie,BffActivity.UserAction);throw new Exception("Expected shared circuit");}
            catch(BffError e)when(e.Code=="BFF_UNAVAILABLE"){}
            Check(quarantined.Has("sealed_tokens") && quarantined.Get<string>("status")=="Active" && httpCalls==beforeStop,
                "five distinct session rejections latch shared breaker; triggering session and later sessions are not erased");
            await circuit.Reset();httpMode="normal";
            await Reauth(async()=>{await networkB.RefreshIfDue(cohort[4].Cookie,BffActivity.UserAction);});
            Check(httpCalls==beforeStop,"resetting breaker never clears unmatched dispatch evidence or authorizes retry");
            BffPeerConfig Config(string cookie,RefreshStage? stage=null)=>new(connection,macKey,materialKey,address+"/",httpKey,receiptKey,Timestamps.Now(clock),cookie,stage);
            var processes=await Fixture();advance(TimeSpan.FromMinutes(13));var beforeProcesses=httpCalls;
            await using(var peerA=await BffProcessPeer.Peer.Start(Config(processes.Cookie)))
            await using(var peerB=await BffProcessPeer.Peer.Start(Config(processes.Cookie)))
            {
                await Task.WhenAll(peerA.Go(),peerB.Go());
                var output=await Task.WhenAll(peerA.Line(),peerB.Line());
                Check(output.All(x=>x=="DONE:"+string.Join(',',Enumerable.Repeat(1,10))) && httpCalls==beforeProcesses+1 && await Generation(processes.Tokens)==1,
                    "twenty requests in two independent OS processes make exactly one Identity HTTP rotation");
            }
            foreach(var stage in Enum.GetValues<RefreshStage>())
            {
                var killed=await Fixture();advance(TimeSpan.FromMinutes(13));var beforeKill=httpCalls;
                await using(var victim=await BffProcessPeer.Peer.Start(Config(killed.Cookie,stage)))
                {
                    await victim.Go();
                    if(await victim.Line()!="PAUSED:"+stage)throw new Exception("Peer missed requested crash boundary");
                    await victim.Kill();
                }
                await using var survivor=await BffProcessPeer.Peer.Start(Config(killed.Cookie));await survivor.Go();var outcome=await survivor.Line();
                if(stage is RefreshStage.BeforeDispatch or RefreshStage.SuccessorCommitted)
                    Check(outcome=="DONE:"+string.Join(',',Enumerable.Repeat(1,10)) && httpCalls==beforeKill+1 && await Generation(killed.Tokens)==1,
                        "actual process kill at "+stage+" recovers one successor without replay");
                else
                    Check(outcome=="ERROR:REAUTHENTICATION_REQUIRED" && httpCalls==beforeKill+(stage==RefreshStage.DispatchCommitted?0:1)
                        && !(await State(killed.Tokens)).Has("sealed_tokens"),
                        "actual process kill at "+stage+" releases row lock but retains durable marker; survivor never resends");
            }
            var logoutFixture=await Fixture();logoutMode="lost";var beforeLogout=logoutCalls;
            try{await networkA.Logout(logoutFixture.Cookie);throw new Exception("Expected lost logout response");}
            catch(BffError e)when(e.Code=="BFF_UNAVAILABLE"){}
            Check((await State(logoutFixture.Tokens)).Has("sealed_tokens") && (await sql.One("SELECT status FROM auth_sessions WHERE id=@id",("id",logoutFixture.Tokens.SessionId)))!.Get<string>("status")=="Closed",
                "lost real HTTP logout response preserves BFF material after Identity has closed the session");
            logoutMode="normal";await networkB.Logout(logoutFixture.Cookie);await networkA.Logout(logoutFixture.Cookie);
            var logoutEvents=(await sql.One("SELECT count(*) AS n FROM auth_domain_event_outbox WHERE event_type='LogoutCompleted' AND session_id=@id",("id",logoutFixture.Tokens.SessionId)))!.Get<long>("n");
            Check(logoutCalls==beforeLogout+2 && logoutEvents==1 && !(await State(logoutFixture.Tokens)).Has("sealed_tokens"),
                "retry of idempotent upstream logout commits one Identity event before BFF erasure; terminal local replay sends nothing");
            var raceLogout=await Fixture();advance(TimeSpan.FromMinutes(13));
            async Task RaceRefresh(){try{await networkA.RefreshIfDue(raceLogout.Cookie,BffActivity.UserAction);}catch(BffError e)when(e.Code=="REAUTHENTICATION_REQUIRED"){}}
            await Task.WhenAll(RaceRefresh(),networkB.Logout(raceLogout.Cookie));
            Check(!(await State(raceLogout.Tokens)).Has("sealed_tokens") && await Generation(raceLogout.Tokens)<=1
                && (await sql.One("SELECT status FROM auth_sessions WHERE id=@id",("id",raceLogout.Tokens.SessionId)))!.Get<string>("status")=="Closed",
                "logout and refresh serialize across pools and close Identity before local erasure without reuse");
            var access401=await Fixture();var accessSnapshot=await networkA.GetAccess(access401.Cookie);
            await networkB.RejectAccess(access401.Cookie,accessSnapshot.SessionId);
            await Reauth(async()=>{await networkA.GetAccess(access401.Cookie);});
            Check(!(await State(access401.Tokens)).Has("sealed_tokens"),"Identity Session 401 from fast-path access terminalizes the matching BFF session");
            httpMode="random-kill";
            var fuzzSeed=RandomNumberGenerator.GetInt32(int.MaxValue);var fuzzRandom=new Random(fuzzSeed);
            Console.WriteLine("BFF RANDOM KILL SEED: "+fuzzSeed);
            for(var n=0;n<6;n++)
            {
                var killDelay=fuzzRandom.Next(5,160);
                var fuzz=await Fixture();advance(TimeSpan.FromMinutes(13));var beforeFuzz=httpCalls;
                await using(var victim=await BffProcessPeer.Peer.Start(Config(fuzz.Cookie) with {HoldAfterDone=true}))
                {
                    await victim.Go();await Task.Delay(killDelay);await victim.Kill();
                }
                using(var settled=new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    while(Volatile.Read(ref httpActive)!=0)await Task.Delay(10,settled.Token);
                await using var survivor=await BffProcessPeer.Peer.Start(Config(fuzz.Cookie));await survivor.Go();var outcome=await survivor.Line();
                var fuzzState=await State(fuzz.Tokens);
                var marker=(await sql.One("SELECT attempt_id FROM bff_refresh_journal WHERE session_id=@id",("id",fuzzState.Get<Guid>("id"))))!;
                var reuse=(await sql.One("SELECT count(*) AS n FROM auth_refresh_security_events WHERE session_id=@id AND type='RefreshTokenReuseDetected'",("id",fuzz.Tokens.SessionId)))!.Get<long>("n");
                Check(httpCalls<=beforeFuzz+1 && await Generation(fuzz.Tokens)<=1 && reuse==0
                    && (outcome.StartsWith("DONE:",StringComparison.Ordinal) && !marker.Has("attempt_id") || outcome=="ERROR:REAUTHENTICATION_REQUIRED" && !fuzzState.Has("sealed_tokens")),
                    "random real process kill trial "+n+" delay "+killDelay+"ms seed "+fuzzSeed+" preserves no-reuse and single-rotation invariants");
            }
            await host.StopAsync();
        }
        var closeFixture=await Fixture();var closeCore=Core(storeA,new Transport(auth,client));await closeCore.Close(closeFixture.Cookie);
        await Reauth(async()=>{await Core(storeB,new Transport(auth,client)).GetAccess(closeFixture.Cookie);});
        Check((await State(closeFixture.Tokens)).Has("sealed_tokens")==false,"local cookie closure erases tokens and denies subsequent cached reads");
        var summary=await storeA.UncertaintySummary(clock.GetUtcNow().AddDays(-7));
        Check(summary.Dispatched>summary.Uncertain && summary.Uncertain>=7,"uncertainty ratio counts distinct dispatched sessions rather than repeated attempts");
        return count;
    }
    private sealed class Probe(RefreshStage chosen,Func<RefreshStage,CancellationToken,Task> callback):IRefreshProbe
    {
        public Task Observe(RefreshStage stage,CancellationToken cancellation)=>stage==chosen?callback(stage,cancellation):Task.CompletedTask;
    }
    // Real Identity service/transactions; injected transport faults are not a network deployment test.
    private sealed class Transport(AuthenticationService identity,string client):IIdentityRefresh
    {
        private int calls;public int Calls=>calls;public bool Pause {get;init;}public string Mode {get;init;}="";
        public ConcurrentQueue<bool> Foregrounds {get;}=new();
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SessionTokens> Refresh(string token,bool foreground,string subject,CancellationToken cancellation)
        {
            Interlocked.Increment(ref calls);Foregrounds.Enqueue(foreground);Entered.TrySetResult();
            if(Pause)await Release.Task.WaitAsync(cancellation);
            if(Mode=="timeout")await Task.Delay(1000,cancellation);
            var result=await identity.Refresh(new(token,foreground),client);
            if(Mode=="lost")throw new IOException("Injected lost response");
            return Mode=="malformed"?result with {SessionId=Guid.Empty}:result;
        }
    }
}
