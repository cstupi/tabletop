using System.Text.Json.Nodes;
using Tabletop.Content;

namespace Tabletop.Sim.Lifecycle;

/// <summary>
/// The armies a lifecycle seat imports, one per army book. Edition 3.5: book 0 is management's Army Forge fixture (Wormhole Daemons of War,
/// list c2-6YsWRH3py), embedded, cut to the first unit of each name that joins no other (Blood Warriors, Blood Hounds, Beast Riders: 13
/// models), so four rounds stay quick; books 1..3 are export bodies of three units each cut from the official books (sim/Lifecycle/Armies/):
/// Human Defense Force, Battle Brothers, Orc Marauders. Edition 3.6 reads sim/Lifecycle/Armies/3.6/: the same four books, three units each at
/// their default loadout, cut from the 3.6 Army Forge dump (book 0 is Daemons of War: Blood Warriors, Blood Hounds, Blood Beast Riders).
/// </summary>
public static class LifecycleArmy
{
    /// <summary>What the army records as its source.</summary>
    public const string Source = "lifecycle";
    /// <summary>Book 0's unit an ambusher shape sets aside, by army edition.</summary>
    public static string AmbushUnit(string edition) => edition == ArmyEditions.V36 ? "Blood Beast Riders" : "Beast Riders";
    const string Resource = "lifecycle-army-forge-list.json";

    /// <summary>The army book uid of each list, by index.</summary>
    public static IReadOnlyList<string> Books { get; } = ["prsajf5ot936qznk", "z65fgu0l29i4lnlu", "78qp9l5alslt6yj8", "1wj1ysgxpuuz9bc7"];

    /// <summary>
    /// The export body of the book's list in the army edition; with `ambush` (book 0 only), its riders (AmbushUnit) also carry Ambush, as an
    /// Army Forge list names a rule.
    /// </summary>
    public static JsonObject List(int book = 0, bool ambush = false, string edition = ArmyEditions.V36)
    {
        if (ambush && book != 0) throw new ArgumentException("only book 0 has the ambushing riders", nameof(ambush));
        var list = edition == ArmyEditions.V36 ? Read($"lifecycle-army-3.6-{Books[book]}.json")
            : book != 0 ? Read($"lifecycle-army-{Books[book]}.json")
            : FirstOfEachName(Read(Resource));
        if (ambush)
            foreach (var unit in list["units"]!.AsArray().Select(u => u!.AsObject()).Where(u => (string)u["name"]! == AmbushUnit(edition)))
                unit["rules"]!.AsArray().Add(new JsonObject { ["id"] = "6mJw5IdqSqNC", ["name"] = "Ambush", ["label"] = "Ambush" });
        return list;
    }

    /// <summary>
    /// Whole Army Forge lists (export bodies, sim/Lifecycle/Armies/3.6/lists/), 1000 points each, imported as they stand: joined Heroes,
    /// combined units, upgrades and all. By the name a Seat gives.
    /// </summary>
    public static IReadOnlyList<string> Lists { get; } = ["goblin-reclaimers", "war-disciples", "hdf-artillery-blob", "ratmen-clans"];

    /// <summary>A whole list by name. Every list is a 3.6 one.</summary>
    public static JsonObject Whole(string name) =>
        Lists.Contains(name) ? Read($"lifecycle-list-3.6-{name}.json") : throw new ArgumentException($"no lifecycle list {name}; known: {string.Join(", ", Lists)}", nameof(name));

    /// <summary>The export body a seat imports: its whole list when it names one, else its book's cut (List).</summary>
    public static JsonObject For(Seat seat, bool ambush, string edition)
    {
        if (seat.List is not { } name) return List(seat.Book, ambush, edition);
        if (edition != ArmyEditions.V36) throw new ArgumentException($"the whole lists are 3.6; the shape plays edition {edition}", nameof(edition));
        if (ambush) throw new ArgumentException("a whole list keeps its own rules", nameof(ambush));
        return Whole(name);
    }

    /// <summary>The units an import of the body holds besides reserve copies: one per entry that joins no other (a joined Hero and a
    /// combined unit's second half are one unit with the entry they join).</summary>
    public static int UnitsOf(JsonObject list) => list["units"]!.AsArray().Count(u => u!["joinToUnit"] is null);

    /// <summary>The list cut to the first unit of each name that joins no other.</summary>
    static JsonObject FirstOfEachName(JsonObject list)
    {
        var seen = new HashSet<string>();
        var kept = new JsonArray();
        foreach (var unit in list["units"]!.AsArray().Select(u => u!.AsObject()))
            if (unit["joinToUnit"] is null && seen.Add((string)unit["name"]!)) kept.Add(unit.DeepClone());
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
