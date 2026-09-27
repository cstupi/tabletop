using Npgsql;

namespace Tabletop.Management.Persistence;

/// <summary>One data source for the process. search_path covers both schemas so SQL names tables bare.</summary>
public sealed class Database : IAsyncDisposable
{
    public Database(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = "management,game,public" };
        Source = new NpgsqlDataSourceBuilder(b.ConnectionString).Build();
    }

    public NpgsqlDataSource Source { get; }
    public Task<NpgsqlConnection> Open(CancellationToken ct = default) => Source.OpenConnectionAsync(ct).AsTask();
    public ValueTask DisposeAsync() => Source.DisposeAsync();
}
