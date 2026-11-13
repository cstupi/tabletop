using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Owner ruling: "The unit would only move as fast as its slowest model to retain cohesion." A unit's Advance, Rush and Charge distance is the
/// least of its models' own; a model's restriction to some actions (Immobile, 3.5.1 Artillery, 3.6 Cumbersome) holds its whole unit.</summary>
public class ArmyRuleAuditMovementTests
{
    static Unit Squad(PropertyInstance[] squad, PropertyInstance[] hero)
    {
        var u = Unit("A", "4+", "4+", [], 3, squad);
        var models = u.Models.ToList();
        models[^1] = models[^1] with { Properties = [P("Hero"), .. hero] };
        return u with { Models = models };
    }

    static double Allowance(string rules, Unit a, string sequence, string action)
    {
        var sim = Sim.Gf(rules, State([a], [Unit("E", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 4, 4, 4, 4);
        sim.Append([new ActionRecorded(a.Id, action)]);
        sim.Start(sequence, new SequenceContext(a.Id, [], [], action));
        for (var i = 0; i < 5 && sim.AskForOrNull(Samples.Team1) is { } ask; i++)
        {
            if (ask.Legal is MoveRegion region) return region.Allowance;
            sim.Answer(Samples.Team1, ask.AllowPass ? new Passed() : new Chosen("pass"));
        }
        throw new InvalidOperationException("no move asked");
    }

    static UnitThreat Threat(string rules, Unit a)
    {
        var state = State([a], [Unit("E", "4+", "4+", [], 1, [])], x1: -20, x2: 20);
        var (r, s, props) = rules == RuleSets.Gf360 ? (GrimdarkFutureRules.V360, state.Under(RuleSets.Gf360), TestProperties.Core36) : (GrimdarkFutureRules.V351, state, TestProperties.Core);
        return r.Threats(s, props)[a.Id];
    }

    [Theory]
    [InlineData(RuleSets.Gf351, "Slow", "", 4, 8)]
    [InlineData(RuleSets.Gf360, "Slow", "", 4, 8)]
    [InlineData(RuleSets.Gf351, "Fast", "", 6, 12)]
    [InlineData(RuleSets.Gf360, "Fast", "", 6, 12)]
    [InlineData(RuleSets.Gf351, "", "Slow", 4, 8)]
    [InlineData(RuleSets.Gf360, "", "Fast Aura", 8, 16)]
    [InlineData(RuleSets.Gf351, "Slow", "Fast Aura", 6, 12)]
    public void A_unit_moves_as_fast_as_its_slowest_model(string rules, string squadRule, string heroRule, double advance, double rush)
    {
        PropertyInstance[] Of(string r) => r == "" ? [] : [P(r)];
        var a = Squad(Of(squadRule), Of(heroRule));
        Assert.Equal(advance, Allowance(rules, a, GfNames.AdvanceSeq, GfNames.Advance));
        Assert.Equal(rush, Allowance(rules, a, GfNames.RushSeq, GfNames.Rush));
        var threat = Threat(rules, a);
        Assert.Equal((rush, advance), (threat.Charge, threat.ShootMove));
    }

    [Theory]
    [InlineData(RuleSets.Gf351, "Immobile", new[] { GfNames.Hold })]
    [InlineData(RuleSets.Gf360, "Immobile", new[] { GfNames.Hold })]
    [InlineData(RuleSets.Gf351, "Artillery", new[] { GfNames.Hold })]
    [InlineData(RuleSets.Gf360, "Cumbersome", new[] { GfNames.Hold, GfNames.Advance })]
    public void One_models_action_restriction_holds_its_whole_unit(string rules, string rule, string[] actions)
    {
        var a = Squad([], [P(rule)]);
        var state = State([a], [Unit("E", "4+", "4+", [], 1, [])], x1: -20, x2: 20);
        var props = rules == RuleSets.Gf360 ? TestProperties.Core36 : TestProperties.Core;
        if (rules == RuleSets.Gf360) state = state.Under(RuleSets.Gf360);
        Assert.Equal(actions, GrimdarkFutureRules.ActionsFor(state, props, state.Projection.Units[a.Id], props.Actions.Select(x => x.Name).ToList()));
        Assert.Equal(0, Threat(rules, a).ShootMove);
    }
}
