using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Owner ruling (2026-11-09): wounds that kill every model of the unit whatever the allocation open no `remove_casualties` Ask; `assign`
/// removes the whole unit with the events a confirmed allocation writes. p.8: "For each wound that the unit has taken, the defending player
/// must remove one model as a casualty." A rule that may ignore a wound after allocation (Regeneration, p.14) keeps the Ask.
/// </summary>
public class GfForcedCasualtiesTests
{
    static Sim Start(string rulesId, Unit attacker, Unit target, int wounds, int[] dice, params Guid[] weapons)
    {
        var state = State([attacker], [target], enforcement: Enforcement.Warn);
        var (rules, props) = rulesId == RuleSets.Gf360 ? (GrimdarkFutureRules.V360, TestProperties.Core36) : (GrimdarkFutureRules.Instance, TestProperties.Core);
        if (rulesId == RuleSets.Gf360) state = state.Under(RuleSets.Gf360);
        var sim = new Sim(state, new WithSequences(rules, WithSequences.Parent("test-parent", "wounds")), props, new ScriptedDice(dice));
        var parent = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(parent, "test-parent", new SequenceContext(attacker.Id, [target.Id], weapons, GfNames.Hold)),
            new StepCompleted(parent, "wounds", null, [], []) { Values = new Dictionary<string, int> { ["wounds"] = wounds } }]);
        sim.Start(GfNames.DealWounds, new SequenceContext(attacker.Id, [target.Id], weapons, GfNames.Hold));
        return sim;
    }

    static Unit Shooter() => Unit("A", "4+", "4+", [], 1, []);

    [Theory]
    [InlineData(RuleSets.Gf351, 5)] [InlineData(RuleSets.Gf351, 7)]
    [InlineData(RuleSets.Gf360, 5)] [InlineData(RuleSets.Gf360, 7)]
    public void Wounds_that_kill_every_model_ask_nothing_and_remove_the_unit(string rules, int wounds)
    {
        var t = Unit("T", "4+", "4+", [], 5, []);
        var sim = Start(rules, Shooter(), t, wounds, [1]);
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        Assert.Equal(0, sim.Alive(t.Id));
        Assert.All(t.Models, m => Assert.IsType<OnShelf>(sim.Model(m.Id).Location));
        Assert.Equal(5, sim.Unit(t.Id).WoundsThisActivation);
        Assert.Empty(sim.State.Projection.Stack);
        var assign = Assert.Single(sim.Steps("assign"));                                      // one segment, as a confirmed allocation of all five
        Assert.Null(assign.Answer);
        Assert.All(t.Models, m => Assert.Equal(1, assign.Values![$"killed_{m.Id:N}"]));
        Assert.Contains(assign.Changes, c => c is ModelsMoved mm && mm.Moves.Count == 5 && mm.Moves.All(r => r.To is OnShelf));   // the removal both clients animate
    }

    [Theory]
    [InlineData(RuleSets.Gf351)] [InlineData(RuleSets.Gf360)]
    public void Wounds_short_of_the_whole_unit_still_ask(string rules)
    {
        var t = Unit("T", "4+", "4+", [], 5, []);
        var sim = Start(rules, Shooter(), t, 4, [1]);
        var legal = Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal);
        Assert.Equal((4, 5), (legal.Amount, legal.Targets.Count));
    }

    /// <summary>p.15 Tough: the four plain models take one wound each, the joined Tough(3) hero "must be assigned wounds last" and takes three.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351)] [InlineData(RuleSets.Gf360)]
    public void A_joined_tough_hero_and_its_unit_given_exactly_their_wounds_ask_nothing(string rules)
    {
        var hero = Unit("H", "4+", "4+", [], 1, [P("Hero"), P("Tough", 3)]).Models[0] with { Ordinal = 5 };
        var t = Unit("T", "4+", "4+", [], 5, []) with { Models = [.. Unit("T", "4+", "4+", [], 4, []).Models, hero] };
        var sim = Start(rules, Shooter(), t, 7, [1]);
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        Assert.Equal(0, sim.Alive(t.Id));
        Assert.Equal(7, sim.Unit(t.Id).WoundsThisActivation);
        Assert.Contains(GfNames.Hero, sim.Steps("assign").Single().Applied ?? []);
        var shortOne = Start(rules, Shooter(), t, 6, [1]);
        Assert.IsType<Allocation>(shortOne.AskFor(Samples.Team2).Legal);                      // one short: the hero may live, the defender chooses
    }

    /// <summary>Deadly(3) against two Tough(3) models: two wounds kill both whichever way they are assigned (3.5.1 p.13, 3.6.0 p.14).</summary>
    [Theory]
    [InlineData(RuleSets.Gf351)] [InlineData(RuleSets.Gf360)]
    public void Deadly_wounds_enough_for_every_model_ask_nothing(string rules)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Cannon", "24\"", 1, P("Deadly", 3)));
        var t = Unit("T", "4+", "4+", [], 2, [P("Tough", 3)]);
        var sim = Start(rules, a, t, 2, [1], a.Models[0].Weapons.Select(w => w.Id).ToArray());
        Assert.Null(sim.AskForOrNull(Samples.Team2));
        Assert.Equal((0, 6), (sim.Alive(t.Id), sim.Unit(t.Id).WoundsThisActivation));
    }

    /// <summary>p.14 Regeneration: "roll one die for each. On a 5+ it is ignored." It rolls after allocation, so a model may survive and the defender still picks.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351)] [InlineData(RuleSets.Gf360)]
    public void A_unit_that_rolls_to_ignore_wounds_is_still_asked(string rules)
    {
        var t = Unit("T", "4+", "4+", [], 5, [P("Regeneration")]);
        var sim = Start(rules, Shooter(), t, 7, [1, 1, 1, 1, 1]);
        Assert.Equal(7, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);
        var bane = Unit("B", "4+", "4+", [], 1, [], Gun("Dart", "24\"", 1, P("Bane")));                // Bane ignores Regeneration: nothing can save a model
        var baned = Start(rules, bane, t, 5, [1], bane.Models[0].Weapons.Select(w => w.Id).ToArray());
        Assert.Null(baned.AskForOrNull(Samples.Team2));
        Assert.Equal(0, baned.Alive(t.Id));
    }
}
