using Npgsql;
using SmartCore.Identity;

namespace SmartCore.Identity.Bff;

public sealed class BffStore(string connectionString):IAsyncDisposable
{
    public NpgsqlDataSource Source {get;}=NpgsqlDataSource.Create(connectionString);
    public async Task Migrate(CancellationToken cancellation=default)
    {
        await using var c=await Source.OpenConnectionAsync(cancellation);
        RequireServerVersion(c.PostgreSqlVersion);
        await c.Execute("SELECT pg_advisory_lock(640902)");
        try
        {
            var present=(await c.One("SELECT to_regclass('bff_schema_versions') IS NOT NULL AS present"))!.Get<bool>("present");
            if(!present) await c.Execute(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"bff-database","001_bff_sessions.sql"),cancellation));
            var updated=(await c.One("SELECT EXISTS(SELECT 1 FROM bff_schema_versions WHERE version=2) AS present"))!.Get<bool>("present");
            if(!updated)await c.Execute(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"bff-database","002_refresh_admission.sql"),cancellation));
        }
        finally {await c.Execute("SELECT pg_advisory_unlock(640902)");}
    }
    public static void RequireServerVersion(Version version)
    {if(version.Major<17)throw new InvalidOperationException("BFF requires PostgreSQL 17 or later for transaction_timeout; run startup migration before accepting requests.");}
    internal static async Task<Row?> One(NpgsqlConnection c,string query,CancellationToken cancellation,params (string,object?)[] args)
    {
        await using var command=new NpgsqlCommand(query,c);
        foreach(var (key,value) in args) command.Parameters.AddWithValue(key,value??DBNull.Value);
        await using var reader=await command.ExecuteReaderAsync(cancellation);
        if(!await reader.ReadAsync(cancellation))return null;
        var fields=new Dictionary<string,object?>();
        for(var i=0;i<reader.FieldCount;i++)fields[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);
        return new(fields);
    }
    internal static async Task<int> Execute(NpgsqlConnection c,string query,CancellationToken cancellation,params (string,object?)[] args)
    {
        await using var command=new NpgsqlCommand(query,c);
        foreach(var (key,value) in args)command.Parameters.AddWithValue(key,value??DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellation);
    }
    // Counts distinct BFF session rows, not attempts; aggregate before any future purge.
    public async Task<(long Dispatched,long Uncertain)> UncertaintySummary(DateTimeOffset since,CancellationToken cancellation=default)
    {
        await using var c=await Source.OpenConnectionAsync(cancellation);
        var r=(await One(c,"""
            SELECT count(*) AS dispatched,count(*) FILTER(WHERE s.uncertain_at>=@since) AS uncertain
            FROM bff_sessions s JOIN bff_refresh_journal j ON j.session_id=s.id WHERE j.last_dispatched_at>=@since
            """,cancellation,("since",since)))!;
        return(r.Get<long>("dispatched"),r.Get<long>("uncertain"));
    }
    public ValueTask DisposeAsync()=>Source.DisposeAsync();
}
