using System.Diagnostics.Metrics;
using SmartCore.Identity;

namespace SmartCore.Identity.Bff;

// One Identity client/deployment per shared BFF store; one recovery probe per minute.
public sealed class BffCircuit(BffStore store,TimeProvider clock)
{
    private static readonly Meter Meter=new("SmartCore.Identity.Bff.Circuit");
    private static readonly Counter<long> Opened=Meter.CreateCounter<long>("bff.refresh.circuit_opened");
    public async Task<bool> IsOpen(CancellationToken cancellation=default)
    {
        await using var c=await store.Source.OpenConnectionAsync(cancellation);
        return (await BffStore.One(c,"SELECT opened_at FROM bff_refresh_circuit WHERE id=1",cancellation))!.Has("opened_at");
    }
    // The session row remains owned by the caller. Probe permission never clears dispatch evidence.
    public async Task<bool> Allow(Guid session,CancellationToken cancellation=default)
    {
        await using var c=await store.Source.OpenConnectionAsync(cancellation);
        if(!(await OneState(c,cancellation)).Has("opened_at"))return true;
        return await BffStore.Execute(c,"""
            UPDATE bff_refresh_circuit SET probe_session=@session,next_probe_at=@next
            WHERE id=1 AND opened_at IS NOT NULL AND next_probe_at<=@now
            """,cancellation,("session",session),("next",Timestamps.Now(clock).AddSeconds(60)),("now",Timestamps.Now(clock)))==1;
    }
    private static async Task<Row> OneState(Npgsql.NpgsqlConnection c,CancellationToken cancellation)=>
        (await BffStore.One(c,"SELECT opened_at FROM bff_refresh_circuit WHERE id=1",cancellation))!;
    public async Task<bool> Record(Guid session,int status,bool configuration,CancellationToken cancellation)
    {
        await using var c=await store.Source.OpenConnectionAsync(cancellation);
        var now=Timestamps.Now(clock);
        if(status==200)
        {
            // Normal successes never acquire the singleton row lock. Only the elected probe can close.
            if((await OneState(c,cancellation)).Has("opened_at"))
                await BffStore.Execute(c,"UPDATE bff_refresh_circuit SET opened_at=NULL,reason=NULL,next_probe_at=NULL,probe_session=NULL WHERE id=1 AND probe_session=@session",cancellation,("session",session));
            await Sample(c,session,status,now,cancellation);return false;
        }
        await using var tx=await c.BeginTransactionAsync(cancellation);
        var state=(await BffStore.One(c,"SELECT opened_at FROM bff_refresh_circuit WHERE id=1 FOR UPDATE",cancellation))!;
        if(state.Has("opened_at")){await tx.CommitAsync(cancellation);return true;}
        await BffStore.Execute(c,"DELETE FROM bff_refresh_samples WHERE observed_at<@since",cancellation,("since",now.AddSeconds(-30)));
        await Sample(c,session,status,now,cancellation);
        var samples=(await BffStore.One(c,"SELECT count(*) AS n,count(*) FILTER(WHERE status IN (400,401,403)) AS failures,count(*) FILTER(WHERE status=403) AS forbidden FROM bff_refresh_samples WHERE observed_at>=@since",cancellation,("since",now.AddSeconds(-30))))!;
        var open=configuration || samples.Get<long>("forbidden")>=3 || samples.Get<long>("failures")>=5 && samples.Get<long>("failures")*2>=samples.Get<long>("n");
        if(open)
        {
            await BffStore.Execute(c,"UPDATE bff_refresh_circuit SET opened_at=@now,reason=@reason,next_probe_at=@next,probe_session=NULL WHERE id=1",cancellation,("now",now),("next",now.AddSeconds(60)),("reason",configuration?"BffCredentialsRejected":"CrossSessionRejections"));
            Opened.Add(1,new KeyValuePair<string,object?>("reason",configuration?"configuration":"rejections"));
        }
        await tx.CommitAsync(cancellation);return open;
    }
    private static Task<int> Sample(Npgsql.NpgsqlConnection c,Guid session,int status,DateTimeOffset now,CancellationToken cancellation)=>
        BffStore.Execute(c,"INSERT INTO bff_refresh_samples VALUES(@id,@now,@status) ON CONFLICT(session_id) DO UPDATE SET observed_at=@now,status=@status",cancellation,("id",session),("now",now),("status",status));
    // Trusted operator host only; does not erase dispatch evidence or authorize token retry.
    public async Task Reset(CancellationToken cancellation=default)
    {
        await using var c=await store.Source.OpenConnectionAsync(cancellation);await using var tx=await c.BeginTransactionAsync(cancellation);
        await BffStore.One(c,"SELECT id FROM bff_refresh_circuit WHERE id=1 FOR UPDATE",cancellation);
        await BffStore.Execute(c,"UPDATE bff_refresh_circuit SET opened_at=NULL,reason=NULL,next_probe_at=NULL,probe_session=NULL WHERE id=1",cancellation);
        await BffStore.Execute(c,"DELETE FROM bff_refresh_samples",cancellation);await tx.CommitAsync(cancellation);
    }
}
