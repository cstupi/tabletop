using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class RuleTextsTests
{
    static readonly RuleTextDto Tough = new("Tough", "unit", true, "Must take X wounds to be killed.");
    static readonly RuleTextDto Ambush = new("Ambush", "unit", false, "May be set aside before deployment.");

    [Fact]
    public void A_catalog_entry_gives_its_text_and_reads_as_automated()
    {
        var rules = RuleTexts.From([Tough, Ambush]);
        Assert.Equal("Must take X wounds to be killed.", rules.TextOf("Tough"));
        Assert.True(rules.IsAutomated("Ambush"));
    }

    [Fact]
    public void A_name_with_no_entry_has_no_text_and_is_not_automated()
    {
        var rules = RuleTexts.From([Tough]);
        Assert.Null(rules.TextOf("Furious"));
        Assert.False(rules.IsAutomated("Furious"));
        Assert.True(rules.NotAutomated("Furious"));
        Assert.False(rules.NotAutomated("Tough"));
    }

    [Fact]
    public void Names_match_as_the_catalog_writes_them()
    {
        var rules = RuleTexts.From([Tough]);
        Assert.Null(rules.TextOf("tough"));
        Assert.Null(rules.TextOf("Tough(3)"));
    }

    [Fact]
    public void Empty_knows_no_rule()
    {
        Assert.Null(RuleTexts.Empty.TextOf("Tough"));
        Assert.False(RuleTexts.Empty.IsAutomated("Tough"));
        Assert.False(RuleTexts.Empty.Loaded);
        Assert.False(RuleTexts.Empty.NotAutomated("Tough"));          // not loaded: no tag, rather than every rule tagged
    }

    [Fact]
    public void A_repeated_name_keeps_the_first_row()
    {
        var rules = RuleTexts.From([Tough, Tough with { RuleText = "other" }]);
        Assert.Equal("Must take X wounds to be killed.", rules.TextOf("Tough"));
    }
}
