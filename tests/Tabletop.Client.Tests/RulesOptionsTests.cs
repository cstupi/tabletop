using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>The lobby's optional-rules controls, read from the ruleset's catalog (optional-rules spec → Lobby). The keys here are made up: the client names no rule.</summary>
public class RulesOptionsTests
{
    static readonly RulesOptionCatalog Catalog = new(
    [
        new RulesOption("a", "Option A", "p.1", [new RulesOptionValue("x", "X"), new RulesOptionValue("y", "Y")], "x"),
        new RulesOption("b", "Option B", "p.2", [new RulesOptionValue("on", "On"), new RulesOptionValue("off", "Off")], "on"),
    ],
    [
        new RulesPreset("Plain", new Dictionary<string, string>(), null),
        new RulesPreset("Spicy", new Dictionary<string, string> { ["a"] = "y", ["b"] = "off" }, Enforcement.Strict),
    ]);

    static Dictionary<string, string> Map(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void An_absent_key_reads_as_its_options_default()
    {
        Assert.Equal("x", RulesOptions.ValueOf(Catalog.Options[0], Map()));
        Assert.Equal("off", RulesOptions.ValueOf(Catalog.Options[1], Map(("b", "off"))));
    }

    [Fact]
    public void The_preset_is_the_one_whose_map_reads_the_same_else_custom()
    {
        Assert.Equal("Plain", RulesOptions.PresetOf(Catalog, Map()));
        Assert.Equal("Plain", RulesOptions.PresetOf(Catalog, Map(("a", "x"))));                     // a default written out is still the default
        Assert.Equal("Spicy", RulesOptions.PresetOf(Catalog, Map(("b", "off"), ("a", "y"))));
        Assert.Equal(RulesOptions.Custom, RulesOptions.PresetOf(Catalog, Map(("a", "y"))));
    }

    [Fact]
    public void The_map_sent_drops_every_value_at_its_default()
    {
        Assert.Equal(Map(("b", "off")), RulesOptions.MapOf(Catalog, Map(("a", "x"), ("b", "off"))));
        Assert.Empty(RulesOptions.MapOf(Catalog, Map()));
    }
}
