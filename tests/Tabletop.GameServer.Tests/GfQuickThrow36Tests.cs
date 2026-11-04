using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Quick Throw (3.6.0): "This model may always target enemies that are not in line of sight when shooting with Indirect weapons (instead of only when
/// using Hold actions)." Read by name in `GfAttack.IndirectApplies`. GfIndirect36Tests' layouts: a 4"-tall wall between a mortar at x = -6 and its
/// target at x = 6, and a one-model screen at (0, 0.8) between (-10, 0) and (10, 0).
/// </summary>
public class GfQuickThrow36Tests
{
    /// <summary>The hand entry: no rows, played, read by name. The shipped 3.6 set lists it text-only until its content lands.</summary>
    static readonly EntryDocument QuickThrow = new(GfNames.QuickThrow, CatalogKind.Model, false, Aggregation.Any, null, null,
        "This model may always target enemies that are not in line of sight when shooting with Indirect weapons (instead of only when using Hold actions).", null, []);

    static readonly PropertySet Props = PropertiesJson.ToSet(TestProperties.CoreDocument36 with
        { Entries = [.. TestProperties.CoreDocument36.Entries.Where(e => e.Name != GfNames.QuickThrow), QuickThrow] }, Guid.CreateVersion7);

    static Sim Game(GameState state, params int[] dice) => new(state.Under(RuleSets.Gf360), GrimdarkFutureRules.V360, Props, new ScriptedDice(dice));

    static Unit Mortar(bool quickThrow) => Unit("Mortar", "4+", "4+", [], 1, quickThrow ? [P(GfNames.QuickThrow)] : [], Gun("Mortar", "36\"", 1, P("Indirect")));

    static SequenceContext Advance(Unit a, Unit t) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Advance);

    static Sim Hidden(bool quickThrow)
    {
        var (mortar, target) = (Mortar(quickThrow), Unit("T", "4+", "6+", [], 1, []));
        var wall = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(0, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 6), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.BlocksLineOfSight, null))]);
        var sim = Game(State([mortar], [target], x1: -6, x2: 6, terrain: [wall]), 5, 6);
        sim.Start(GfNames.AttackSequence, Advance(mortar, target));
        return sim;
    }

    static Sim Screened(bool quickThrow)
    {
        var (mortar, target, screen) = (Mortar(quickThrow), Unit("Lone", "4+", "6+", [], 1, []), Unit("Screen", "4+", "4+", [], 1, []));
        var sim = Game(State([mortar], [target, screen]), 6, 5);
        sim.Append([new ModelsMoved([Move(sim, mortar.Models[0].Id, new Point3(-10, 0, 0)), Move(sim, screen.Models[0].Id, new Point3(0, 0.8, 0)),
            Move(sim, target.Models[0].Id, new Point3(10, 0, 0))])]);
        sim.Start(GfNames.AttackSequence, Advance(mortar, target));
        return sim;
    }

    static ModelRelocation Move(Sim sim, Guid model, Point3 to) => new(model, sim.Model(model).Location, new OnTable(new Placement(to, new Yaw(0))));

    static RollRecord Block(Sim sim) => sim.Steps("roll-to-block").SelectMany(s => s.Rolls).Single();

    [Fact]
    public void On_an_advance_a_quick_throw_model_targets_unseen_and_ignores_sight_blocker_cover()
    {
        var hidden = Hidden(quickThrow: true);
        var attacks = hidden.Steps("determine-attacks").Single();
        Assert.Equal(1, attacks.Values!["attacks"]);
        Assert.Contains(GfNames.Indirect, attacks.Applied!);
        Assert.Contains(GfNames.QuickThrow, attacks.Applied!);

        var screened = Screened(quickThrow: true);
        Assert.Equal(new CoverVerdict(true, 1, 1, GfNames.Indirect), Block(screened).Cover);
        Assert.DoesNotContain(Block(screened).Modifiers, m => m.Source == "cover");
        Assert.Contains(GfNames.QuickThrow, screened.Steps("determine-attacks").Single().Applied!);
    }

    [Fact]
    public void Without_quick_throw_an_advance_needs_sight_and_takes_cover()
    {
        Assert.Equal(0, Hidden(quickThrow: false).Steps("determine-attacks").Single().Values!["attacks"]);
        var screened = Screened(quickThrow: false);
        Assert.Equal(new CoverVerdict(true, 1, 1), Block(screened).Cover);
        Assert.Contains(Block(screened).Modifiers, m => m.Source == "cover" && m.Amount == 1);
        Assert.DoesNotContain(GfNames.QuickThrow, screened.Steps("determine-attacks").Single().Applied ?? []);
    }
}
