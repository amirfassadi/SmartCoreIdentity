using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartCore.Identity.Bff;

// Register as a hosted service in the trusted BFF host. No cookie or browser input required.
public sealed class BffMaintenance(BffRefresh refresh,BffCircuit circuit,ILogger<BffMaintenance> logger):BackgroundService
{
    public async Task Tick(CancellationToken cancellation=default)
    {
        await circuit.Prune(cancellation);
        await refresh.SweepLogout(cancellation);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);budget.CancelAfter(TimeSpan.FromSeconds(90));
            try{await Tick(budget.Token);}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("BFF maintenance tick did not complete; durable logout intent will be retried.");}
        }while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
