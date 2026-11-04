using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6 Enhanced Wolfborn: "If this model has Wolfborn, it may move by up to 2D3" from Wolfborn (instead of only D3")." A dice move rolls its row's
/// count plus the instance's parameter, so `modify_parameter Wolfborn +1` conditioned `holds_property self Wolfborn` rolls two dice.
/// </summary>
public class GfEnhancedMoves36Tests
{
    static readonly EntryDocument Enhanced = new("Test Enhanced Wolfborn", CatalogKind.Model, false, Aggregation.None, null, null, "test",
        null, [new EffectDocument(EffectSubject.Self, 10, EffectKind.ModifyParameter, null, null, "Wolfborn", null, null, null, null, 1, false, null, null, null,
            EffectDuration.Interaction, [new ConditionDocument(ConditionKind.HoldsProperty, EffectSubject.Self, Comparison.Eq, null, ["Wolfborn"])], [])]);

    static readonly PropertySet Props = PropertiesJson.ToSet(TestProperties.CoreDocument36 with { Entries = [.. TestProperties.CoreDocument36.Entries, Enhanced] }, Guid.CreateVersion7);

    static (Sim Sim, Unit Unit) Start(bool enhanced, params int[] dice)
    {
        var a = Unit("A", "4+", "4+", enhanced ? [P(Enhanced.Name)] : [], 3, []);
        a = a with { Models = [a.Models[0] with { Properties = [P("Wolfborn")] }, a.Models[1] with { Properties = [P("Wolfborn")] }, a.Models[2]] };
        var state = State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20).Under(RuleSets.Gf360);
        var sim = new Sim(state, GrimdarkFutureRules.V360, Props, new ScriptedDice(dice));
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        return (sim, a);
    }

    [Theory]
    [InlineData(true, "Wolfborn: move up to 2D3\"")]
    [InlineData(false, "Wolfborn: move up to D3\"")]
    public void The_offer_reads_the_parameter_in_its_label(bool enhanced, string label)
    {
        var (sim, a) = Start(enhanced, 5, 3);
        Assert.Equal(label, Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal).Choices.Single(c => c.Id == $"Wolfborn|{a.Id:N}").Label);
    }

    [Theory]
    [InlineData(true, 2, 5.0)]     // 5 → 3, 3 → 2
    [InlineData(false, 1, 3.0)]
    public void The_reposition_rolls_one_die_more_per_parameter_and_moves_the_carriers(bool enhanced, int count, double allowance)
    {
        var (sim, a) = Start(enhanced, 5, 3);
        sim.Answer(Samples.Team1, new Chosen($"Wolfborn|{a.Id:N}"));
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal(allowance, region.Allowance);
        Assert.Equal([a.Models[0].Id, a.Models[1].Id], region.ModelIds);
        var roll = Assert.Single(sim.Steps("reposition").SelectMany(s => s.Rolls));
        Assert.Equal(("Wolfborn", count), (roll.Roll, roll.Dice.Count));
    }
}
