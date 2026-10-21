using System.Text.Json.Nodes;

namespace Tabletop.Sim.Lifecycle;

/// <summary>
/// The army every lifecycle seat imports: management's Army Forge fixture (Wormhole Daemons of War, list c2-6YsWRH3py), embedded, cut to
/// the first unit of each name that joins no other (Blood Warriors, Blood Hounds, Beast Riders: 13 models), so four rounds stay quick.
/// </summary>
public static class LifecycleArmy
{
    /// <summary>What the army records as its source.</summary>
    public const string Source = "lifecycle";
    public const string AmbushUnit = "Beast Riders";
    const string Resource = "lifecycle-army-forge-list.json";

    /// <summary>The export body; with `ambush`, the Beast Riders also carry Ambush (p.13), as an Army Forge list names a rule.</summary>
    public static JsonObject List(bool ambush = false)
    {
        using var stream = typeof(LifecycleArmy).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"the embedded resource {Resource} is missing");
        var list = JsonNode.Parse(stream)!.AsObject();
        var seen = new HashSet<string>();
        var kept = new JsonArray();
        foreach (var unit in list["units"]!.AsArray().Select(u => u!.AsObject()))
        {
            var name = (string)unit["name"]!;
            if (unit["joinToUnit"] is not null || !seen.Add(name)) continue;
            if (ambush && name == AmbushUnit) unit["rules"]!.AsArray().Add(new JsonObject { ["id"] = "6mJw5IdqSqNC", ["name"] = "Ambush", ["label"] = "Ambush" });
            kept.Add(unit.DeepClone());
        }
        list["units"] = kept;
        return list;
    }
}
