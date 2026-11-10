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
        Assert.Equal(39, tables);
        Assert.DoesNotContain("shared_at", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='asset'"));
        Assert.Equal(["share_block", "share_offer", "share_offer_asset", "shared_copy"],
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
        Assert.Contains("steep", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='asset_volume'"));
        Assert.Contains("steep", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='terrain_volume'"));
        var floorChecks = (await c.QueryAsync<(string Name, string Def)>("select conname, pg_get_constraintdef(oid) from pg_constraint where conrelid = 'management.effect'::regclass and contype = 'c' and pg_get_constraintdef(oid) ilike '%floor%'")).ToList();
        var floorCheck = Assert.Single(floorChecks);
        Assert.Equal("effect_floor_check", floorCheck.Name);
        Assert.Contains("modify_parameter", floorCheck.Def);
    }

    [Fact]
    public async Task Usernames_are_unique_ignoring_case_and_a_shared_folder_is_one_top_level_folder()
    {
        await using var c = new NpgsqlConnection(_db.ConnectionString);
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await c.ExecuteAsync("insert into management.account (id, name, username) values (@a, 'Clay', 'Clay_S')", new { a });
        Assert.Equal("account_username_idx", (await Assert.ThrowsAsync<PostgresException>(() =>
            c.ExecuteAsync("insert into management.account (id, name, username) values (@b, 'Clay', 'clay_s')", new { b }))).ConstraintName);
        Assert.Equal("account_username_format", (await Assert.ThrowsAsync<PostgresException>(() =>
            c.ExecuteAsync("insert into management.account (id, name, username) values (@b, 'Clay', 'a b')", new { b }))).ConstraintName);
        await c.ExecuteAsync("insert into management.account (id, name) values (@b, 'Clay')", new { b });

        var (shared, sub) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await c.ExecuteAsync("insert into management.folder (id, owner_id, name, shared) values (@shared, @a, 'Shared', true)", new { shared, a });
        Assert.Equal("folder_shared_idx", (await Assert.ThrowsAsync<PostgresException>(() =>
            c.ExecuteAsync("insert into management.folder (id, owner_id, name, shared) values (@sub, @a, 'Second', true)", new { sub, a }))).ConstraintName);
        Assert.Equal("folder_shared_top_level", (await Assert.ThrowsAsync<PostgresException>(() =>
            c.ExecuteAsync("insert into management.folder (id, owner_id, parent_id, name, shared) values (@sub, @b, @shared, 'Below', true)", new { sub, b, shared }))).ConstraintName);
        await c.ExecuteAsync("delete from management.account where id = any(@ids)", new { ids = new[] { a, b } });
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
        var (a35, a36, slot36, weapon36) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        foreach (var (army, name) in new[] { (a35, "old"), (a36, "unit rule"), (slot36, "slot rule"), (weapon36, "weapon rule") })
            await c.ExecuteAsync("insert into management.army (id, owner_id, name, game_system, source, edition) values (@army, @owner, @name, 'grimdark-future', 's', '3.5')", new { army, owner, name });
        // A unit per army; each army's one rule name sits on the unit, a model slot or a weapon. Armor is in 3.5 books too.
        async Task<Guid> Unit(Guid army)
        {
            var unit = Guid.CreateVersion7();
            await c.ExecuteAsync("insert into management.unit (id, army_id, ordinal, name) values (@unit, @army, 0, 'u')", new { unit, army });
            return unit;
        }
        async Task<Guid> Slot(Guid unit)
        {
            var slot = Guid.CreateVersion7();
            await c.ExecuteAsync("insert into management.model_slot (id, unit_id, ordinal, name, base_shape, base_a, base_b, height) values (@slot, @unit, 0, 'm', 'circle', 1, 1, 1.5)", new { slot, unit });
            return slot;
        }
        await c.ExecuteAsync("insert into management.unit_property (unit_id, name) values (@unit, 'Armor')", new { unit = await Unit(a35) });
        await c.ExecuteAsync("insert into management.unit_property (unit_id, name) values (@unit, 'Bravery Aura')", new { unit = await Unit(a36) });
        var slotUnit = await Unit(slot36);
        await c.ExecuteAsync("insert into management.unit_property (unit_id, name) values (@slotUnit, 'Tough')", new { slotUnit });
        await c.ExecuteAsync("insert into management.slot_property (slot_id, name) values (@slot, 'Pierce')", new { slot = await Slot(slotUnit) });
        var weaponUnit = await Unit(weapon36);
        await c.ExecuteAsync("insert into management.unit_property (unit_id, name) values (@weaponUnit, 'Tough')", new { weaponUnit });
        var weapon = Guid.CreateVersion7();
        await c.ExecuteAsync("insert into management.weapon (id, slot_id, name) values (@weapon, @slot, 'w'); insert into management.weapon_property (weapon_id, name) values (@weapon, 'Smash')", new { weapon, slot = await Slot(weaponUnit) });

        var script = typeof(Persistence.Migrator).Assembly.GetManifestResourceStream("Tabletop.Management.Persistence.Migrations.023_army_edition.sql")!;
        await c.ExecuteAsync(await new StreamReader(script).ReadToEndAsync());

        Assert.Equal("3.5", await c.ExecuteScalarAsync<string>("select edition from management.army where id=@a35", new { a35 }));
        foreach (var army in new[] { a36, slot36, weapon36 })
            Assert.Equal("3.6", await c.ExecuteScalarAsync<string>("select edition from management.army where id=@army", new { army }));
        var e = await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync("insert into management.army (id, owner_id, name, game_system, source, edition) values (@id, @owner, 'x', 'grimdark-future', 's', '3.7')", new { id = Guid.CreateVersion7(), owner }));
        Assert.Equal("23514", e.SqlState);
        await c.ExecuteAsync("delete from management.account where id = @owner", new { owner });
    }

    [Fact]
    public async Task The_text_only_rules_grammar_is_admitted_by_the_checks()
    {
        await using var c = new NpgsqlConnection(_db.ConnectionString);
        async Task<string> Def(string name) => await c.ExecuteScalarAsync<string>("select pg_get_constraintdef(oid) from pg_constraint where conname = @name", new { name }) ?? "";
        Assert.Contains("count_wounds", await Def("effect_kind_check"));
        Assert.Contains("remove_spell_tokens", await Def("effect_kind_check"));
        Assert.Contains("weapon_range", await Def("effect_condition_kind_check"));
        Assert.Contains("remaining_size", await Def("effect_condition_kind_check"));
        Assert.Contains("positive_defense", await Def("effect_target_modifier_check"));
        Assert.Contains("one_marker", await Def("effect_duration_check"));
        Assert.Contains("add_hit", await Def("effect_optional_check"));
        Assert.Contains("optional", await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='effect'"));
        var entryColumns = (await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='catalog_entry'")).ToList();
        Assert.Contains("targeting_once_per_round", entryColumns);
        Assert.Contains("targeting_count_from_parameter", entryColumns);
    }

    [Fact]
    public async Task Built_in_ruins_at_their_old_default_become_dense_in_layouts_and_unfinished_games_only()
    {
        await using var c = new NpgsqlConnection(_db.ConnectionString);
        var (owner, hash) = (Guid.CreateVersion7(), "dense-ruins-" + Guid.NewGuid().ToString("N"));
        await c.ExecuteAsync("insert into management.account (id, name) values (@owner, 'dense test')", new { owner });
        await c.ExecuteAsync("insert into management.properties (content_hash, name, game_rules) values (@hash, 'GF test', 'grimdark-future')", new { hash });
        async Task<Guid> Table(string? status)
        {
            var table = Guid.CreateVersion7();
            Guid? game = null;
            if (status is not null)
            {
                game = Guid.CreateVersion7();
                await c.ExecuteAsync("insert into management.game (id, name, status, game_rules, properties_hash, scheduler_shape, enforcement, rng_seed, created_by) " +
                    "values (@game, 'g', @status, 'grimdark-future', @hash, 'alternating-activation', 'warn', 1, @owner)", new { game, status, hash, owner });
            }
            await c.ExecuteAsync("insert into management.table_state (id, owner_id, game_id, width, length, texture_builtin) values (@table, @o, @game, 72, 48, 'grass')",
                new { table, o = game is null ? owner : (Guid?)null, game });
            return table;
        }
        async Task<Guid> Volume(Guid table, string builtin, string flags)
        {
            var (placement, volume) = (Guid.CreateVersion7(), Guid.CreateVersion7());
            await c.ExecuteAsync("insert into management.terrain_placement (id, table_state_id, builtin, pos_x, pos_y) values (@placement, @table, @builtin, 10, 10)", new { placement, table, builtin });
            bool F(string f) => flags.Split(' ').Contains(f);
            await c.ExecuteAsync("insert into management.terrain_volume (id, terrain_placement_id, kind, footprint, dim_a, dim_b, top, impassable, blocks_los, see_in_out, cover, difficult, dangerous, dangerous_rush_charge, steep) " +
                "values (@volume, @placement, 'area', 'rect', 6, 6, 4, false, @los, @sio, @cover, @difficult, @dangerous, @drc, false)",
                new { volume, placement, los = F("los"), sio = F("sio"), cover = F("cover"), difficult = F("difficult"), dangerous = F("dangerous"), drc = F("drc") });
            return volume;
        }
        var (layout, playing, setup, ended) = (await Table(null), await Table("playing"), await Table("setup"), await Table("ended"));
        Guid[] dense =
        [
            await Volume(layout, "ruined-corner", "cover drc"), await Volume(playing, "ruined-corner", "cover drc"), await Volume(setup, "ruins", "cover drc"),
            await Volume(playing, "overgrown-ruins", "cover difficult"), await Volume(playing, "deadly-vegetation", "cover difficult dangerous"),
        ];
        Guid[] kept =
        [
            await Volume(ended, "ruined-corner", "cover drc"),            // an ended game
            await Volume(playing, "ruined-corner", "cover"),              // edited away from the default
            await Volume(playing, "ruins", "cover difficult"),
            await Volume(layout, "crater", "cover"),                      // another piece
        ];

        var script = typeof(Persistence.Migrator).Assembly.GetManifestResourceStream("Tabletop.Management.Persistence.Migrations.033_dense_builtin_ruins.sql")!;
        await c.ExecuteAsync(await new StreamReader(script).ReadToEndAsync());

        const string Sight = "select blocks_los and see_in_out from management.terrain_volume where id = @id";
        foreach (var id in dense) Assert.True(await c.ExecuteScalarAsync<bool>(Sight, new { id }));
        foreach (var id in kept) Assert.False(await c.ExecuteScalarAsync<bool>(Sight, new { id }));
        Assert.True(await c.ExecuteScalarAsync<bool>("select dangerous_rush_charge and cover from management.terrain_volume where id = @id", new { id = dense[0] }));
        await c.ExecuteAsync("delete from management.game where created_by = @owner; delete from management.account where id = @owner; delete from management.properties where content_hash = @hash", new { owner, hash });
    }

    [Fact]
    public void Running_the_migrator_twice_is_a_no_op() => Persistence.Migrator.Run(_db.ConnectionString);
}
