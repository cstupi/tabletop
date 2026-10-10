using Dapper;
using Npgsql;

namespace Tabletop.Management.Tests;

public class MigrationTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public MigrationTests(ManagementFactory f) { _f = f; _f.CreateClient(); }

    [Fact]
    public async Task Schema_has_every_table_the_upload_columns_and_the_starter_column()
    {
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        var tables = await c.ExecuteScalarAsync<int>("select count(*) from information_schema.tables where table_schema in ('management','game')");
        Assert.Equal(35, tables);
        var cols = (await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='blob'")).ToList();
        Assert.Contains("uploaded", cols);
        Assert.Contains("content_type", cols);
        Assert.Contains("starter_seeded_at", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='account'"));
        Assert.Contains("seeded_at", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='properties'"));
        Assert.Contains("army_book", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='model_slot'"));
        Assert.Contains("target_terrain", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='effect'"));
        Assert.Contains("played", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='catalog_entry'"));
        Assert.Contains("target_line_of_sight", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='catalog_entry'"));
        Assert.Contains("amount_dice", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='effect'"));
    }

    [Fact]
    public void Running_the_migrator_twice_is_a_no_op() => Persistence.Migrator.Run(_f.Database.ConnectionString);
}
