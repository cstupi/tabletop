using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Piercing Spotter: "When attacking, friendly units may remove tokens from their target before rolling to block to get +AP(X) on their weapons,
/// where X is the number of removed tokens." The attacking team picks how many.</summary>
public class ArmyRuleAuditSpotterTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("0", 0)]
    public void The_attacking_team_picks_how_many_tokens_to_remove(string answer, int recorded)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var props = TestProperties.Core36;
        var row = props.Entry("Piercing Spotter")!.Effects[0].Children[0];
        var rules = new WithSequences(GrimdarkFutureRules.V360, new Sequence("test-tokens", [new GfChoice.SpotterTokensStep()]));
        var sim = new Sim(State([a], [b], x1: -4, x2: 4).Under(RuleSets.Gf360), rules, props, new ScriptedDice(4));
        sim.Append([new GrantAdded(b.Id, new Grant(Guid.CreateVersion7(), row.Id, "Piercing Spotter", a.Id, 1)), new GrantAdded(b.Id, new Grant(Guid.CreateVersion7(), row.Id, "Piercing Spotter", a.Id, 1))]);
        sim.Start("test-tokens", new SequenceContext(a.Id, [b.Id], [], GfNames.Hold));
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal((GfChoice.RemoveTokens, "Piercing Spotter: remove how many of 2 tokens?"), (ask.Question, ask.Prompt));
        Assert.Equal(["0", "1", "2"], ((Options)ask.Legal).Choices.Select(c => c.Id));
        sim.Answer(Samples.Team1, new Chosen(answer));
        Assert.Equal(recorded, sim.Steps("spotter-tokens").Single().Values?.GetValueOrDefault("tokens_Piercing Spotter") ?? 0);
    }
}
