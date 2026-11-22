using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>GfOptions: the optional rules a Grimdark Future game opted into, parsed from GameInfo.OptionalRules (optional-rules spec → How the rules read it).</summary>
public class GfOptionsTests
{
    static Dictionary<string, string> Map(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void An_empty_map_is_the_core_game() => Assert.Equal(GfOptions.Core, GfOptions.Parse(Map()));

    /// <summary>p.5 "Models can't see through solid obstacles, including the perimeter of other units (friendly or enemy)": the core game blocks with
    /// each other unit's perimeter (owner, 2026-11-07: a unit in between blocks); each model blocking alone is the option.</summary>
    [Fact]
    public void The_core_game_blocks_sight_with_each_units_perimeter()
    {
        Assert.Equal(GfUnitSightBlocking.Perimeter, GfOptions.Core.UnitSightBlocking);
        Assert.Equal(GfUnitSightBlocking.PerModel, GfOptions.Parse(Map(("unit_sight_blocking", "per_model"))).UnitSightBlocking);
    }

    [Fact]
    public void Every_key_and_value_parses()
    {
        Assert.Equal(new GfOptions(GfSightMethod.HybridTrue, GfUnitSightBlocking.Perimeter, MeleeSeparation: false),
            GfOptions.Parse(Map(("line_of_sight", "hybrid_true"), ("unit_sight_blocking", "perimeter"), ("melee_separation", "off"))));
        Assert.Equal(GfOptions.Core, GfOptions.Parse(Map(("line_of_sight", "core"), ("unit_sight_blocking", "perimeter"), ("melee_separation", "on"))));
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
            ("unit_sight_blocking", "Unit sight blocking", "perimeter", "perimeter,per_model"),
            ("line_of_sight", "Line of sight method", "core", "core,hybrid_true"),
            ("melee_separation", "Melee separation", "on", "on,off"),
        ], catalog.Options.Select(o => (o.Key, o.Label, o.Default, string.Join(",", o.Values.Select(v => v.Value)))));
        Assert.Contains(catalog.Options.SelectMany(o => o.Values), v => v.Label == "Hybrid True Line of Sight");
        Assert.Equal([("Core rules", 0, (Enforcement?)null)], catalog.Presets.Select(p => (p.Name, p.OptionalRules.Count, p.Enforcement)));
        Assert.Empty(GrimdarkFutureRules.Instance.OptionProblems(Map(("melee_separation", "off"))));
        Assert.NotEmpty(GrimdarkFutureRules.Instance.OptionProblems(Map(("melee_separation", "maybe"))));
    }

    /// <summary>The lobby shows each option's and each value's description as its tooltip: every one is written, in both editions, and none cites a page.</summary>
    [Fact]
    public void Every_option_and_value_carries_a_description_in_both_editions()
    {
        foreach (var rules in new[] { GrimdarkFutureRules.V351, GrimdarkFutureRules.V360 })
        {
            Assert.NotEmpty(rules.OptionCatalog.Options);
            var texts = rules.OptionCatalog.Options.SelectMany(o => o.Values.Select(v => v.Description).Prepend(o.Description)).ToList();
            Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
            Assert.All(texts, t => Assert.DoesNotMatch(@"\bp\.\s?\d", t));
        }
    }
}
