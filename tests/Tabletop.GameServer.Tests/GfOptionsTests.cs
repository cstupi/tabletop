using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>GfOptions: the optional rules a Grimdark Future game opted into, parsed from GameInfo.OptionalRules (optional-rules spec → How the rules read it).</summary>
public class GfOptionsTests
{
    static Dictionary<string, string> Map(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void An_empty_map_is_the_core_game() => Assert.Equal(GfOptions.Core, GfOptions.Parse(Map()));

    [Fact]
    public void Every_key_and_value_parses()
    {
        Assert.Equal(new GfOptions(GfSightMethod.HybridTrue, GfUnitSightBlocking.Perimeter, MeleeSeparation: false),
            GfOptions.Parse(Map(("line_of_sight", "hybrid_true"), ("unit_sight_blocking", "perimeter"), ("melee_separation", "off"))));
        Assert.Equal(GfOptions.Core, GfOptions.Parse(Map(("line_of_sight", "core"), ("unit_sight_blocking", "per_model"), ("melee_separation", "on"))));
        Assert.Empty(GfOptions.Problems(Map(("line_of_sight", "hybrid_true"), ("unit_sight_blocking", "perimeter"), ("melee_separation", "off"))));
    }

    [Fact]
    public void An_unknown_key_or_value_is_a_problem()
    {
        Assert.Equal(["unknown option fog_of_war", "line_of_sight has no value top_down"],
            GfOptions.Problems(Map(("fog_of_war", "on"), ("line_of_sight", "top_down"))));
    }

    [Fact]
    public void The_game_state_reads_its_info()
    {
        var state = Infrastructure.Armies.State([], []);
        state.Info = state.Info with { OptionalRules = Map(("melee_separation", "off")) };
        Assert.False(GfOptions.Of(state).MeleeSeparation);
    }

    [Fact]
    public void The_catalog_names_each_option_by_the_books_words_and_page_and_offers_the_core_preset()
    {
        var catalog = GrimdarkFutureRules.Instance.OptionCatalog;
        Assert.Equal(
        [
            ("unit_sight_blocking", "Unit sight blocking", "p.5", "per_model", "per_model,perimeter"),
            ("line_of_sight", "Line of sight method", "p.56", "core", "core,hybrid_true"),
            ("melee_separation", "Melee separation", "p.10", "on", "on,off"),
        ], catalog.Options.Select(o => (o.Key, o.Label, o.Page, o.Default, string.Join(",", o.Values.Select(v => v.Value)))));
        Assert.Contains(catalog.Options.SelectMany(o => o.Values), v => v.Label == "Hybrid True Line of Sight");
        Assert.Equal([("Core rules", 0, (Enforcement?)null)], catalog.Presets.Select(p => (p.Name, p.OptionalRules.Count, p.Enforcement)));
        Assert.Empty(GrimdarkFutureRules.Instance.OptionProblems(Map(("melee_separation", "off"))));
        Assert.NotEmpty(GrimdarkFutureRules.Instance.OptionProblems(Map(("melee_separation", "maybe"))));
    }
}
