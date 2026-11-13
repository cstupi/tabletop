using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6 Hit & Run, Guerrilla, Harassing: "Once per round, this model may move by up to 3" after shooting or being in melee." A unit moves together, as its
/// slowest model (owner ruling: "The unit would only move as fast as its slowest model to retain cohesion"), so it cannot leave a model behind: the move
/// is offered only when every living model of the unit holds the rule. The Enhanced Auras ("This model and its unit get Enhanced Guerrilla") give the 6"
/// move to a unit that holds the rule throughout, and nothing to one that does not.
/// </summary>
public class ArmyRuleAuditFinishMovesTests
{
    /// <summary>A squad of two with a Rifle and a joined Hero with a Pistol, each with its own rules; shoots and misses, then the after-shooting window.</summary>
    static (Sim Sim, Unit A) ShootThenWindow(PropertyInstance[] squad, PropertyInstance[] hero, bool heroDead = false)
    {
        var a = ArmyRuleAuditHoldersTests.Joined(Unit("A", "4+", "4+", [], 3, squad, Gun("Rifle", "24\"", 1)), hero, Gun("Pistol", "12\"", 1));
        var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Sim.Gf36(State([a], [b], x1: -4, x2: 4), [.. Enumerable.Repeat(2, 20)]);
        if (heroDead) sim.Append([new ModelsMoved([new ModelRelocation(a.Models[2].Id, sim.Model(a.Models[2].Id).Location, new OnShelf())])]);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        var groups = heroDead ? new Dictionary<string, Guid> { ["Rifle"] = b.Id } : new Dictionary<string, Guid> { ["Rifle"] = b.Id, ["Pistol"] = b.Id };
        sim.Answer(Samples.Team1, new Assigned(groups));
        return (sim, a);
    }

    static IReadOnlyList<string> Offered(Sim sim) =>
        sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.React, Legal: Options o } ? o.Choices.Select(c => c.Label).ToList() : [];

    [Theory]
    [InlineData("Hit & Run")]
    [InlineData("Guerrilla")]
    [InlineData("Harassing")]
    public void The_move_is_offered_only_when_every_living_model_holds_the_rule(string rule)
    {
        Assert.Equal([$"{rule}: move up to 3\""], Offered(ShootThenWindow([P(rule)], [P(rule)]).Sim));
        Assert.Empty(Offered(ShootThenWindow([P(rule)], []).Sim));             // a joined Hero without it: the unit cannot leave it behind
        Assert.Empty(Offered(ShootThenWindow([], [P(rule)]).Sim));             // a Hero with it in a squad without
        Assert.Equal([$"{rule}: move up to 3\""], Offered(ShootThenWindow([P(rule)], [], heroDead: true).Sim));   // the model without it gone
    }

    [Theory]
    [InlineData("Guerrilla")]
    [InlineData("Harassing")]
    public void An_enhanced_aura_gives_six_inches_to_a_unit_that_holds_the_rule_throughout_and_nothing_to_one_that_does_not(string rule)
    {
        var aura = P($"Enhanced {rule} Aura");
        Assert.Equal([$"Enhanced {rule}: move up to 6\""], Offered(ShootThenWindow([P(rule)], [P(rule), aura]).Sim));
        Assert.Equal([$"Enhanced {rule}: move up to 6\""], Offered(ShootThenWindow([P(rule), aura], [P(rule)]).Sim));
        Assert.Empty(Offered(ShootThenWindow([P(rule)], [aura]).Sim));         // the Aura's carrier lacks the rule: no model may be left behind
    }
}
