using Tabletop.Content;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class ScenarioCompilerTests
{
    [Fact]
    public void Core_properties_exist_for_both_editions_and_differ_in_id()
    {
        Assert.Equal(RuleSets.Gf360, ScenarioCompiler.CorePropertiesOf(RuleSets.Gf360).GameRules);
        Assert.Equal(RuleSets.Gf351, ScenarioCompiler.CorePropertiesOf(RuleSets.Gf351).GameRules);
        Assert.Equal(ScenarioCompiler.CoreProperties.ContentHash, ScenarioCompiler.CorePropertiesOf(RuleSets.Gf351).ContentHash);
        Assert.Same(ScenarioCompiler.CorePropertiesOf(RuleSets.Gf360), ScenarioCompiler.CorePropertiesOf(RuleSets.Gf360));
    }

    static Scenario TwoArmies(string? rules = null)
    {
        var b = new ScenarioBuilder("edition", 1).Army("Red", 1, 1).Army("Blue", 1, 1);
        return (rules is null ? b : b.Rules(rules)).Build();
    }

    [Fact]
    public void A_scenario_plays_3_6_unless_it_names_3_5_1_and_compiles_against_its_editions_set()
    {
        var plain = ScenarioCompiler.Compile(TwoArmies());     // a scenario that names no rules plays 3.6.0
        var set = ScenarioCompiler.CorePropertiesOf(RuleSets.Gf360);
        Assert.Equal(RuleSets.Gf360, plain.Scenario.Rules);
        Assert.Same(set, plain.Properties);
        Assert.Equal((RuleSets.Gf360, set.ContentHash, set.Name), (plain.Info.GameRules, plain.Info.PropertiesHash, plain.Info.RuleSet));
        Assert.All(plain.Board.Armies, a => Assert.Equal(ArmyEditions.V36, a.Snapshot.Edition));

        var v351 = ScenarioCompiler.Compile(TwoArmies(RuleSets.Gf351));
        Assert.Same(ScenarioCompiler.CoreProperties, v351.Properties);
        Assert.Equal((RuleSets.Gf351, ScenarioCompiler.CoreProperties.Name), (v351.Info.GameRules, v351.Info.RuleSet));
        Assert.All(v351.Board.Armies, a => Assert.Equal(ArmyEditions.V35, a.Snapshot.Edition));
    }
}
