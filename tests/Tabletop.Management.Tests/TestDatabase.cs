using Dapper;
using Npgsql;

namespace Tabletop.Management.Tests;

/// <summary>A database of its own per factory, on the Docker Postgres from .env. Dropped on dispose.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _admin;
    // The full 32 hex chars, not a prefix: a UUIDv7's leading 48 bits are a millisecond timestamp, so
    // two factories built within the same millisecond (xunit constructs fixtures for different test
    // classes back to back) would otherwise get the same truncated name and collide on `create database`.
    public string Name { get; } = "tabletop_test_" + Guid.CreateVersion7().ToString("N");
    public string ConnectionString { get; }

    public TestDatabase()
    {
        Configuration.DotEnv.Load();
        var dev = Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION") ?? throw new InvalidOperationException("TABLETOP_DB_CONNECTION not set; copy .env.example to .env");
        _admin = new NpgsqlConnectionStringBuilder(dev) { Database = "postgres" }.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(dev) { Database = Name }.ConnectionString;
        using var c = new NpgsqlConnection(_admin);
        c.Execute($"create database \"{Name}\"");
        Persistence.Migrator.Run(ConnectionString);
    }

    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        using var c = new NpgsqlConnection(_admin);
        c.Execute($"drop database if exists \"{Name}\" with (force)");
    }
}
