using System.Text.Json.Nodes;

namespace Tabletop.Client.Tests;

/// <summary>The same three literal cases are in viewer/tests/callNormalise.test.ts.</summary>
public class CallNormaliseTests
{
    static string Run(string input, params string[] known) =>
        CallNormalise.Text(CallNormalise.Of(JsonNode.Parse(input)!, new HashSet<string>(known)));

    [Fact]
    public void Nulls_go_numbers_round_keys_sort() =>
        Assert.Equal(
            """{"kind":"add_setup_marker","marker":{"diameter":3,"id":"new:1","kind":"objective","position":{"x":0.3,"y":0,"z":3},"visual":{"builtin":"objective-ring"},"yaw":{"degrees":0}}}""",
            Run("""{"kind":"add_setup_marker","marker":{"id":"019a0000-0000-7000-8000-00000000000f","kind":"objective","position":{"x":0.30000000000000004,"y":-0.0,"z":3},"yaw":{"degrees":0},"diameter":3,"label":null,"visual":{"builtin":"objective-ring"}}}"""));

    [Fact]
    public void Fresh_ids_are_numbered_in_canonical_order() =>
        Assert.Equal("""{"a":["new:1","new:2"],"z":"new:2"}""",
            Run("""{"z":"019a0000-0000-7000-8000-0000000000aa","a":["019a0000-0000-7000-8000-0000000000bb","019a0000-0000-7000-8000-0000000000aa"]}"""));

    [Fact]
    public void Known_ids_stay_and_keys_are_checked_too() =>
        Assert.Equal("""{"counts":{"019a0000-0000-7000-8000-000000000001":2},"kind":"allocated"}""",
            Run("""{"counts":{"019a0000-0000-7000-8000-000000000001":2},"kind":"allocated"}""", "019a0000-0000-7000-8000-000000000001"));

    [Fact]
    public void KnownIds_finds_every_guid_in_the_texts()
    {
        var ids = CallNormalise.KnownIds(
            """{"a":"019A0000-0000-7000-8000-000000000001","b":"019a0000-0000-7000-8000-000000000002"}""",
            """["019a0000-0000-7000-8000-000000000003"]""");
        Assert.Equal(3, ids.Count);
        Assert.Contains("019a0000-0000-7000-8000-000000000001", ids);
    }
}
