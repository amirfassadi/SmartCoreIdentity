using Npgsql;

namespace SmartCore.Identity;

public sealed class Database(string connectionString) : IAsyncDisposable
{
    public NpgsqlDataSource Source { get; } = NpgsqlDataSource.Create(connectionString);
    public async Task Migrate()
    {
        await using var connection = await Source.OpenConnectionAsync();
        // Advisory lock serializes migration runners; script owns its transaction.
        await connection.Execute("SELECT pg_advisory_lock(640901)");
        try { await connection.Execute(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database/001_registration.sql"))); }
        finally { await connection.Execute("SELECT pg_advisory_unlock(640901)"); }
    }
    public ValueTask DisposeAsync() => Source.DisposeAsync();
}

public sealed class Row(Dictionary<string, object?> fields)
{
    public T Get<T>(string name) => (T)fields[name]!;
    public T? Optional<T>(string name) => fields[name] is null ? default : (T)fields[name]!;
    public DateTimeOffset Time(string name) => new(Get<DateTime>(name));
    public bool Has(string name) => fields[name] is not null;
}

public static class Sql
{
    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, (string, object?)[] args)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    public static async Task<int> Execute(this NpgsqlConnection connection, string sql, params (string, object?)[] args)
    {
        await using var command = Command(connection, sql, args);
        return await command.ExecuteNonQueryAsync();
    }
    public static async Task<List<Row>> Rows(this NpgsqlConnection connection, string sql, params (string, object?)[] args)
    {
        await using var command = Command(connection, sql, args);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Row>();
        while (await reader.ReadAsync())
        {
            var values = new Dictionary<string, object?>();
            for (var i=0; i<reader.FieldCount; i++) values[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(new Row(values));
        }
        return rows;
    }
    public static async Task<Row?> One(this NpgsqlConnection connection, string sql, params (string, object?)[] args)
        => (await connection.Rows(sql,args)).SingleOrDefault();
}
