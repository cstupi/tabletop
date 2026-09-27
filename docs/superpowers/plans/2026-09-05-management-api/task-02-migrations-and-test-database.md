# Task 2: Migrations, the data source and the test database

**Files:**
- Create: `server/management/Persistence/Migrator.cs`, `server/management/Persistence/Database.cs`, `server/management/Persistence/Migrations/001_schema.sql` (byte-for-byte copy of `scripts/postgres/init/001_schema.sql`), `server/management/Persistence/Migrations/002_blob_uploaded.sql`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/TestDatabase.cs`, `tests/Tabletop.Management.Tests/MigrationTests.cs`
- Modify: `tests/Tabletop.Management.Tests/ManagementFactory.cs`

**Interfaces:**
- Consumes: `scripts/postgres/init/001_schema.sql` (contracts plan Task 15); `TABLETOP_DB_CONNECTION`.
- Produces: `Migrator.Run(string connectionString)` (idempotent; baselines a database the compose init already created); `Database` with `Task<NpgsqlConnection> Open(CancellationToken)` and `NpgsqlDataSource Source`; `TestDatabase` (one fresh database per factory, migrated, dropped on dispose); every later repository takes `Database`.
- Decision — **Dapper over Npgsql with hand-written SQL**, no ORM: the schema is normalised and hand-written already, the army tree and table state are loaded whole with a handful of queries, and every constraint lives in the database rather than in mapping attributes (`schema.md` → Normalised, not documents). Rows are `sealed record`s mapped by constructor, so SQL aliases columns to the parameter names (`owner_id as OwnerId`). **DbUp** applies embedded `NNN_*.sql` scripts once each, journalled in `public.schema_version`; the compose init SQL stays for a bare `docker compose up`, and the migrator recognises that database and records 001 as applied (contracts plan → Local development: "the management API plan adopts it as migration 001").
- `002` adds `blob.uploaded boolean` (false between `POST /v1/assets` and `/confirm`; a manifest and a download skip unuploaded blobs) and `blob.content_type text` (`model/gltf-binary`, `image/png`, `image/jpeg`, `image/webp`) — both columns are in `docs/schema.md` → `blob`; WebP is ruling 8.

Spec: `schema.md` in full; management API → Asset bytes never pass through the API (`confirm` is a distinct step, so the row exists before the bytes).

- [ ] **Step 1: Write the failing test**

```csharp
using Dapper;
using Npgsql;

namespace Tabletop.Management.Tests;

public class MigrationTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public MigrationTests(ManagementFactory f) { _f = f; _f.CreateClient(); }

    [Fact]
    public async Task Schema_has_every_table_and_the_upload_columns()
    {
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        var tables = await c.ExecuteScalarAsync<int>("select count(*) from information_schema.tables where table_schema in ('management','game')");
        Assert.Equal(33, tables);
        var cols = (await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='blob'")).ToList();
        Assert.Contains("uploaded", cols);
        Assert.Contains("content_type", cols);
    }

    [Fact]
    public void Running_the_migrator_twice_is_a_no_op() => Persistence.Migrator.Run(_f.Database.ConnectionString);
}
```

- [ ] **Step 2: Write `TestDatabase.cs` and extend the factory**

```csharp
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
```

In `ManagementFactory`: add `public TestDatabase Database { get; } = new();`, add `["DB_CONNECTION"] = Database.ConnectionString` to the in-memory configuration, and override `Dispose(bool disposing)` to call `Database.Dispose()` after `base.Dispose(disposing)`.

- [ ] **Step 3: Run; expect compile errors (`Migrator`, `Database` undefined).**

- [ ] **Step 4: Write `Migrator.cs`, `Database.cs`, the two scripts**

```csharp
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
```

```csharp
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
```

```sql
-- 002_blob_uploaded.sql: the upload state between POST /v1/assets and /confirm, and what the bytes are.
alter table management.blob
    add column uploaded     boolean not null default false,
    add column content_type text    not null default 'model/gltf-binary'
        check (content_type in ('model/gltf-binary', 'image/png', 'image/jpeg', 'image/webp'));
```

`001_schema.sql` is copied with `cp scripts/postgres/init/001_schema.sql server/management/Persistence/Migrations/001_schema.sql` and never edited afterwards — later changes are new scripts.

- [ ] **Step 5: Wire `Program.cs`** — the data source beside the options registration, before `Build()`:

```csharp
builder.Services.AddSingleton(sp => new Tabletop.Management.Persistence.Database(sp.GetRequiredService<ManagementOptions>().DbConnection));
```

and the migration after `Build()`, off the eagerly resolved options:

```csharp
var options = app.Services.GetRequiredService<ManagementOptions>();
Tabletop.Management.Persistence.Migrator.Run(options.DbConnection);
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Management.Tests` with the compose stack up; expect 3 passed. Confirm the dev database also migrates: `TABLETOP_DB_CONNECTION` from `.env` → `dotnet run --project server/management` logs no error and `psql -c '\dt public.*'` shows `schema_version` with two rows. Commit: `Add migrations, the data source and a per-run test database`.**
