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
        Assert.Equal(38, tables);
        Assert.DoesNotContain("shared_at", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='asset'"));
        Assert.Equal(["share_block", "share_offer", "share_offer_asset"],
            await c.QueryAsync<string>("select table_name from information_schema.tables where table_schema='management' and table_name like 'share%' order by table_name"));
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
        Assert.Contains("target_requires", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='catalog_entry'"));
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
    public async Task Tokens_are_removed_from_tables_and_logs_and_the_schema_refuses_them()
    {
        await using var c = new NpgsqlConnection(_db.ConnectionString);
        var (account, table, objective, token) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await c.ExecuteAsync("insert into management.account (id, name) values (@account, 'token test')", new { account });
        await c.ExecuteAsync("insert into management.table_state (id, owner_id, name, width, length, texture_builtin) values (@table, @account, 'T', 72, 48, 'grass')", new { table, account });
        const string Marker = "insert into management.marker (id, table_state_id, kind, pos_x, pos_y, diameter, visual_builtin) values (@id, @table, @kind, 1, 1, 1, 'chip')";
        var refused = await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync(Marker, new { id = token, table, kind = "token" }));
        Assert.Equal("marker_kind_check", refused.ConstraintName);

        // Data written before 022: a token row, and a game log holding a token dropped, moved and removed in play.
        await c.ExecuteAsync("alter table management.marker drop constraint marker_kind_check; alter table management.marker add constraint marker_kind_check check (kind in ('objective', 'token'))");
        await c.ExecuteAsync(Marker, new { id = objective, table, kind = "objective" });
        await c.ExecuteAsync(Marker, new { id = token, table, kind = "token" });
        var (game, played, setup) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        const string Event = "insert into game.game_event (game_id, seq, kind, payload) values (@game, @seq, @kind, @payload::jsonb)";
        string Payload(long seq, string body) => $"{{\"seq\": {seq}, \"actor\": null, \"payload\": {body}}}";
        foreach (var (seq, kind, body) in new (long, string, string)[] {
            (1, "marker_placed", $$$"""{"kind": "marker_placed", "marker": {"id": "{{{played}}}", "kind": "token"}}"""),
            (2, "marker_moved", $$$"""{"kind": "marker_moved", "markerId": "{{{played}}}"}"""),
            (3, "marker_moved", $$$"""{"kind": "marker_moved", "markerId": "{{{setup}}}"}"""),
            (4, "marker_removed", $$$"""{"kind": "marker_removed", "markerId": "{{{played}}}"}"""),
            (5, "score_set", """{"kind": "score_set"}""") })
            await c.ExecuteAsync(Event, new { game, seq, kind, payload = Payload(seq, body) });

        var script = typeof(Persistence.Migrator).Assembly.GetManifestResourceStream("Tabletop.Management.Persistence.Migrations.022_remove_tokens.sql")!;
        await c.ExecuteAsync(await new StreamReader(script).ReadToEndAsync());

        Assert.Equal([objective], await c.QueryAsync<Guid>("select id from management.marker where table_state_id = @table", new { table }));
        Assert.Equal([3L, 5L], await c.QueryAsync<long>("select seq from game.game_event where game_id = @game order by seq", new { game }));
        Assert.Equal("marker_kind_check", (await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync(Marker, new { id = Guid.CreateVersion7(), table, kind = "token" }))).ConstraintName);
        await c.ExecuteAsync("delete from game.game_event where game_id = @game; delete from management.account where id = @account", new { game, account });
    }

    [Fact]
    public async Task Army_edition_is_checked_and_the_backfill_marks_3_6_lists_by_their_rule_names()
    {
        await using var c = new NpgsqlConnection(_db.ConnectionString);
        var owner = Guid.CreateVersion7();
        await c.ExecuteAsync("insert into management.account (id, name) values (@owner, 'edition test')", new { owner });
        var a35 = Guid.CreateVersion7(); var a36 = Guid.CreateVersion7();
        await c.ExecuteAsync("insert into management.army (id, owner_id, name, game_system, source, edition) values (@a35, @owner, 'old', 'grimdark-future', 's', '3.5'), (@a36, @owner, 'new', 'grimdark-future', 's', '3.5')", new { a35, a36, owner });
        var unit = Guid.CreateVersion7();
        await c.ExecuteAsync("insert into management.unit (id, army_id, ordinal, name) values (@unit, @a36, 0, 'u'); insert into management.unit_property (unit_id, name) values (@unit, 'Bravery Aura')", new { unit, a36 });

        var script = typeof(Persistence.Migrator).Assembly.GetManifestResourceStream("Tabletop.Management.Persistence.Migrations.023_army_edition.sql")!;
        await c.ExecuteAsync(await new StreamReader(script).ReadToEndAsync());

        Assert.Equal("3.5", await c.ExecuteScalarAsync<string>("select edition from management.army where id=@a35", new { a35 }));
        Assert.Equal("3.6", await c.ExecuteScalarAsync<string>("select edition from management.army where id=@a36", new { a36 }));
        var e = await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync("insert into management.army (id, owner_id, name, game_system, source, edition) values (@id, @owner, 'x', 'grimdark-future', 's', '3.7')", new { id = Guid.CreateVersion7(), owner }));
        Assert.Equal("23514", e.SqlState);
        await c.ExecuteAsync("delete from management.account where id = @owner", new { owner });
    }

    [Fact]
    public void Running_the_migrator_twice_is_a_no_op() => Persistence.Migrator.Run(_db.ConnectionString);
}
