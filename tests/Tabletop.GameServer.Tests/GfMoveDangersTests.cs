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
/// The move Ask names the volumes dangerous to the move, as ApplyMove tests them, so a client can draw its area in bands. p.11: "If a model moves in
/// or through dangerous terrain, or is activated in it, then it must take a dangerous terrain test." 3.5.1 p.12: "Ruins - Cover + Dangerous when using
/// rush/charge". 3.6.0 p.12 Unstable: "If any model in a unit moves over 6” when moving into or through unstable terrain, then it counts as Dangerous
/// terrain."
/// </summary>
public class GfMoveDangersTests
{
    static TerrainPlacement Piece(double x, double y, TerrainDeclarations d) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("ruins"), new Point3(x, y, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 2), new Point2(0, 0), new Yaw(0), 0, 2, d, null))]);

    static readonly TerrainPlacement Pit = Piece(-6, -30, TerrainDeclarations.Dangerous);
    static readonly TerrainPlacement Ruin = Piece(-6, -10, TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge);
    static Guid Id(TerrainPlacement t) => t.Volumes[0].Id;
    static readonly int[] Fours = Enumerable.Repeat(4, 30).ToArray();

    static MoveRegion Opened(string rules, string action, params PropertyInstance[] props)
    {
        var mover = Unit("A", "4+", "4+", props, 1, [], Melee("CCW", 1));
        var target = Unit("B", "4+", "4+", [], 1, [], Melee("CCW", 1));
        var sim = Sim.Gf(rules, State([mover], [target], x1: -12, x2: 0, terrain: [Pit, Ruin]), Fours);
        var seq = action switch { GfNames.Advance => GfNames.AdvanceSeq, GfNames.Rush => GfNames.RushSeq, _ => GfNames.ChargeSeq };
        sim.Start(seq, new SequenceContext(mover.Id, [], [], action));
        if (action == GfNames.Charge) sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        return Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
    }

    [Theory]
    [InlineData(GfNames.Advance, false)]
    [InlineData(GfNames.Rush, true)]
    [InlineData(GfNames.Charge, true)]
    public void Under_3_5_1_dangerous_terrain_is_dangerous_at_any_length_and_ruins_only_on_a_rush_or_charge(string action, bool ruins)
    {
        var region = Opened(RuleSets.Gf351, action);
        Assert.Equal(ruins ? [Id(Pit), Id(Ruin)] : [Id(Pit)], region.DangerousVolumeIds!);
        Assert.Null(region.UnstableVolumeIds);
        Assert.Null(region.UnstableOver);
    }

    [Theory]
    [InlineData(GfNames.Advance)]
    [InlineData(GfNames.Rush)]
    [InlineData(GfNames.Charge)]
    public void Under_3_6_unstable_terrain_is_dangerous_to_a_move_over_six_inches_whatever_the_action(string action)
    {
        var region = Opened(RuleSets.Gf360, action);
        Assert.Equal([Id(Pit)], region.DangerousVolumeIds!);
        Assert.Equal([Id(Ruin)], region.UnstableVolumeIds!);
        Assert.Equal(6, region.UnstableOver);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_flying_unit_is_told_of_no_dangerous_terrain_and_a_strider_is(string rules)
    {
        var flying = Opened(rules, GfNames.Rush, P("Flying"));
        Assert.Null(flying.DangerousVolumeIds);
        Assert.Null(flying.UnstableVolumeIds);
        Assert.Null(flying.UnstableOver);
        var strider = Opened(rules, GfNames.Rush, P("Strider"));
        Assert.Contains(Id(Pit), strider.DangerousVolumeIds!);
        Assert.Null(strider.DifficultCap);
    }

    [Fact]
    public void A_table_with_no_dangerous_terrain_names_none()
    {
        var mover = Unit("A", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(RuleSets.Gf360, State([mover], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20), Fours);
        sim.Start(GfNames.RushSeq, new SequenceContext(mover.Id, [], [], GfNames.Rush));
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Null(region.DangerousVolumeIds);
        Assert.Null(region.UnstableVolumeIds);
        Assert.Null(region.UnstableOver);
    }

    static readonly EntryDocument Ward = new("Ward", CatalogKind.Spell, false, null, 1, "Test Book", "Target up to two friendly units within 24\" get Regeneration once.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 2, 24),
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.GrantProperty, null, null, "Regeneration", null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], [])]);

    [Fact]
    public void Under_3_6_the_segment_after_a_stop_is_told_what_is_left_of_six_inches()
    {
        // A Fast caster's Advance (8"): 4" to a stop, then the rest. 3.6.0 p.12 "moves over 6”" is the whole movement.
        var wizard = Unit("Wizard", "4+", "4+", [P("Fast")], 1, [P("Caster", 3)]);
        var state = State([wizard], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20, terrain: [Ruin]).Under(RuleSets.Gf360);
        var sim = new Sim(state, GrimdarkFutureRules.V360, TestProperties.WithSpells(Ward), new ScriptedDice(4));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(wizard.Id, [], [], GfNames.Advance));
        Assert.Equal(6, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).UnstableOver);
        var m = sim.Model(wizard.Models[0].Id);
        var at = ((OnTable)m.Location).Placement.Position;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m.Id, m.Location, new OnTable(new Placement(at with { X = at.X + 4 }, new Yaw(0))))], Continues: true));
        sim.Answer(Samples.Team1, new Passed());                                                    // the window after the stop
        Assert.Equal(2, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).UnstableOver!.Value, 6);
    }
}
