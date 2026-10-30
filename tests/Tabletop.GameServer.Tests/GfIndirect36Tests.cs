using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 p.14 Indirect: "When using Hold actions and shooting, may target enemies that are not in line of sight as if in line of sight, and ignores
/// cover from Sight Blockers." 3.5.1 p.14: "Gets -1 to hit rolls when shooting after moving. May target enemies that are not in line of sight as if
/// in line of sight, and ignores cover from sight obstructions." The hidden layout is a 4"-tall wall between a mortar at x = -6 and its target at
/// x = 6; the screened layout is GfCoverTests' (a one-model screen at (0, 0.8) between (-10, 0) and (10, 0)), where the target is seen and in cover.
/// </summary>
public class GfIndirect36Tests
{
    static Unit Mortar() => Unit("Mortar", "4+", "4+", [], 1, [], Gun("Mortar", "36\"", 1, P("Indirect")));

    static SequenceContext Fire(Unit a, Unit t, string action) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), action);

    static (Sim Sim, Unit Mortar, Unit Target) Hidden(string rulesId)
    {
        var (mortar, target) = (Mortar(), Unit("T", "4+", "6+", [], 1, []));
        var wall = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(0, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 6), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.BlocksLineOfSight, null))]);
        return (Sim.Gf(rulesId, State([mortar], [target], x1: -6, x2: 6, terrain: [wall]), 5, 6), mortar, target);
    }

    static (Sim Sim, Unit Mortar, Unit Target) Screened(string rulesId)
    {
        var (mortar, target, screen) = (Mortar(), Unit("Lone", "4+", "6+", [], 1, []), Unit("Screen", "4+", "4+", [], 1, []));
        var sim = Sim.Gf(rulesId, State([mortar], [target, screen]), 6, 5);   // a hit even at -1, then a 5 that blocks only with the cover bonus
        sim.Append([new ModelsMoved([Move(sim, mortar.Models[0].Id, new Point3(-10, 0, 0)), Move(sim, screen.Models[0].Id, new Point3(0, 0.8, 0)),
            Move(sim, target.Models[0].Id, new Point3(10, 0, 0))])]);
        return (sim, mortar, target);
    }

    static ModelRelocation Move(Sim sim, Guid model, Point3 to) => new(model, sim.Model(model).Location, new OnTable(new Placement(to, new Yaw(0))));

    static RollRecord Block(Sim sim) => sim.Steps("roll-to-block").SelectMany(s => s.Rolls).Single();

    [Theory]
    [InlineData(RuleSets.Gf351, 1, true, false)]
    [InlineData(RuleSets.Gf360, 0, false, true)]
    public void Indirect_after_an_advance_needs_sight_and_gives_cover_under_3_6(string rulesId, int hiddenAttacks, bool minusOne, bool coverApplies)
    {
        // 3.5.1: after an Advance, the hidden target is shot at -1; 3.6.0: Indirect is off, so the wall hides it and the screen gives cover.
        var (hidden, mortar, target) = Hidden(rulesId);
        // The sight query with no running activation: 3.6 reads the unit as not holding, so the hidden target has no shooter.
        Assert.Equal(hiddenAttacks, hidden.State.Rules.Shooters(hidden.State, hidden.Engine.Properties, mortar.Id, target.Id).Count);
        hidden.Start(GfNames.AttackSequence, Fire(mortar, target, GfNames.Advance));
        Assert.Equal(hiddenAttacks, hidden.Steps("determine-attacks").Single().Values!["attacks"]);

        var (screened, m2, t2) = Screened(rulesId);
        screened.Start(GfNames.AttackSequence, Fire(m2, t2, GfNames.Advance));
        Assert.Equal(minusOne, screened.Steps("roll-to-hit").Single().Rolls.Single().Modifiers.Any(m => m.Source == "Indirect" && m.Amount == -1));
        Assert.Equal(coverApplies ? new CoverVerdict(true, 1, 1) : new CoverVerdict(true, 1, 1, "Indirect"), Block(screened).Cover);
        Assert.Equal(coverApplies, Block(screened).Modifiers.Any(m => m.Source == "cover" && m.Amount == 1));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void On_a_hold_it_targets_unseen_and_ignores_sight_blocker_cover(string rulesId)
    {
        var (hidden, mortar, target) = Hidden(rulesId);
        hidden.Start(GfNames.AttackSequence, Fire(mortar, target, GfNames.Hold));
        Assert.Equal(1, hidden.Steps("determine-attacks").Single().Values!["attacks"]);
        Assert.Contains("Indirect", hidden.Steps("determine-attacks").Single().Applied!);

        var (screened, m2, t2) = Screened(rulesId);
        screened.Start(GfNames.AttackSequence, Fire(m2, t2, GfNames.Hold));
        Assert.DoesNotContain(screened.Steps("roll-to-hit").Single().Rolls.Single().Modifiers, m => m.Source == "Indirect");
        Assert.Equal(new CoverVerdict(true, 1, 1, "Indirect"), Block(screened).Cover);
        Assert.DoesNotContain(Block(screened).Modifiers, m => m.Source == "cover");
    }
}
