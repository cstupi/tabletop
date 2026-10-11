using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

/// <summary>Owner ruling: "Spells move as however that unit moves". A spell's forced move treats terrain the way the target's own move does:
/// p.13 Flying "may move through units and terrain, and ignore terrain effects whilst moving"; p.14 Strider "May ignore the effects of difficult
/// terrain when moving"; p.13 Aircraft "ignore all units and terrain when moving and stopping".</summary>
public class GfForcedMoveTerrainTests
{
    static EffectDocument Kind(EffectKind kind, EffectDuration duration, int? amount = null, string? roll = null, string? entry = null, params EffectDocument[] children) =>
        new(EffectSubject.Self, 10, kind, roll, null, entry, null, null, null, null, amount, false, null, null, null, duration, [], children);

    static readonly PropertySet Props = TestProperties.WithSpells(
        new EntryDocument("Hypnosis", CatalogKind.Spell, false, null, 1, null, "Hypnosis.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 24),
            [Kind(EffectKind.TakeTest, EffectDuration.Interaction, roll: "morale", children: [Kind(EffectKind.Move, EffectDuration.Interaction, amount: 6)])]),
        new EntryDocument("Wings", CatalogKind.Spell, false, null, 1, null, "Wings.", new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 18),
            [Kind(EffectKind.GrantProperty, EffectDuration.Once, entry: "Flying")]));

    // The target stands at (28, 20); the wall is at x 31, the bog beside it at y 30, both across the target's line of a +5" move only when y is theirs.
    static readonly TerrainPlacement Wall = new(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(31, 20, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 6), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null))]);
    static readonly TerrainPlacement Bog = new(Guid.CreateVersion7(), VisualRef.OfBuiltin("bog"), new Point3(31, 30, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 2), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Difficult, null))]);

    static Guid WallId => Wall.Volumes[0].Id;
    static Guid BogId => Bog.Volumes[0].Id;

    /// <summary>A caster's Hypnosis on the target, cast on a 6 and its morale test failed on a 1, under strict: the caster's team holds the move Ask.</summary>
    static Sim Hypnotised(PropertyInstance[] targetRules, out Unit target, string? granted = null)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]); target = Unit("T", "4+", "4+", targetRules, 1, []);
        var sim = new Sim(State([wizard], [target], x1: -4, x2: 4, enforcement: Enforcement.Strict, terrain: [Wall, Bog]),
            new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Props, new ScriptedDice(6, 1));
        foreach (var (u, x) in new[] { (wizard, 20.0), (target, 28.0) })
        {
            var m = u.Models[0].Id;
            sim.Append([new ModelsMoved([new ModelRelocation(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(x, 20, 0), new Yaw(0))))])]);
        }
        if (granted is not null) sim.Append([new GrantAdded(target.Id, new Grant(Guid.CreateVersion7(), Props.Entry(granted)!.Effects[0].Id, granted, target.Id, 1))]);
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Hypnosis|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        return sim;
    }

    static MoveRegion Region(Sim sim) => Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);

    static ModelRelocation Shifted(Sim sim, Guid model, double dx)
    {
        var at = ((OnTable)sim.Model(model).Location).Placement;
        return new ModelRelocation(model, sim.Model(model).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }));
    }

    static DispatchOutcome Move(Sim sim, params ModelRelocation[] moves)
    {
        var outcome = sim.Dispatch(new AnswerAsk(sim.AskFor(Samples.Team1).Id, new Destinations(moves)), Samples.P1, Samples.Team1);
        sim.Run();
        return outcome;
    }

    static double X(Sim sim, Guid model) => ((OnTable)sim.Model(model).Location).Placement.Position.X;

    [Fact]
    public void A_plain_target_is_moved_as_it_moves_itself_blocked_by_impassable_terrain_and_capped_by_difficult()
    {
        var sim = Hypnotised([], out var target);
        var region = Region(sim);
        Assert.Equal([WallId], region.ImpassableVolumeIds.ToList());
        Assert.Equal([BogId], region.DifficultVolumeIds.ToList());
        Assert.Equal(GfNames.DifficultCap, region.DifficultCap);
        Assert.Equal(GfNames.Separation, region.Separation);
        var m = target.Models[0].Id;
        Assert.Equal(RefusalReason.Illegal, Assert.IsType<Refused>(Move(sim, Shifted(sim, m, 5))).Reason);     // through the wall (p.11)
        Assert.Equal(28, X(sim, m), 6);
    }

    [Fact]
    public void A_flying_target_is_moved_over_impassable_terrain_with_no_difficult_cap()
    {
        var sim = Hypnotised([P("Flying")], out var target);
        var region = Region(sim);
        Assert.Empty(region.ImpassableVolumeIds);
        Assert.Null(region.DifficultCap);
        var m = target.Models[0].Id;
        Assert.IsType<Applied>(Move(sim, Shifted(sim, m, 5)));                                                  // over the wall, landing clear of it
        Assert.Equal(33, X(sim, m), 6);
    }

    [Fact]
    public void A_flying_target_still_may_not_be_left_standing_in_impassable_terrain()
    {
        var sim = Hypnotised([P("Flying")], out var target);
        var m = target.Models[0].Id;
        Assert.Equal(RefusalReason.Illegal, Assert.IsType<Refused>(Move(sim, Shifted(sim, m, 3))).Reason);     // p.11, p.7: ends in the wall
        Assert.Equal(28, X(sim, m), 6);
    }

    [Fact]
    public void A_strider_target_is_moved_with_no_difficult_cap_but_still_blocked_by_impassable_terrain()
    {
        var sim = Hypnotised([P("Strider")], out var target);
        var region = Region(sim);
        Assert.Equal([WallId], region.ImpassableVolumeIds.ToList());
        Assert.Equal([BogId], region.DifficultVolumeIds.ToList());
        Assert.Null(region.DifficultCap);
        var m = target.Models[0].Id;
        Assert.IsType<Applied>(Move(sim, Shifted(sim, m, -5)));
        Assert.Equal(23, X(sim, m), 6);
    }

    [Fact]
    public void An_aircraft_target_is_moved_with_no_terrain_volumes_and_no_cap()
    {
        var sim = Hypnotised([P(GfNames.Aircraft)], out _);
        var region = Region(sim);
        Assert.Empty(region.ImpassableVolumeIds);
        Assert.Empty(region.DifficultVolumeIds);
        Assert.Null(region.DifficultCap);
        Assert.Null(region.Separation);                                                                         // p.13 "ignore all units"
    }

    [Fact]
    public void A_target_granted_flying_flies_in_the_forced_move_and_keeps_the_grant_for_its_own_activation()
    {
        // Owner ruling: the forced move is not the target's activation, so its granted Flying is read for terrain but neither spent nor held.
        var sim = Hypnotised([], out var target, granted: "Wings");
        Assert.Empty(Region(sim).ImpassableVolumeIds);
        var m = target.Models[0].Id;
        Assert.IsType<Applied>(Move(sim, Shifted(sim, m, 5)));
        Assert.Equal(33, X(sim, m), 6);
        Assert.DoesNotContain(sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Values?.Keys ?? []), k => k.StartsWith(GfNames.FlyingHeldPrefix));
        while (sim.AskForOrNull(Samples.Team1) is not null) sim.Answer(Samples.Team1, new Passed());
        sim.Append(GrimdarkFutureRules.Instance.OnTurnEnded(sim.State, Props, new TurnContext(Samples.Team1, null, 0, sim.State.Projection.LastSeq)));
        Assert.Single(sim.Unit(target.Id).Grants);                                                              // the caster's activation ended: still held by the target
        var since = sim.State.Projection.LastSeq;
        sim.Start(GfNames.RushSeq, new SequenceContext(target.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team2, new Destinations([Shifted(sim, m, 2)]));
        while (sim.AskForOrNull(Samples.Team2) is not null) sim.Answer(Samples.Team2, new Passed());
        Assert.Single(sim.Unit(target.Id).Grants);
        sim.Append(GrimdarkFutureRules.Instance.OnTurnEnded(sim.State, Props, new TurnContext(Samples.Team2, null, since, sim.State.Projection.LastSeq)));
        Assert.Empty(sim.Unit(target.Id).Grants);                                                               // spent by its own activation
    }
}
