using Dapper;
using DbUp;
using Npgsql;

namespace Tabletop.Management.Persistence;

public static class Migrator
{
    private const string Journal = "schema_version";
    private const string FirstScript = "Tabletop.Management.Persistence.Migrations.001_schema.sql";

    public static void Run(string connectionString)
    {
        // DbUp-postgresql's own schema-existence probe breaks (a Postgres syntax error) when the
        // connection string carries a multi-entry Search Path, as TABLETOP_DB_CONNECTION does; every
        // script here names its schema explicitly (001's own `set search_path to management;`, 002's
        // `management.blob`), so the migrator never needs the connection's search_path.
        var cs = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = null }.ConnectionString;
        Baseline(cs);
        var result = DeployChanges.To.PostgresqlDatabase(cs)
            .WithScriptsEmbeddedInAssembly(typeof(Migrator).Assembly, s => s.EndsWith(".sql"))
            .JournalToPostgresqlTable("public", Journal)
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build()
            .PerformUpgrade();
        if (!result.Successful) throw new InvalidOperationException("Migration failed", result.Error);
    }

    /// <summary>A database created by docker compose's init SQL already holds 001; record it instead of re-running it.</summary>
    private static void Baseline(string connectionString)
    {
        using var c = new NpgsqlConnection(connectionString);
        var hasSchema = c.ExecuteScalar<bool>("select exists(select 1 from information_schema.tables where table_schema='management' and table_name='account')");
        var hasJournal = c.ExecuteScalar<bool>($"select exists(select 1 from information_schema.tables where table_schema='public' and table_name='{Journal}')");
        if (!hasSchema || hasJournal) return;
        c.Execute($"create table public.{Journal} (schemaversionsid serial primary key, scriptname varchar(255) not null, applied timestamp not null)");
        c.Execute($"insert into public.{Journal} (scriptname, applied) values (@name, now())", new { name = FirstScript });
    }
}
