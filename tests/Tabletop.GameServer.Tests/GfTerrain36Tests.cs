using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 p.12 Unstable: "If any model in a unit moves over 6” when moving into or through unstable terrain, then it counts as Dangerous terrain."
/// 3.5.1 p.12 Ruins: "Dangerous when using rush/charge". Both are a DangerousOnRushCharge volume, read by the game's edition. 3.6.0 p.12 Steep:
/// "Units moving onto steep terrain count it as being in Difficult terrain, but units moving off it count it as open terrain" (3.5.1 p.12 Hills:
/// "Difficult when moving up"), the same in both editions. The words a client shows for them come from the server.
/// </summary>
public class GfTerrain36Tests
{
    /// <summary>A ruin centred on (x, -20), by default 4" wide and 30" long so no path goes round it.</summary>
    static TerrainPlacement Ruin(double x, double width = 4, double length = 30) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruins"), new Point3(x, -20, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(width, length), new Point2(0, 0), new Yaw(0), 0, 4,
            TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge, null))]);

    /// <summary>A steep hill, 6" wide and 30" long, centred on (x, -20), so no path goes round it.</summary>
    static TerrainPlacement Hill(double x) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill"), new Point3(x, -20, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(6, 30), new Point2(0, 0), new Yaw(0), 0, 2,
            TerrainDeclarations.Steep, null))]);

    static Destinations By(Sim sim, Tabletop.Content.Unit u, double dx) => new(u.Models.Select(m => sim.Model(m.Id)).Select(m => new ModelRelocation(m.Id, m.Location,
        new OnTable(new Placement(((OnTable)m.Location).Placement.Position with { X = ((OnTable)m.Location).Placement.Position.X + dx }, new Yaw(0))))).ToList());

    static readonly int[] Fours = Enumerable.Repeat(4, 30).ToArray();

    [Theory]
    [InlineData(RuleSets.Gf351, 0, 1)]
    [InlineData(RuleSets.Gf360, 1, 0)]
    public void An_advance_with_fast_over_six_inches_through_ruins_tests_under_3_6_and_a_five_inch_charge_does_not(string rules, int advanceTests, int chargeTests)
    {
        // Fast: Advance 8". -12 → -4, through the ruin at x -10..-6.
        var fast = Unit("A", "4+", "4+", [P("Fast")], 1, []);
        var advance = Sim.Gf(rules, State([fast], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20, terrain: [Ruin(-8)]), Fours);
        advance.Start(GfNames.AdvanceSeq, new SequenceContext(fast.Id, [], [], GfNames.Advance));
        Assert.Empty(advance.Steps("test"));                                                       // activated outside it
        advance.Answer(Samples.Team1, By(advance, fast, 8));
        Assert.Equal(advanceTests, advance.Steps("test").Count);

        // A 5" charge: -6.3 → -1.3, base to base, through the ruin at x -5.5..-1.5.
        var charger = Unit("A", "4+", "4+", [], 1, [], Melee("CCW", 1));
        var target = Unit("B", "4+", "4+", [], 1, [], Melee("CCW", 1));
        var charge = Sim.Gf(rules, State([charger], [target], x1: -6.3, x2: 0, terrain: [Ruin(-3.5)]), Fours);
        charge.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        charge.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        charge.Answer(Samples.Team1, By(charge, charger, 5));
        Assert.Equal(chargeTests, charge.Steps("test").Count);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_rush_activated_in_unstable_ground_tests_only_under_3_5_1(string rules)
    {
        // 3.6.0 p.12 names a move over 6" into or through it; being activated in it is not one.
        var unit = Unit("A", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(rules, State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20, terrain: [Ruin(-12)]), Fours);
        sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
        Assert.Equal(rules == RuleSets.Gf351 ? 1 : 0, sim.Steps("test").Count);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_rush_onto_a_steep_hill_is_capped_and_one_off_it_is_not(string rules)
    {
        // Onto it: -12 → -1 (11") across the hill at x -9..-3, over the 6" cap.
        var onto = Unit("A", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(rules, State([onto], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20, terrain: [Hill(-6)]), Fours);
        sim.Start(GfNames.RushSeq, new SequenceContext(onto.Id, [], [], GfNames.Rush));
        var ask = sim.AskFor(Samples.Team1);
        var region = ask.Legal as MoveRegion;
        Assert.NotNull(region);
        Assert.Single(region.SteepVolumeIds!);
        Assert.Empty(region.DifficultVolumeIds);
        Assert.NotNull(GfMoves.DifficultCapBreach(sim.State, ask, region, By(sim, onto, 11)));
        Assert.Null(GfMoves.DifficultCapBreach(sim.State, ask, region, By(sim, onto, 5)));

        // Off it: standing on the hill at -6, 11" east to 5, open ground.
        var off = Unit("A", "4+", "4+", [], 1, []);
        sim = Sim.Gf(rules, State([off], [Unit("B", "4+", "4+", [], 1, [])], x1: -6, x2: 20, terrain: [Hill(-6)]), Fours);
        sim.Start(GfNames.RushSeq, new SequenceContext(off.Id, [], [], GfNames.Rush));
        ask = sim.AskFor(Samples.Team1);
        Assert.Null(GfMoves.DifficultCapBreach(sim.State, ask, (MoveRegion)ask.Legal, By(sim, off, 11)));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_model_standing_on_the_hills_edge_rushes_and_charges_off_it_in_the_open(string rules)
    {
        // At x -2.6 a 32 mm base's centre is 0.4" past the hill's edge at -3, its base on the hill: it stands on it, so going east is moving off it.
        var rim = Unit("A", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(rules, State([rim], [Unit("B", "4+", "4+", [], 1, [])], x1: -2.6, x2: 20, terrain: [Hill(-6)]), Fours);
        sim.Start(GfNames.RushSeq, new SequenceContext(rim.Id, [], [], GfNames.Rush));
        var ask = sim.AskFor(Samples.Team1);
        Assert.Null(GfMoves.DifficultCapBreach(sim.State, ask, (MoveRegion)ask.Legal, By(sim, rim, 11)));

        // A charge 10.34" from the rim into base contact with a model at x 9 (contact at 7.74): the full 12", not 6".
        var charger = Unit("A", "4+", "4+", [], 1, [], Melee("CCW", 1));
        var target = Unit("B", "4+", "4+", [], 1, [], Melee("CCW", 1));
        var charge = Sim.Gf(rules, State([charger], [target], x1: -2.6, x2: 9, enforcement: Enforcement.Strict, terrain: [Hill(-6)]), Fours);
        charge.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        charge.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        var region = Assert.IsType<MoveRegion>(charge.AskFor(Samples.Team1).Legal);
        Assert.Equal(12, region.Allowance);
        charge.Answer(Samples.Team1, By(charge, charger, 10.34));
        Assert.Equal(7.74, ((OnTable)charge.Model(charger.Models[0].Id).Location).Placement.Position.X, 2);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Steep_is_read_per_model_by_where_it_starts_and_the_cap_binds_the_unit(string rules)
    {
        // Two models: the first on the hill (x -6), the second moved off it to x -14 (its base reaches -13.37; the hill is -9..-3).
        var unit = Unit("A", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(rules, State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -6, x2: 20, terrain: [Hill(-6)]), Fours);
        var second = sim.Model(unit.Models[1].Id);
        sim.Append([new ModelsMoved([new ModelRelocation(second.Id, second.Location,
            new OnTable(new Placement(((OnTable)second.Location).Placement.Position with { X = -14 }, new Yaw(0))))])]);
        sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
        var ask = sim.AskFor(Samples.Team1);
        var region = (MoveRegion)ask.Legal;
        Destinations Moves(double first, double other) => new([
            Relocate(sim, unit.Models[0].Id, first, 0), Relocate(sim, unit.Models[1].Id, other, 0)]);
        // The first off the hill 11" and the second 4" short of it: no model moves onto it.
        Assert.Null(GfMoves.DifficultCapBreach(sim.State, ask, region, Moves(11, 4)));
        // The second 6" onto it: it moves onto steep ground, so no model of the unit may go over 6".
        Assert.NotNull(GfMoves.DifficultCapBreach(sim.State, ask, region, Moves(11, 6)));
        Assert.Null(GfMoves.DifficultCapBreach(sim.State, ask, region, Moves(6, 6)));
        // Along the hill's top, 11" north, staying on it: neither onto nor off it, not difficult.
        Assert.Null(GfMoves.DifficultCapBreach(sim.State, ask, region, new Destinations([
            Relocate(sim, unit.Models[0].Id, 0, 11), Relocate(sim, unit.Models[1].Id, 0, 0)])));
    }

    static ModelRelocation Relocate(Sim sim, Guid id, double dx, double dy)
    {
        var m = sim.Model(id);
        var at = ((OnTable)m.Location).Placement.Position;
        return new ModelRelocation(id, m.Location, new OnTable(new Placement(at with { X = at.X + dx, Y = at.Y + dy }, new Yaw(0))));
    }

    static readonly EntryDocument Ward = new("Ward", CatalogKind.Spell, false, null, 1, "Test Book", "Target up to two friendly units within 24\" get Regeneration once.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 2, 24),
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.GrantProperty, null, null, "Regeneration", null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], [])]);

    [Theory]
    [InlineData(RuleSets.Gf351, 0)]
    [InlineData(RuleSets.Gf360, 1)]
    public void Unstable_reads_the_whole_move_across_a_stop(string rules, int tests)
    {
        // A Fast caster's Advance (8") in two 4" segments round a stop: -20 → -16 → -12, into the ruin at x -16..-12 on the second.
        // 3.6.0 p.12 "moves over 6”" is the movement, 8", not either segment.
        var wizard = Unit("Wizard", "4+", "4+", [P("Fast")], 1, [P("Caster", 3)]);
        var state = State([wizard], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20, terrain: [Ruin(-14)]).Under(rules);
        var sim = new Sim(state, rules == RuleSets.Gf360 ? GrimdarkFutureRules.V360 : GrimdarkFutureRules.V351, TestProperties.WithSpells(Ward), new ScriptedDice(4));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(wizard.Id, [], [], GfNames.Advance));
        Assert.True(Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Stoppable);
        sim.Answer(Samples.Team1, By(sim, wizard, 4) with { Continues = true });
        Assert.Empty(sim.Steps("test"));
        sim.Answer(Samples.Team1, new Passed());                                                    // the window after the stop
        sim.Answer(Samples.Team1, By(sim, wizard, 4));
        Assert.Equal(tests, sim.Steps("test").Count);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_path_over_six_inches_goes_round_a_small_unstable_ruin_when_that_fits(string rules)
    {
        // Fast Advance (8"): -12 → -5, 7" straight through a 2" ruin at x -9.5..-7.5; round it is under 8". 3.5.1 Advance: not dangerous anyway.
        var fast = Unit("A", "4+", "4+", [P("Fast")], 1, []);
        var sim = Sim.Gf(rules, State([fast], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20, terrain: [Ruin(-8.5, 2, 2)]), Fours);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(fast.Id, [], [], GfNames.Advance));
        sim.Answer(Samples.Team1, By(sim, fast, 7));
        Assert.Empty(sim.Steps("test"));
        Assert.Equal(-5, ((OnTable)sim.Model(fast.Models[0].Id).Location).Placement.Position.X, 3);
    }

    [Fact]
    public void Terrain_words_follow_the_edition()
    {
        Assert.Equal("See in and out", GrimdarkFutureRules.V351.TerrainWords["see_in_and_out"]);
        Assert.Equal("Dense", GrimdarkFutureRules.V360.TerrainWords["see_in_and_out"]);
        Assert.Equal("Difficult moving up", GrimdarkFutureRules.V351.TerrainWords["steep"]);
        Assert.Equal("Steep", GrimdarkFutureRules.V360.TerrainWords["steep"]);
        Assert.Equal("Unstable", GrimdarkFutureRules.V360.TerrainWords["dangerous_on_rush_charge"]);
        Assert.Equal("Sight blocking", GrimdarkFutureRules.V360.TerrainWords["blocks_line_of_sight"]);
        Assert.All(GrimdarkFutureRules.V360.TerrainWords.Keys, k => Assert.True(BuiltinTerrain.DeclarationNames.ContainsKey(k), k));
    }

    [Fact]
    public async Task The_welcome_carries_the_games_terrain_words()
    {
        var db = new InMemorySeeder();
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2);
        await using var client = await TestClient.OpenAsync(factory);
        var welcome = await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(GrimdarkFutureRules.Instance.TerrainWords, welcome.TerrainWords);
        Assert.Equal("Difficult moving up", welcome.TerrainWords["steep"]);
    }

    /// <summary>p.6 "within 3" of a marker" (3.6.0 p.6 the same): the range a client draws about each marker's centre.</summary>
    [Fact]
    public async Task The_welcome_carries_the_marker_range()
    {
        var db = new InMemorySeeder();
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby, teams: 2);
        await using var client = await TestClient.OpenAsync(factory);
        var welcome = await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(3.0, welcome.MarkerRange);
        Assert.Equal(GfObjectives.SeizeRange, GrimdarkFutureRules.V360.MarkerRange);
    }
}
