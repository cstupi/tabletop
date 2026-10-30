using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 Rapid Blink, Wolfborn, Bounding, Dash: "you may move all models with this rule in it by up to D3"" (D3+1"); 3.5.1's "place ... anywhere fully
/// within D3"". A move row with dice rolls the distance once chosen, as the placement does, and asks a move of the holding models.
/// </summary>
public class GfDiceMoves36Tests
{
    [Theory]
    [InlineData(RuleSets.Gf351, "Rapid Blink", true, 3.0)]
    [InlineData(RuleSets.Gf360, "Rapid Blink", false, 3.0)]
    [InlineData(RuleSets.Gf360, "Wolfborn", false, 3.0)]
    [InlineData(RuleSets.Gf351, "Bounding", true, 4.0)]
    [InlineData(RuleSets.Gf360, "Bounding", false, 4.0)]
    public void An_activation_start_rule_rolls_its_distance_then_moves_or_places_the_holders(string rules, string rule, bool placement, double allowance)
    {
        var a = Unit("A", "4+", "4+", [], 3, []);
        a = a with { Models = [a.Models[0] with { Properties = [P(rule)] }, a.Models[1] with { Properties = [P(rule)] }, a.Models[2]] };
        var sim = Sim.Gf(rules, State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 5);
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Chosen($"{rule}|{a.Id:N}"));
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((allowance, placement), (region.Allowance, region.PassesModels));                // a 5 halved and rounded up, +1 for D3+1
        Assert.Equal([a.Models[0].Id, a.Models[1].Id], region.ModelIds);
        var roll = Assert.Single(sim.Steps("reposition").SelectMany(s => s.Rolls));
        Assert.Equal((rule, 1), (roll.Roll, roll.Dice.Count));
    }

    [Fact]
    public void A_rolled_move_moves_the_holders_up_to_the_roll()
    {
        var a = Unit("A", "4+", "4+", [], 2, []);
        a = a with { Models = [a.Models[0] with { Properties = [P("Rapid Blink")] }, a.Models[1]] };
        var sim = Sim.Gf36(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 3);   // D3 = 2
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Chosen($"Rapid Blink|{a.Id:N}"));
        Assert.Equal(2.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        var m = a.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { Y = at.Position.Y + 1.5 } }))]));
        Assert.Equal(at.Position.Y + 1.5, ((OnTable)sim.Model(m).Location).Placement.Position.Y, 3);
        Assert.Contains(sim.Steps("reposition"), s => s.Applied?.Contains("Rapid Blink") ?? false);
    }

    [Theory]
    [InlineData(RuleSets.Gf351, "Dash: place within D3+1\"", true)]
    [InlineData(RuleSets.Gf360, "Dash: move up to D3+1\"", false)]
    public void Dash_rolls_d3_plus_one_at_the_end_of_the_activation(string rules, string label, bool placement)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Dash")]);
        var sim = Sim.Gf(rules, State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 1);   // D3 = 1, +1
        sim.Start(GfNames.ActivationEndSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        var use = Assert.Single(Assert.IsType<Options>(sim.AskFor(Samples.Team1).Legal).Choices);
        Assert.Equal(label, use.Label);
        sim.Answer(Samples.Team1, new Chosen(use.Id));
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((2.0, placement), (region.Allowance, region.PassesModels));
    }
}
