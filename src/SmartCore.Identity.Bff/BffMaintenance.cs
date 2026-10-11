using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartCore.Identity.Bff;

// Register as a hosted service in the trusted BFF host. No cookie or browser input required.
public sealed class BffMaintenance(BffRefresh refresh,BffCircuit circuit,ILogger<BffMaintenance> logger):BackgroundService
{
    private static readonly Meter Meter=new("SmartCore.Identity.Bff.Maintenance");
    private static readonly Counter<long> Failures=Meter.CreateCounter<long>("bff.maintenance.failures");
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
            catch(Exception failure)
            {
                var type=failure.GetType().FullName??failure.GetType().Name;
                Failures.Add(1,new KeyValuePair<string,object?>("exception_type",type));
                // Type is useful operational evidence; message/stack can contain transport secrets.
                logger.LogWarning("BFF maintenance tick failed ({ExceptionType}); durable logout intent will be retried.",type);
            }
        }while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
