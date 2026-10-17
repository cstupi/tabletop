using Dapper;
using Npgsql;

namespace Tabletop.Management.Tests;

/// <summary>E2E only: a migrated database of its own, on the Docker Postgres from .env. Dropped on dispose.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _admin;
    // The full 32 hex chars, not a prefix: a UUIDv7's leading 48 bits are a millisecond timestamp, so
    // two fixtures built within the same millisecond would otherwise get the same truncated name and collide on `create database`.
    public string Name { get; } = "tabletop_test_" + Guid.CreateVersion7().ToString("N");
    public string ConnectionString { get; }
    /// <summary>The repositories' data source on this database.</summary>
    public Persistence.Database Data { get; }

    /// <summary>The directory holding Tabletop.sln, where the dev .env lives.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tabletop.sln"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    public TestDatabase()
    {
        Configuration.DotEnv.Load(RepoRoot);
        var dev = Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION") ?? throw new InvalidOperationException("TABLETOP_DB_CONNECTION not set; copy .env.example to .env");
        _admin = new NpgsqlConnectionStringBuilder(dev) { Database = "postgres" }.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(dev) { Database = Name }.ConnectionString;
        using var c = new NpgsqlConnection(_admin);
        c.Execute($"create database \"{Name}\"");
        Persistence.Migrator.Run(ConnectionString);
        Data = new Persistence.Database(ConnectionString);
    }

    public void Dispose()
    {
        Data.DisposeAsync().AsTask().GetAwaiter().GetResult();
        NpgsqlConnection.ClearAllPools();
        using var c = new NpgsqlConnection(_admin);
        c.Execute($"drop database if exists \"{Name}\" with (force)");
    }
}
