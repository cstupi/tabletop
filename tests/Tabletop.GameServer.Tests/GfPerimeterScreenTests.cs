using Tabletop.Content;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Core line of sight with the default `perimeter` unit sight blocking (p.5 / 3.6.0 p.5: "Models can't see through solid obstacles, including
/// the perimeter of other units (friendly or enemy)"; p.8 Who Can Shoot: "All models in a unit with line of sight to the target, and that have a
/// weapon that is within range of it, may fire at it"). A unit of four shoots at one model 20" up the y axis; a third unit stands just in front
/// of the target, screening three of the shooters, and the fourth shoots from 30" to the side, past the screen.
/// </summary>
public class GfPerimeterScreenTests
{
    static Unit Tall(Unit u, double height, double diameter) =>
        u with { Models = u.Models.Select(m => m with { Height = height, Base = Base.Circle(diameter) }).ToList() };

    static GameState Table(params (Unit Unit, bool Team1, (double X, double Y)[] At)[] units)
    {
        var state = State(units.Where(u => u.Team1).Select(u => u.Unit).ToList(), units.Where(u => !u.Team1).Select(u => u.Unit).ToList());
        var moves = units.SelectMany(u => u.Unit.Models.Zip(u.At, (m, at) => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location,
            new OnTable(new Placement(new Point3(at.X, at.Y, 0), new Yaw(0)))))).ToList();
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved(moves), null));
        return state;
    }

    /// <summary>The screens: one model on a 4" base, two side by side, five in two rows; each covers every line from the three screened shooters.</summary>
    static (double Diameter, (double X, double Y)[] At) Screen(int models) => models switch
    {
        1 => (4, [(0, 17.3)]),
        2 => (1.26, [(-0.63, 18.5), (0.63, 18.5)]),
        5 => (1.26, [(-1.26, 18.5), (0, 18.5), (1.26, 18.5), (-0.63, 17.4), (0.63, 17.4)]),
        _ => throw new ArgumentOutOfRangeException(nameof(models)),
    };

    [Theory]
    [InlineData(RuleSets.Gf351, 1, true)]
    [InlineData(RuleSets.Gf351, 2, true)]
    [InlineData(RuleSets.Gf351, 5, true)]
    [InlineData(RuleSets.Gf351, 2, false)]
    [InlineData(RuleSets.Gf360, 1, true)]
    [InlineData(RuleSets.Gf360, 2, true)]
    [InlineData(RuleSets.Gf360, 5, true)]
    [InlineData(RuleSets.Gf360, 1, false)]
    [InlineData(RuleSets.Gf360, 2, false)]
    [InlineData(RuleSets.Gf360, 5, false)]
    public void Only_the_model_with_a_line_past_another_units_perimeter_shoots(string rules, int screenModels, bool friendlyScreen)
    {
        var shooter = Tall(Unit("Shooters", "4+", "4+", [], 4, [], Gun("Rifle", "36\"", 1)), 1.5, 1.26);
        var (diameter, at) = Screen(screenModels);
        var screen = Tall(Unit("Screen", "4+", "4+", [], screenModels, []), 1.5, diameter);
        var target = Tall(Unit("Target", "4+", "4+", [], 1, []), 1.5, 1.26);
        var state = Table((shooter, true, [(-1.5, 0), (0, 0), (1.5, 0), (30, 0)]), (screen, friendlyScreen, at), (target, false, [(0, 20)]));

        var sight = Sight.From(state, shooter.Id);
        Assert.Equal([false, false, false, true], shooter.Models.Select(m => sight.CanSee(m.Id, target.Models[0].Id)));

        var sim = Sim.Gf(rules, state, 1, 1, 1, 1);
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = target.Id }));
        var values = sim.Steps("determine-attacks").Single().Values!;
        Assert.Equal(1, values["attacks"]);
        Assert.Equal([$"aim_{shooter.Models[3].Id}_{target.Models[0].Id}"], values.Keys.Where(k => k.StartsWith("aim_", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Goblins on 25 mm bases (imported height 1.5 × 0.984" = 1.476", eye at half that) shoot at walkers on 50 mm bases (2.953" tall) behind an
    /// infantry unit on 25 mm bases standing right in front of them. The perimeter is a prism as tall as its tallest model, so a line from the eye
    /// to the walker's top passes over it (p.5 "get down to the eye level of the miniature"); with the walkers no taller than the screen, nothing sees.
    /// </summary>
    [Theory]
    [InlineData(2.953, true)]
    [InlineData(1.476, false)]
    public void A_line_passes_over_a_perimeter_only_to_the_part_of_a_taller_target_above_it(double targetHeight, bool seen)
    {
        var shooter = Tall(Unit("Goblins", "5+", "5+", [], 4, [], Gun("Rifle", "24\"", 1)), 1.476, 0.984);
        var screen = Tall(Unit("Infantry", "4+", "4+", [], 4, []), 1.476, 0.984);
        var target = Tall(Unit("Walkers", "4+", "4+", [], 3, []), targetHeight, 1.9685);
        var state = Table((shooter, true, [(-1.5, 0), (-0.5, 0), (0.5, 0), (1.5, 0)]), (screen, false, [(-1.5, 18.3), (-0.5, 18.3), (0.5, 18.3), (1.5, 18.3)]),
            (target, false, [(-2.5, 20), (0, 20), (2.5, 20)]));

        var sight = Sight.From(state, shooter.Id);
        var line = sight.Line(shooter.Models[1].Id, target.Models[1].Id)!;
        Assert.Equal(seen, line.Clear);
        if (!seen) { Assert.Equal([screen.Id], line.BlockedBy); return; }
        Assert.Equal(targetHeight, line.Seen!.Value.Z, 6);                                        // the walker's top: the lower points are screened
        var eye = Battlefield.Placed(state, shooter.Models[1].Id)!.Value.Eye();
        var t = (18.3 + 0.492 - eye.Y) / (line.Seen.Value.Y - eye.Y);                             // the screen's far edge
        Assert.True(eye.Z + t * (line.Seen.Value.Z - eye.Z) > 1.476);                             // above the perimeter's top there
    }
}
