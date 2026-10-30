using System.Text.Json.Nodes;
using Tabletop.Content;

namespace Tabletop.Sim.Lifecycle;

/// <summary>
/// The armies a lifecycle seat imports, one per army book. Book 0 is management's Army Forge fixture (Wormhole Daemons of War, list
/// c2-6YsWRH3py), embedded, cut to the first unit of each name that joins no other (Blood Warriors, Blood Hounds, Beast Riders: 13 models),
/// so four rounds stay quick. Books 1..3 are export bodies of three units each cut from the official books (sim/Lifecycle/Armies/):
/// Human Defense Force, Battle Brothers, Orc Marauders. `edition` "3.6" reads Lifecycle/Armies/3.6/: Orc Marauders (book 3) cut from the 3.6 Army Forge dump.
/// </summary>
public static class LifecycleArmy
{
    /// <summary>What the army records as its source.</summary>
    public const string Source = "lifecycle";
    public const string AmbushUnit = "Beast Riders";
    const string Resource = "lifecycle-army-forge-list.json";

    /// <summary>The army book uid of each list, by index.</summary>
    public static IReadOnlyList<string> Books { get; } = ["prsajf5ot936qznk", "z65fgu0l29i4lnlu", "78qp9l5alslt6yj8", "1wj1ysgxpuuz9bc7"];

    /// <summary>The export body of the book's list; with `ambush` (book 0 only), the Beast Riders also carry Ambush (p.13), as an Army Forge list names a rule.</summary>
    public static JsonObject List(int book = 0, bool ambush = false, string edition = ArmyEditions.V35)
    {
        if (edition == ArmyEditions.V36)
        {
            if (book != 3 || ambush) throw new ArgumentException("only book 3 has a 3.6 list, and no Ambush", nameof(book));
            return Read($"lifecycle-army-3.6-{Books[book]}.json");
        }
        if (book != 0)
        {
            if (ambush) throw new ArgumentException("only book 0 has Beast Riders", nameof(ambush));
            return Read($"lifecycle-army-{Books[book]}.json");
        }
        var list = Read(Resource);
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

    static JsonObject Read(string resource)
    {
        using var stream = typeof(LifecycleArmy).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"the embedded resource {resource} is missing");
        return JsonNode.Parse(stream)!.AsObject();
    }
}
