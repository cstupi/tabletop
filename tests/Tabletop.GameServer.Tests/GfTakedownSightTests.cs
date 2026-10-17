using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// p.14 Takedown: "This model may pick any model in the target unit as its individual target, which is resolved as if it was a unit of [1]",
/// and "Note that other models in the target's unit don't block line of sight or provide cover to the target model in the unit."
/// The shooter stands at (-10, 0); sight lines to a model at (10, y) cross x = 0 within 0.315" of y / 2 (GfCoverTests' layout).
/// </summary>
public class GfTakedownSightTests
{
    static readonly Point3 Shooter = new(-10, 0, 0);

    static TerrainPlacement Wall() => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(0, -6, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(2, 2), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.BlocksLineOfSight, null))]);

    static TerrainPlacement Wood() => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("wood"), new Point3(10, -6, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(3, 3), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, null))]);

    static Unit Sniper() => Unit("Sniper", "4+", "4+", [], 1, [], Gun("Long Rifle", "36\"", 1, P("Takedown")));

    static SequenceContext Fire(Unit a, Unit t) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold);

    static Sim Laid(Unit shooter, Unit target, IReadOnlyList<Point3> at, TerrainPlacement terrain, params int[] dice)
    {
        var sim = Sim.Gf(State([shooter], [target], terrain: [terrain]), dice);
        var moves = new List<ModelRelocation> { new(shooter.Models[0].Id, sim.Model(shooter.Models[0].Id).Location, new OnTable(new Placement(Shooter, new Yaw(0)))) };
        moves.AddRange(target.Models.Select((m, i) => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnTable(new Placement(at[i], new Yaw(0))))));
        sim.Append([new ModelsMoved(moves)]);
        return sim;
    }

    [Fact]
    public void The_pick_offers_the_models_the_shooter_sees_with_the_targets_own_models_ignored()
    {
        var (sniper, squad) = (Sniper(), Unit("Squad", "4+", "4+", [], 3, []));
        // model 0 stands behind its squad-mate (model 1, on the line at x = 0); model 2 stands behind the wall.
        var sim = Laid(sniper, squad, [new Point3(10, 0, 0), new Point3(0, 0, 0), new Point3(10, -12, 0)], Wall());
        sim.Start(GfNames.AttackSequence, Fire(sniper, squad));
        var pick = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal(new HashSet<Guid> { squad.Models[0].Id, squad.Models[1].Id }, pick.Eligible.ToHashSet());
    }

    [Fact]
    public void The_picked_model_takes_its_own_cover_as_a_unit_of_one()
    {
        // The picked model in the wood, its two squad-mates in the open: the unit has no majority in cover, the picked model is in cover.
        var (sniper, squad) = (Sniper(), Unit("Squad", "4+", "6+", [], 3, []));
        var sim = Laid(sniper, squad, [new Point3(10, -6, 0), new Point3(10, 12, 0), new Point3(13, 12, 0)], Wood(), 6, 5);
        sim.Start(GfNames.AttackSequence, Fire(sniper, squad));
        Assert.Equal(0, sim.Steps("determine-attacks").Single().Values!["in_cover"]);
        sim.Answer(Samples.Team1, new SelectedPieces([squad.Models[0].Id]));
        var block = sim.Steps("roll-to-block").Single().Rolls.Single();
        Assert.Equal(new CoverVerdict(true, 1, 1), block.Cover);
        Assert.Contains(block.Modifiers, m => m.Source == "cover" && m.Amount == 1);
        Assert.True(block.Dice.Single().Success);                                                       // 5 + 1 against 6+
    }

    [Fact]
    public void A_picked_model_in_the_open_gets_no_cover_from_a_unit_mostly_in_cover()
    {
        var (sniper, trio) = (Sniper(), Unit("Trio", "4+", "6+", [], 3, []));
        var sim = Laid(sniper, trio, [new Point3(9.3, -6, 0), new Point3(10.7, -6, 0), new Point3(10, 12, 0)], Wood(), 6, 5);
        sim.Start(GfNames.AttackSequence, Fire(sniper, trio));
        Assert.Equal(1, sim.Steps("determine-attacks").Single().Values!["in_cover"]);                    // two of three in the wood
        sim.Answer(Samples.Team1, new SelectedPieces([trio.Models[2].Id]));
        var block = sim.Steps("roll-to-block").Single().Rolls.Single();
        Assert.Equal(new CoverVerdict(false, 0, 1), block.Cover);
        Assert.DoesNotContain(block.Modifiers, m => m.Source == "cover");
    }
}
