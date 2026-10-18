using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class SpellTextTests
{
    static readonly RuleTexts Catalog = RuleTexts.From([
        new RuleTextDto("Hex", "spell", false, "Pick one enemy unit within 18\", which takes 2 hits."),
        new RuleTextDto("Blast | Ruin (Wizards)", "spell", false, "Pick up to 3 units.")]);

    [Fact]
    public void An_option_id_names_its_spell_before_the_last_bar()
    {
        Assert.Equal("Hex", SpellText.NameOfOption("Hex|0198d1a0-0000-7000-8000-000000000001"));
        Assert.Equal("Blast | Ruin (Wizards)", SpellText.NameOfOption("Blast | Ruin (Wizards)|m"));
        Assert.Equal("Hex", SpellText.NameOfOption("Hex"));
    }

    [Fact]
    public void A_label_splits_into_name_and_casting_value()
    {
        Assert.Equal(("Hex", (int?)3), SpellText.Split("Hex (3)"));
        Assert.Equal(("Blast | Ruin (Wizards)", (int?)2), SpellText.Split("Blast | Ruin (Wizards) (2)"));
        Assert.Equal(("Blast | Ruin (Wizards)", (int?)null), SpellText.Split("Blast | Ruin (Wizards)"));
    }

    [Fact]
    public void The_tooltip_is_name_value_then_catalog_text()
    {
        Assert.Equal("Hex\nCasting value 3\nPick one enemy unit within 18\", which takes 2 hits.", SpellText.OfOption(Catalog, "Hex|m", "Hex (3)"));
    }

    [Fact]
    public void A_spell_with_no_entry_or_no_catalog_still_names_itself_and_its_value()
    {
        Assert.Equal("Bolt\nCasting value 1", SpellText.Tooltip(Catalog, "Bolt", 1));
        Assert.Equal("Hex", SpellText.Tooltip(RuleTexts.Empty, "Hex"));
        Assert.Null(SpellText.Tooltip(Catalog, ""));
    }
}
