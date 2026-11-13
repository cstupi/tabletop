using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Army rules at the end of an activation.</summary>
public class ArmyRuleAuditActivationTests
{
    [Fact]
    public void Ambush_re_deployment_is_offered_at_the_end_of_an_activation_made_through_coordinate()
    {
        // "When a unit where all models have this rule ends its activation, you may remove it from the table …"
        var a = Unit("A", "4+", "4+", [], 1, [P("Coordinate")]); var b = Unit("B", "4+", "4+", [], 1, [P("Ambush Re-Deployment")]);
        var state = State([a, b], [Unit("E", "4+", "4+", [], 1, [])]);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ActionRecorded(a.Id, GfNames.Hold), null));
        var seqs = GfActivationEnd.Sequences(state, TestProperties.Core, new Activation(a.Id, GfNames.Hold, true, []));
        var sim = new Sim(state, GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice([4, 4, 4]));
        sim.Start(GfNames.CoordinateSeq, seqs.Single(s => s.Sequence == GfNames.CoordinateSeq).Context);
        sim.Answer(Samples.Team1, new Chosen(ActivationOption.Id(b.Id, GfNames.Hold)));
        var offered = new List<string>();
        for (var i = 0; i < 10 && sim.AskForOrNull(Samples.Team1) is { } ask; i++)
        {
            if (ask is { Question: AskQuestions.React, Legal: Options o }) offered.AddRange(o.Choices.Select(c => c.Id));
            sim.Answer(Samples.Team1, ask.AllowPass ? new Passed() : new Chosen("pass"));
        }
        Assert.Contains($"{GfAmbush.AmbushReDeployment}|{b.Id:N}", offered);
    }
}
