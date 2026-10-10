using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class PlayedFlagTests
{
    const string Doc = """
    { "name": "Doc", "gameRules": "grimdark-future", "rolls": [ "hit" ],
      "entries": [
        { "name": "Banner", "kind": "model", "aggregation": "any", "ruleText": "", "played": false },
        { "name": "Shown", "kind": "model", "aggregation": "all", "ruleText": "Shown.", "played": true }
      ] }
    """;

    [Fact]
    public void Played_false_is_read_and_played_true_reads_as_omitted()
    {
        var doc = PropertiesJson.Parse(Doc);
        Assert.False(doc.Entries.Single(e => e.Name == "Banner").Played);
        Assert.Null(doc.Entries.Single(e => e.Name == "Shown").Played);
        var omitted = PropertiesJson.Parse(Doc.Replace(", \"played\": true", ""));
        Assert.Equal(PropertiesJson.Hash(doc), PropertiesJson.Hash(omitted));
        Assert.DoesNotContain("played", PropertiesJson.EntryCanonical(omitted.Entries.Single(e => e.Name == "Shown")));
    }

    [Fact]
    public void A_set_carries_played_onto_its_entries()
    {
        var set = PropertiesJson.ToSet(PropertiesJson.Parse(Doc), Guid.CreateVersion7);
        Assert.False(set.Entry("Banner")!.Played);
        Assert.True(set.Entry("Shown")!.Played);
    }

    [Fact]
    public void Played_false_on_an_entry_with_effects_is_refused()
    {
        var bad = PropertiesJson.Parse(Doc.Replace("\"ruleText\": \"\", \"played\": false",
            "\"ruleText\": \"\", \"played\": false, \"effects\": [ { \"subject\": \"self\", \"ordinal\": 1, \"kind\": \"modify_roll\", \"targetRoll\": \"hit\", \"amount\": 1 } ]"));
        Assert.Contains("Banner: a text-only entry (played false) has no effects", PropertiesJson.Validate(bad));
    }

    [Fact]
    public void Gather_skips_an_unplayed_entry_as_it_skips_an_absent_name()
    {
        var unplayed = new EntryDocument("Not Yet", CatalogKind.Model, false, Aggregation.Any, null, null, "Not yet.", null, [], Played: false);
        var props = TestProperties.WithSpells(unplayed);
        var a = Unit("A", "4+", "4+", [P("Not Yet")], 1, [P("Not Yet")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        var live = LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, []));
        Assert.False(live.Has(Side.Attacker, "Not Yet"));
    }
}
