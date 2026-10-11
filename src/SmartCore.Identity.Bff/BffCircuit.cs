using System.Diagnostics.Metrics;
using SmartCore.Identity;

namespace SmartCore.Identity.Bff;

// One Identity client/deployment per shared BFF store. Latched until operator reset.
public sealed class BffCircuit(BffStore store,TimeProvider clock)
{
    private static readonly Meter Meter=new("SmartCore.Identity.Bff.Circuit");
    private static readonly Counter<long> Opened=Meter.CreateCounter<long>("bff.refresh.circuit_opened");
    public async Task<bool> IsOpen(CancellationToken cancellation=default)
    {
        await using var c=await store.Source.OpenConnectionAsync(cancellation);
        return (await BffStore.One(c,"SELECT opened_at FROM bff_refresh_circuit WHERE id=1",cancellation))!.Has("opened_at");
    }
    public async Task<bool> Record(Guid session,int status,bool configuration,CancellationToken cancellation)
    {
        await using var c=await store.Source.OpenConnectionAsync(cancellation);await using var tx=await c.BeginTransactionAsync(cancellation);
        var state=(await BffStore.One(c,"SELECT opened_at FROM bff_refresh_circuit WHERE id=1 FOR UPDATE",cancellation))!;
        if(state.Has("opened_at")){await tx.CommitAsync(cancellation);return true;}
        var now=Timestamps.Now(clock);
        await BffStore.Execute(c,"DELETE FROM bff_refresh_samples WHERE observed_at<@since",cancellation,("since",now.AddSeconds(-30)));
        await BffStore.Execute(c,"INSERT INTO bff_refresh_samples VALUES(@id,@now,@status) ON CONFLICT(session_id) DO UPDATE SET observed_at=@now,status=@status",cancellation,("id",session),("now",now),("status",status));
        var samples=(await BffStore.One(c,"SELECT count(*) AS n,count(*) FILTER(WHERE status IN (400,401,403)) AS failures FROM bff_refresh_samples",cancellation))!;
        var open=configuration || samples.Get<long>("failures")>=5 && samples.Get<long>("failures")*2>=samples.Get<long>("n");
        if(open)
        {
            await BffStore.Execute(c,"UPDATE bff_refresh_circuit SET opened_at=@now,reason=@reason WHERE id=1",cancellation,("now",now),("reason",configuration?"BffCredentialsRejected":"CrossSessionRejections"));
            Opened.Add(1,new KeyValuePair<string,object?>("reason",configuration?"configuration":"rejections"));
        }
        await tx.CommitAsync(cancellation);return open;
    }
    // Trusted operator host only; does not erase dispatch evidence or authorize token retry.
    public async Task Reset(CancellationToken cancellation=default)
    {
        await using var c=await store.Source.OpenConnectionAsync(cancellation);await using var tx=await c.BeginTransactionAsync(cancellation);
        await BffStore.One(c,"SELECT id FROM bff_refresh_circuit WHERE id=1 FOR UPDATE",cancellation);
        await BffStore.Execute(c,"UPDATE bff_refresh_circuit SET opened_at=NULL,reason=NULL WHERE id=1",cancellation);
        await BffStore.Execute(c,"DELETE FROM bff_refresh_samples",cancellation);await tx.CommitAsync(cancellation);
    }
}
