using Tabletop.Content;
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
    /// <summary>A 4"-wide ruin centred on (x, -20), 30" long so no path goes round it.</summary>
    static TerrainPlacement Ruin(double x) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruins"), new Point3(x, -20, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(4, 30), new Point2(0, 0), new Yaw(0), 0, 4,
            TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge, null))]);

    /// <summary>A steep hill, a circle of radius 3 centred on (x, -20), 30" long sideways so no path goes round it.</summary>
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
}
