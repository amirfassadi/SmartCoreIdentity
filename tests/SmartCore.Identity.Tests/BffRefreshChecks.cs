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
        var aConfig=new NpgsqlConnectionStringBuilder(connection){ApplicationName="bff-a-test"};
        var bConfig=new NpgsqlConnectionStringBuilder(connection){ApplicationName="bff-b-test"};
        await using var storeA=new BffStore(aConfig.ConnectionString);await using var storeB=new BffStore(bConfig.ConnectionString);
        await storeA.Migrate();await storeB.Migrate();
        await using var sql=await db.Source.OpenConnectionAsync();
        await sql.Execute("TRUNCATE bff_sessions CASCADE");
        var secrets=new Secrets(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
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
        var httpCalls=0;var trapCalls=0;var httpMode="normal";var httpKey=Secrets.Token();var httpSubject=Secrets.Token();
        var httpBuilder=WebApplication.CreateSlimBuilder(new WebApplicationOptions {EnvironmentName="Development"});
        httpBuilder.Logging.ClearProviders();httpBuilder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,0));
        await using(var host=httpBuilder.Build())
        {
            host.MapPost("/auth/refresh",async(HttpContext context)=>
            {
                Interlocked.Increment(ref httpCalls);
                if(context.Request.Headers["X-Bff-Client-Key"]!=httpKey || context.Request.Headers["X-Bff-Subject"]!=httpSubject)
                {context.Response.StatusCode=401;return;}
                if(httpMode=="redirect"){context.Response.StatusCode=307;context.Response.Headers.Location="/trap";return;}
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
            host.MapPost("/trap",()=>{Interlocked.Increment(ref trapCalls);return Results.NoContent();});
            await host.StartAsync();
            var address=host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http=new IdentityRefreshHttp(new Uri(address+"/"),httpKey,httpSubject,developmentLoopback:true);
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
        public async Task<SessionTokens> Refresh(string token,bool foreground,CancellationToken cancellation)
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
