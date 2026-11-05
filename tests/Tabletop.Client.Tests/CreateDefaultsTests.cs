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
}
