using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class CreateDefaultsTests
{
    static PropertiesSummary Pack(string name, bool isDefault) => new("sha256:" + name, name, "grimdark-future", isDefault);

    [Fact]
    public void The_default_set_is_preselected_wherever_it_is_listed()
    {
        Assert.Equal(1, CreateDefaults.DefaultIndex([Pack("Grimdark Future v3.5.1", false), Pack("Grimdark Future v3.6.0", true)]));
        Assert.Equal(0, CreateDefaults.DefaultIndex([Pack("a", false), Pack("b", false)]));
    }

    [Fact]
    public void A_set_is_labelled_by_its_name_only() =>
        Assert.Equal("Grimdark Future v3.6.0", CreateDefaults.PackLabel(Pack("Grimdark Future v3.6.0", true)));

    [Fact]
    public void Each_enforcement_has_its_line_as_the_viewer_words_it()
    {
        Assert.Equal("Moves are never checked.", CreateDefaults.EnforcementHint(Tabletop.Protocol.Enforcement.Silent));
        Assert.Equal("A move that breaks a rule is shown and asked about first.", CreateDefaults.EnforcementHint(Tabletop.Protocol.Enforcement.Warn));
        Assert.Equal("A move that breaks a rule is refused.", CreateDefaults.EnforcementHint(Tabletop.Protocol.Enforcement.Strict));
    }

    [Fact]
    public void The_name_problem_waits_for_the_first_keystroke_or_an_attempt()
    {
        Assert.Null(CreateDefaults.NameShown("", touched: false));
        Assert.NotNull(CreateDefaults.NameShown("", touched: true));
        Assert.Null(CreateDefaults.NameShown("Friday", touched: true));
    }
}
