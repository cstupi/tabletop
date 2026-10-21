using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Two documents of one game rules that differ only in a rule text, so they seed as two sets with the same rows.</summary>
static class AliasDocs
{
    static EffectDocument Row(EffectKind kind, params EffectDocument[] children) =>
        new(EffectSubject.Self, 10, kind, "hit", null, null, null, null, null, null, 1, false, null, null, null, EffectDuration.Interaction, [], children);

    /// <summary>"Bolster": a modify_roll with a modify_roll child, then a force_reroll; "Plain": one modify_roll.</summary>
    public static PropertySetDocument Of(string rules, string text) => new("Alias " + text, rules, [], [], ["hit"],
    [
        new EntryDocument("Bolster", CatalogKind.Model, false, Aggregation.Any, null, null, text, null,
            [Row(EffectKind.ModifyRoll, Row(EffectKind.ModifyRoll)), Row(EffectKind.ForceReroll)]),
        new EntryDocument("Plain", CatalogKind.Model, false, Aggregation.Any, null, null, "Plain.", null, [Row(EffectKind.ModifyRoll)]),
    ]);
}

public class CurrentRulesTests
{
    static readonly CancellationToken Ct = CancellationToken.None;
    static readonly GameServerOptions Options = new() { DbConnection = "", StytchProjectId = "p", StytchJwksUrl = "u" };

    static PropertySet Set(string text) => PropertiesJson.ToSet(AliasDocs.Of("grimdark-future", text), Guid.CreateVersion7);

    [Fact]
    public void An_old_row_aliases_the_row_with_the_same_entry_path_and_kind()
    {
        var old = Set("Old.");
        var current = Set("New.");
        var aliases = EffectAliases.Match(current, EffectAliases.Of(old));
        var oldBolster = old.Entry("Bolster")!.Effects;
        var newBolster = current.Entry("Bolster")!.Effects;
        Assert.Same(newBolster[0], aliases[oldBolster[0].Id]);
        Assert.Same(newBolster[0].Children[0], aliases[oldBolster[0].Children[0].Id]);     // a nested child
        Assert.Same(newBolster[1], aliases[oldBolster[1].Id]);
        Assert.Same(current.Entry("Plain")!.Effects[0], aliases[old.Entry("Plain")!.Effects[0].Id]);
        Assert.Equal(4, aliases.Count);
    }

    [Fact]
    public void A_row_of_another_kind_or_at_a_path_the_set_lacks_has_no_alias()
    {
        var current = Set("New.");
        ForeignEffect[] foreign =
        [
            new(Guid.CreateVersion7(), "Bolster", [1], EffectKind.ModifyRoll),          // the row there is a force_reroll
            new(Guid.CreateVersion7(), "Bolster", [2], EffectKind.ModifyRoll),          // no third row
            new(Guid.CreateVersion7(), "Bolster", [1, 0], EffectKind.ModifyRoll),       // the force_reroll has no child
            new(Guid.CreateVersion7(), "Gone", [0], EffectKind.ModifyRoll),             // no such entry
        ];
        Assert.Empty(EffectAliases.Match(current, foreign));
    }

    [Fact]
    public void EffectById_falls_back_to_the_alias_and_the_in_memory_store_aliases_its_other_sets_of_the_same_rules()
    {
        var store = new InMemoryPropertiesStore();
        var oldDoc = AliasDocs.Of("grimdark-future", "Old.");
        var newDoc = AliasDocs.Of("grimdark-future", "New.");
        var otherRules = AliasDocs.Of("other-rules", "Other.");
        store.SeedAsync(oldDoc, Ct).GetAwaiter().GetResult();
        store.SeedAsync(newDoc, Ct).GetAwaiter().GetResult();
        store.SeedAsync(otherRules, Ct).GetAwaiter().GetResult();
        var old = store.LoadAsync(PropertiesJson.Hash(oldDoc), Ct).GetAwaiter().GetResult()!;
        var other = store.LoadAsync(PropertiesJson.Hash(otherRules), Ct).GetAwaiter().GetResult()!;
        var current = store.LoadAsync(PropertiesJson.Hash(newDoc), Ct).GetAwaiter().GetResult()!;
        var child = old.Entry("Bolster")!.Effects[0].Children[0];
        Assert.Equal(current.Entry("Bolster")!.Effects[0].Children[0].Id, current.EffectById(child.Id)!.Id);
        Assert.Null(current.EffectById(other.Entry("Plain")!.Effects[0].Id));         // another game rules' rows are never aliased
    }

    sealed class Capture : IRulesEngineFactory
    {
        public GameInfo? Info;
        public IRulesEngine Create(IGameRules rules, GameInfo game) { Info = game; return new InertEngine(); }
    }

    static async Task<(InMemorySeeder Db, string Current, CurrentProperties Pins)> WithCurrentSet(PropertySetDocument? doc = null)
    {
        var db = new InMemorySeeder();
        string hash;
        if (doc is null) hash = await db.SeedPropertiesAsync();
        else { await db.Properties.SeedAsync(doc, Ct); hash = PropertiesJson.Hash(doc); }
        var pins = new CurrentProperties();
        pins.Set("grimdark-future", hash);
        return (db, hash, pins);
    }

    [Theory]
    [InlineData(GameStatus.Lobby)]
    [InlineData(GameStatus.Setup)]
    [InlineData(GameStatus.Playing)]
    public async Task Hydration_moves_a_game_that_has_not_ended_to_its_rules_current_set(GameStatus status)
    {
        var (db, current, pins) = await WithCurrentSet();
        var seeded = await db.SeedGameAsync(status);
        if (status != GameStatus.Lobby) await db.SeedTableStateAsync(seeded.GameId);
        if (status == GameStatus.Playing) db.SeedArmiesInPlay(seeded);
        var engines = new Capture();
        var session = await Hydrator.HydrateAsync(seeded.GameId, new SessionServices(db.Rows, db.Log, engines, Options, NullLoggerFactory.Instance, pins), Ct);
        Assert.NotEqual(current, seeded.PropertiesHash);
        Assert.Equal(current, session.Info.PropertiesHash);
        Assert.Equal(current, db.GameRows.Game(seeded.GameId)!.PropertiesHash);
        if (status == GameStatus.Playing) Assert.Equal(current, engines.Info!.PropertiesHash);
    }

    [Fact]
    public async Task Hydration_leaves_an_ended_game_on_its_own_set()
    {
        var (db, _, pins) = await WithCurrentSet();
        var seeded = await db.SeedGameAsync(GameStatus.Ended);
        var session = await Hydrator.HydrateAsync(seeded.GameId, new SessionServices(db.Rows, db.Log, new InertEngineFactory(), Options, NullLoggerFactory.Instance, pins), Ct);
        Assert.Equal(seeded.PropertiesHash, session.Info.PropertiesHash);
        Assert.Equal(seeded.PropertiesHash, db.GameRows.Game(seeded.GameId)!.PropertiesHash);
    }

    [Fact]
    public async Task Without_a_current_set_nothing_moves()
    {
        var db = new InMemorySeeder();
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        var session = await Hydrator.HydrateAsync(seeded.GameId, new SessionServices(db.Rows, db.Log, new InertEngineFactory(), Options, NullLoggerFactory.Instance), Ct);
        Assert.Equal(seeded.PropertiesHash, session.Info.PropertiesHash);
    }

    [Fact]
    public async Task A_grant_minted_under_the_old_set_resolves_under_the_current_one_after_the_move()
    {
        var oldDoc = AliasDocs.Of("grimdark-future", "Old.");
        var (db, current, pins) = await WithCurrentSet(AliasDocs.Of("grimdark-future", "New."));
        await db.Properties.SeedAsync(oldDoc, Ct);
        var seeded = await db.SeedGameAsync(GameStatus.Playing);
        await db.Rows.RepinPropertiesAsync(seeded.GameId, PropertiesJson.Hash(oldDoc), Ct);    // created under the old set
        await db.SeedTableStateAsync(seeded.GameId);
        db.SeedArmiesInPlay(seeded);
        var minted = (await db.Properties.LoadAsync(PropertiesJson.Hash(oldDoc), Ct))!.Entry("Bolster")!.Effects[1];
        var grant = new Grant(Guid.CreateVersion7(), minted.Id, "Bolster", Guid.CreateVersion7(), 1);

        var engines = new Capture();
        await Hydrator.HydrateAsync(seeded.GameId, new SessionServices(db.Rows, db.Log, engines, Options, NullLoggerFactory.Instance, pins), Ct);
        var set = (await db.Properties.LoadAsync(engines.Info!.PropertiesHash, Ct))!;
        Assert.Equal(current, set.ContentHash);
        Assert.Equal(set.Entry("Bolster")!.Effects[1].Id, set.EffectById(grant.EffectId)!.Id);
    }
}
