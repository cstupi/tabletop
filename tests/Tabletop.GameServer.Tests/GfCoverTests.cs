using Tabletop.Content;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// p.11: "If the majority of models in a unit are fully inside a piece of cover terrain or behind a sight blocker (for multi-model
/// units), or that are mostly inside cover terrain or behind sight blockers (for single-model units), they get +1 to Defense rolls";
/// "models can't see through the perimeter of other units (friendly or enemy), they are sight blockers that can provide other units
/// with cover". One majority over the models that pass either test (R-N16).
///
/// The layout: the shooter at (-10, 0); a screen, a one-model unit of the target's army, at (0, 0.8). Every base has radius 0.63 and
/// every body is 0..1.5" tall, so the eye is at 0.75". A target at (10, 0) is seen along lines that run parallel to x through its
/// centre and its eight rim points (y offsets 0, ±0.445, ±0.63); halfway, at x = 0, those lines sit at half the offset, y 0..±0.315.
/// The screen spans y 0.17..1.43 there, so the lines to the +0.445 and +0.63 rim points pass through it and the centre line misses
/// it by 0.17": Check is clear and SightBlockersBetween names the screen. A target at (10, 12) is seen along lines that cross x = 0 at
/// y ≈ 6, far from the screen. The wood is a 3"×3" Cover area centred on (10, -6), wholly around a base there (y -6.63..-5.37).
/// </summary>
public class GfCoverTests
{
    static readonly Point3 Shooter = new(-10, 0, 0), Screen = new(0, 0.8, 0), Screened = new(10, 0, 0), Open = new(10, 12, 0), InWood = new(10, -6, 0);

    static TerrainPlacement Wood() => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("wood"), InWood, new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(3, 3), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, null))]);

    static Sim Laid(Unit shooter, Unit target, Unit screen, IReadOnlyList<Point3> at, bool wood = false, params int[] dice)
    {
        var sim = Sim.Gf(State([shooter], [target, screen], terrain: wood ? [Wood()] : null), dice);
        var moves = new List<ModelRelocation> { Move(sim, shooter.Models[0].Id, Shooter), Move(sim, screen.Models[0].Id, Screen) };
        moves.AddRange(target.Models.Select((m, i) => Move(sim, m.Id, at[i])));
        sim.Append([new ModelsMoved(moves)]);
        return sim;
    }

    static ModelRelocation Move(Sim sim, Guid model, Point3 to) => new(model, sim.Model(model).Location, new OnTable(new Placement(to, new Yaw(0))));

    static Unit Rifle() => Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "36\"", 1));
    static Unit Wall() => Unit("Screen", "4+", "4+", [], 1, []);

    /// <summary>The fixture's geometry, checked rather than trusted: the line to the screened target is clear and passes the screen.</summary>
    static void AssertScreened(Sim sim, Guid shooter, Guid target, Guid screen)
    {
        var (from, to) = (Battlefield.Placed(sim.State, shooter)!.Value, Battlefield.Placed(sim.State, target)!.Value);
        var body = Bodies.AsBlockingVolume(screen, Battlefield.Placed(sim.State, screen)!.Value);
        Assert.True(LineOfSight.Check(from, to, [body]).Clear);
        Assert.Equal([screen], Cover.SightBlockersBetween(from, to, [body]));
    }

    [Fact]
    public void One_model_in_cover_terrain_and_one_behind_another_units_base_are_a_majority_of_three()
    {
        var (shooter, target, screen) = (Rifle(), Unit("Squad", "4+", "4+", [], 3, []), Wall());
        var sim = Laid(shooter, target, screen, [InWood, Screened, Open], wood: true);
        AssertScreened(sim, shooter.Models[0].Id, target.Models[1].Id, screen.Models[0].Id);
        Assert.Single(Battlefield.ModelsInCoverTerrain(sim.State, target.Id, wholeBase: true));
        Assert.Equal([target.Models[1].Id], Battlefield.ModelsBehindSightBlockers(sim.State, target.Id, [shooter.Models[0].Id]));
        Assert.True(GfRolls.InCover(sim.State, target.Id, [shooter.Models[0].Id], ignoresSightObstructions: false));   // 2 of 3 by the union; one of three by either test alone
    }

    [Fact]
    public void One_screened_model_of_two_is_not_a_majority()
    {
        var (shooter, target, screen) = (Rifle(), Unit("Pair", "4+", "4+", [], 2, []), Wall());
        var sim = Laid(shooter, target, screen, [Screened, Open]);
        AssertScreened(sim, shooter.Models[0].Id, target.Models[0].Id, screen.Models[0].Id);
        Assert.False(GfRolls.InCover(sim.State, target.Id, [shooter.Models[0].Id], ignoresSightObstructions: false));
    }

    [Fact]
    public void A_single_model_behind_another_units_base_is_in_cover()
    {
        var (shooter, target, screen) = (Rifle(), Unit("Lone", "4+", "4+", [], 1, []), Wall());
        var sim = Laid(shooter, target, screen, [Screened]);
        AssertScreened(sim, shooter.Models[0].Id, target.Models[0].Id, screen.Models[0].Id);
        Assert.True(GfRolls.InCover(sim.State, target.Id, [shooter.Models[0].Id], ignoresSightObstructions: false));
    }

    [Fact]
    public void A_single_model_with_a_clear_line_and_no_blocker_is_not_in_cover()
    {
        var (shooter, target, screen) = (Rifle(), Unit("Lone", "4+", "4+", [], 1, []), Wall());
        var sim = Laid(shooter, target, screen, [Open]);
        Assert.Empty(Battlefield.ModelsBehindSightBlockers(sim.State, target.Id, [shooter.Models[0].Id]));
        Assert.False(GfRolls.InCover(sim.State, target.Id, [shooter.Models[0].Id], ignoresSightObstructions: false));
    }

    [Fact]
    public void A_model_seen_past_a_blocker_by_one_shooter_and_clearly_by_another_is_not_behind_a_sight_blocker()
    {
        // "Every attacking model with a clear line to it" is behind a blocker, or the model does not count: a second shooter at (10, 20)
        // looks down x = 10 at the target and nothing stands between them.
        var shooter = Unit("Shooters", "4+", "4+", [], 2, [], Gun("Rifle", "36\"", 1));
        var (target, screen) = (Unit("Lone", "4+", "4+", [], 1, []), Wall());
        var sim = Laid(shooter, target, screen, [Screened]);
        sim.Append([new ModelsMoved([Move(sim, shooter.Models[1].Id, new Point3(10, 20, 0))])]);
        Assert.True(GfRolls.InCover(sim.State, target.Id, [shooter.Models[0].Id], ignoresSightObstructions: false));
        Assert.False(GfRolls.InCover(sim.State, target.Id, [shooter.Models[0].Id, shooter.Models[1].Id], ignoresSightObstructions: false));
    }

    [Fact]
    public void Indirect_ignores_cover_from_sight_obstructions_and_keeps_cover_terrain()
    {
        // p.14 Indirect: "ignores cover from sight obstructions". Blast "Ignores cover" (p.13) outright; Indirect names only the sight half.
        var mortar = Unit("Mortar", "4+", "4+", [], 1, [], Gun("Mortar", "36\"", 1, P("Indirect")));
        var (screened, screen) = (Unit("Lone", "4+", "6+", [], 1, []), Wall());
        var sim = Laid(mortar, screened, screen, [Screened], wood: false, 6, 5);                          // a hit, then a 5 that blocks only with the cover bonus
        AssertScreened(sim, mortar.Models[0].Id, screened.Models[0].Id, screen.Models[0].Id);
        sim.Start(GfNames.AttackSequence, new SequenceContext(mortar.Id, [screened.Id], mortar.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        Assert.DoesNotContain(sim.Steps("roll-to-block").Single().Rolls.Single().Modifiers, m => m.Source == "cover");
        Assert.Equal(["Indirect"], sim.Steps("determine-attacks").Single().Applied);                      // seen, so only the cover half acted

        var (wooded, screen2) = (Unit("Lone", "4+", "6+", [], 1, []), Wall());
        var inWood = Laid(mortar, wooded, screen2, [InWood], wood: true, 6, 5);
        inWood.Start(GfNames.AttackSequence, new SequenceContext(mortar.Id, [wooded.Id], mortar.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        var block = inWood.Steps("roll-to-block").Single();
        Assert.Contains(block.Rolls.Single().Modifiers, m => m.Source == "cover" && m.Amount == 1);
        Assert.Equal(0, block.Values!["wounds"]);
    }
    [Fact]
    public void A_model_screened_only_by_its_own_squad_mates_base_is_not_behind_a_sight_blocker()
    {
        // R-N18: p.11 "they are sight blockers that can provide other units with cover", so a unit's own models do not screen it.
        var (shooter, pair) = (Rifle(), Unit("Pair", "4+", "4+", [], 2, []));
        var sim = Sim.Gf(State([shooter], [pair]));
        sim.Append([new ModelsMoved([Move(sim, shooter.Models[0].Id, Shooter), Move(sim, pair.Models[0].Id, Screened), Move(sim, pair.Models[1].Id, Screen)])]);
        AssertScreened(sim, shooter.Models[0].Id, pair.Models[0].Id, pair.Models[1].Id);                 // the squad-mate's base is on the line
        Assert.Empty(Battlefield.ModelsBehindSightBlockers(sim.State, pair.Id, [shooter.Models[0].Id]));
        Assert.False(GfRolls.InCover(sim.State, pair.Id, [shooter.Models[0].Id], ignoresSightObstructions: false));
    }

    // Combat-log spec → What the server adds: every defense roll of a shooting attack carries the target unit's one verdict.
    static SequenceContext Fire(Unit a, Unit t) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold);
    static IReadOnlyList<RollRecord> Blocks(Sim sim) => sim.Steps("roll-to-block").SelectMany(s => s.Rolls).ToList();

    [Fact]
    public void A_screened_single_model_blocks_with_a_verdict_of_one_of_one_in_cover()
    {
        var (shooter, target, screen) = (Rifle(), Unit("Lone", "4+", "6+", [], 1, []), Wall());
        var sim = Laid(shooter, target, screen, [Screened], wood: false, 6, 5);
        sim.Start(GfNames.AttackSequence, Fire(shooter, target));
        Assert.Equal(new CoverVerdict(true, 1, 1), Blocks(sim).Single().Cover);
    }

    [Fact]
    public void One_screened_model_of_two_blocks_with_a_verdict_of_one_of_two_not_in_cover()
    {
        var (shooter, target, screen) = (Rifle(), Unit("Pair", "4+", "6+", [], 2, []), Wall());
        var sim = Laid(shooter, target, screen, [Screened, Open], wood: false, 6, 5);
        sim.Start(GfNames.AttackSequence, Fire(shooter, target));
        var block = Blocks(sim).Single();
        Assert.Equal(new CoverVerdict(false, 1, 2), block.Cover);
        Assert.DoesNotContain(block.Modifiers, m => m.Source == "cover");
    }

    [Fact]
    public void Blast_at_a_unit_in_cover_records_the_verdict_and_names_blast_as_what_ignored_it()
    {
        var shooter = Unit("Mortar", "4+", "4+", [], 1, [], Gun("Mortar", "36\"", 1, P("Blast", 3)));
        var (target, screen) = (Unit("Lone", "4+", "6+", [], 1, []), Wall());
        var sim = Laid(shooter, target, screen, [Screened], wood: false, 6, 5);
        sim.Start(GfNames.AttackSequence, Fire(shooter, target));
        var block = Blocks(sim).Single();
        Assert.Equal(new CoverVerdict(true, 1, 1, "Blast"), block.Cover);
        Assert.DoesNotContain(block.Modifiers, m => m.Source == "cover");
    }

    [Fact]
    public void Indirect_dropping_the_sight_half_names_indirect_and_cover_terrain_stands()
    {
        var mortar = Unit("Mortar", "4+", "4+", [], 1, [], Gun("Mortar", "36\"", 1, P("Indirect")));
        var (screened, screen) = (Unit("Lone", "4+", "6+", [], 1, []), Wall());
        var sim = Laid(mortar, screened, screen, [Screened], wood: false, 6, 5);
        sim.Start(GfNames.AttackSequence, Fire(mortar, screened));
        Assert.Equal(new CoverVerdict(true, 1, 1, "Indirect"), Blocks(sim).Single().Cover);

        var (wooded, screen2) = (Unit("Lone", "4+", "6+", [], 1, []), Wall());
        var inWood = Laid(mortar, wooded, screen2, [InWood], wood: true, 6, 5);
        inWood.Start(GfNames.AttackSequence, Fire(mortar, wooded));
        Assert.Equal(new CoverVerdict(true, 1, 1), Blocks(inWood).Single().Cover);
    }

    [Fact]
    public void A_hold_action_gives_every_block_group_the_verdict_fire_groups_settled()
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "36\"", 2, P("Rending")));
        var (target, screen) = (Unit("Lone", "4+", "6+", [], 1, []), Wall());
        var sim = Laid(shooter, target, screen, [Screened], wood: false, 6, 5, 5, 5);
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Tabletop.Protocol.Tests.Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = target.Id }));
        var blocks = Blocks(sim);
        Assert.Equal(2, blocks.Count);                                                                  // the Rending six and the plain five
        Assert.All(blocks, b => Assert.Equal(new CoverVerdict(true, 1, 1), b.Cover));
    }

    [Fact]
    public void A_melee_block_has_no_verdict()
    {
        var (fighter, target) = (Unit("F", "4+", "4+", [], 1, [], Melee("Axe", 1)), Unit("T", "4+", "6+", [], 1, []));
        var sim = Sim.Gf(State([fighter], [target], x1: -1, x2: 1), 6, 5);
        sim.Start(GfNames.AttackSequence, new SequenceContext(fighter.Id, [target.Id], fighter.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Charge));
        Assert.Null(Blocks(sim).Single().Cover);
    }
}
