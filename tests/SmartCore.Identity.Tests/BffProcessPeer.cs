using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SmartCore.Identity;
using SmartCore.Identity.Bff;

// Test-only stdin IPC. Credentials are never command-line arguments or output.
internal sealed record BffPeerConfig(string Connection,string MacKey,string MaterialKey,string Origin,string Key,string Subject,DateTimeOffset Now,string Cookie,RefreshStage? Pause);
internal static class BffProcessPeer
{
    public static async Task Run()
    {
        var config=JsonSerializer.Deserialize<BffPeerConfig>((await Console.In.ReadLineAsync())!)!;
        await using var store=new BffStore(config.Connection);await store.Migrate();
        using var http=new IdentityRefreshHttp(new Uri(config.Origin),config.Key,config.Subject,true);
        var core=new BffRefresh(store,new Secrets(config.MacKey,config.MaterialKey),new FixedClock(config.Now),http,probe:new PauseProbe(config.Pause));
        Console.WriteLine("READY");
        if(await Console.In.ReadLineAsync()!="GO")throw new Exception("Missing peer start signal");
        try
        {
            var results=await Task.WhenAll(Enumerable.Range(0,10).Select(_=>core.RefreshIfDue(config.Cookie,BffActivity.UserAction)));
            Console.WriteLine("DONE:"+string.Join(',',results.Select(x=>x.Version)));
        }
        catch(BffError error){Console.WriteLine("ERROR:"+error.Code);}
    }
    private sealed class FixedClock(DateTimeOffset now):TimeProvider {public override DateTimeOffset GetUtcNow()=>now;}
    private sealed class PauseProbe(RefreshStage? pause):IRefreshProbe
    {
        public async Task Observe(RefreshStage stage,CancellationToken cancellation)
        {
            if(stage!=pause)return;
            Console.WriteLine("PAUSED:"+stage);
            // Ignore cooperative cancellation: parent must actually kill the process.
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
    }
    internal sealed class Peer: IAsyncDisposable
    {
        private readonly Process process;
        private readonly Task<string> stderr;
        private Peer(Process process){this.process=process;stderr=process.StandardError.ReadToEndAsync();}
        public static async Task<Peer> Start(BffPeerConfig config)
        {
            var start=new ProcessStartInfo("dotnet"){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--bff-process-peer");
            var peer=new Peer(Process.Start(start)??throw new Exception("Could not start BFF peer"));
            try
            {
                await peer.process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(config));await peer.process.StandardInput.FlushAsync();
                if(await peer.Line()!="READY")throw new Exception("Peer did not become ready");
                return peer;
            }
            catch {await peer.DisposeAsync();throw;}
        }
        public async Task Go(){await process.StandardInput.WriteLineAsync("GO");await process.StandardInput.FlushAsync();}
        public async Task<string> Line()=>await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30))??throw new Exception("Peer exited before result");
        public async Task Kill()
        {
            process.Kill(entireProcessTree:true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if(process.ExitCode==0)throw new Exception("Peer was not forcibly killed");
        }
        public async ValueTask DisposeAsync()
        {
            // DONE may reach the reader just before the successful process exits.
            // Cleanup accepts normal completion; explicit Kill() above still requires
            // abnormal termination of the peer paused at a crash boundary.
            if(!process.HasExited)
            {
                try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1));}
                catch(TimeoutException)
                {process.Kill(entireProcessTree:true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));}
            }
            await stderr;process.Dispose();
        }
    }
}
