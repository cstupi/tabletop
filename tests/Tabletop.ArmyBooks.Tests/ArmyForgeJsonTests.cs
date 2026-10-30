using Tabletop.ArmyBooks;
using Xunit;

namespace Tabletop.ArmyBooks.Tests;

public class ArmyForgeJsonTests
{
    [Fact] public void Edition_is_major_minor_of_the_index_versions()
        => Assert.Equal("3.6", ArmyForgeJson.EditionOf([new("a", "A", "3.6.0"), new("b", "B", "3.6.0")]));

    [Fact] public void Patch_versions_of_one_edition_agree()
        => Assert.Equal("3.5", ArmyForgeJson.EditionOf([new("a", "A", "3.5.2"), new("b", "B", "3.5.3")]));

    [Fact] public void An_entry_without_a_version_is_tolerated_when_another_fixes_the_edition()
        => Assert.Equal("3.5", ArmyForgeJson.EditionOf([new("a", "A", "3.5.3"), new("b", "B", null)]));

    [Fact] public void Mixed_editions_or_no_version_at_all_are_refused()
    {
        Assert.Throws<InvalidDataException>(() => ArmyForgeJson.EditionOf([new("a", "A", "3.5.3"), new("b", "B", "3.6.0")]));
        Assert.Throws<InvalidDataException>(() => ArmyForgeJson.EditionOf([new("a", "A", null)]));
    }
}
