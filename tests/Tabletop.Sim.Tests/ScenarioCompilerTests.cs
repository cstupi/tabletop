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
}
