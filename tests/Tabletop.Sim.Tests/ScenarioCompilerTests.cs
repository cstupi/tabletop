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
    public void A_3_6_scenario_compiles_against_the_3_6_set_and_stamps_its_snapshots_3_6()
    {
        var compiled = ScenarioCompiler.Compile(TwoArmies(RuleSets.Gf360));
        var set = ScenarioCompiler.CorePropertiesOf(RuleSets.Gf360);
        Assert.Same(set, compiled.Properties);
        Assert.Equal((RuleSets.Gf360, set.ContentHash, set.Name), (compiled.Info.GameRules, compiled.Info.PropertiesHash, compiled.Info.RuleSet));
        Assert.All(compiled.Board.Armies, a => Assert.Equal(ArmyEditions.V36, a.Snapshot.Edition));

        var plain = ScenarioCompiler.Compile(TwoArmies());     // a scenario that names no rules stays on 3.5.1
        Assert.Equal(RuleSets.Gf351, plain.Scenario.Rules);
        Assert.Same(ScenarioCompiler.CoreProperties, plain.Properties);
        Assert.Equal((RuleSets.Gf351, ScenarioCompiler.CoreProperties.Name), (plain.Info.GameRules, plain.Info.RuleSet));
        Assert.All(plain.Board.Armies, a => Assert.Equal(ArmyEditions.V35, a.Snapshot.Edition));
    }
}
