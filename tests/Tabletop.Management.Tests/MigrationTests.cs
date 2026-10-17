using Dapper;
using Npgsql;

namespace Tabletop.Management.Tests;

[Trait("Category", "E2E")]
public class MigrationTests : IClassFixture<TestDatabase>
{
    private readonly TestDatabase _db;
    public MigrationTests(TestDatabase db) => _db = db;

    [Fact]
    public async Task Schema_has_every_table_the_upload_columns_and_the_starter_column()
    {
        await using var c = new NpgsqlConnection(_db.ConnectionString);
        var tables = await c.ExecuteScalarAsync<int>("select count(*) from information_schema.tables where table_schema in ('management','game')");
        Assert.Equal(35, tables);
        var cols = (await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='blob'")).ToList();
        Assert.Contains("uploaded", cols);
        Assert.Contains("content_type", cols);
        Assert.Contains("created_at", cols);
        Assert.Contains("starter_seeded_at", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='account'"));
        Assert.Contains("seeded_at", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='properties'"));
        Assert.Contains("army_book", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='model_slot'"));
        Assert.Contains("target_terrain", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='effect'"));
        Assert.Contains("played", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='catalog_entry'"));
        Assert.Contains("target_line_of_sight", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='catalog_entry'"));
        Assert.Contains("amount_dice", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='effect'"));
    }

    [Fact]
    public async Task Names_past_the_api_limits_are_refused_by_the_schema_too()
    {
        await using var c = new NpgsqlConnection(_db.ConnectionString);
        var id = Guid.CreateVersion7();
        var e = await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync("insert into management.account (id, name) values (@id, @name)", new { id, name = new string('n', 65) }));
        Assert.Equal("account_name_length", e.ConstraintName);
        await c.ExecuteAsync("insert into management.account (id, name) values (@id, @name)", new { id, name = new string('n', 64) });
        await c.ExecuteAsync("delete from management.account where id = @id", new { id });
    }

    [Fact]
    public void Running_the_migrator_twice_is_a_no_op() => Persistence.Migrator.Run(_db.ConnectionString);
}
