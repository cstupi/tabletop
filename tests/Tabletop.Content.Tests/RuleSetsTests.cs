namespace Tabletop.Content.Tests;

public class RuleSetsTests
{
    [Fact]
    public void Editions_map_to_ids_both_ways()
    {
        Assert.Equal("3.6", RuleSets.EditionOf(RuleSets.Gf360));
        Assert.Equal("3.5", RuleSets.EditionOf(RuleSets.Gf351));
        Assert.Equal(RuleSets.Gf351, RuleSets.RulesIdOf("3.5"));
        Assert.Equal(RuleSets.Gf360, RuleSets.RulesIdOf("3.6"));
        Assert.Throws<ArgumentException>(() => RuleSets.EditionOf("x"));
        Assert.Throws<ArgumentException>(() => RuleSets.RulesIdOf("3.7"));
    }

    [Fact]
    public void New_games_default_to_3_6() => Assert.Equal("grimdark-future-3.6", RuleSets.Default);
}
