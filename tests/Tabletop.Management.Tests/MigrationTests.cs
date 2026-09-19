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
