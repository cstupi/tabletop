using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tabletop.ArmyBooks;

/// <summary>
/// The units a book's Spawn and Split name (army-book rules spec → Readings, batch 7: "a new unit of X"), for the import to add as reserve units. Army Forge
/// gives X as the rule's rating, the unit's name and size: <c>"rating": "Bot Swarms [3]"</c>. Each named unit is written in the TTS export's shape (rules,
/// a loadout of weapons and items, bases), keyed by book uid and unit name, to <c>server/management/Content/Armies/army-book-units.json</c>.
/// </summary>
public static partial class ReserveUnits
{
    public static readonly string[] Rules = ["Spawn", "Split"];
    public const string FileName = "army-book-units.json";

    [GeneratedRegex(@"^(?<name>.+?)\s*\[(?<size>\d+)\]$")] private static partial Regex RatingShape();

    /// <summary>X read off a rating: the unit's name and size; null for any other shape.</summary>
    public static (string Name, int Size)? Parse(string rating) =>
        RatingShape().Match(rating.Trim()) is { Success: true } m ? (m.Groups["name"].Value, int.Parse(m.Groups["size"].Value)) : null;

    /// <summary>The file's text and the ratings that name no unit of their book (any problem fails the run).</summary>
    public static (string Json, IReadOnlyList<string> Problems) Build(IEnumerable<(string Uid, string Name, string BookJson)> books)
    {
        var problems = new List<string>();
        var root = new JsonObject();
        foreach (var (uid, bookName, json) in books.OrderBy(b => b.Uid, StringComparer.Ordinal))
        {
            var book = JsonNode.Parse(json)!.AsObject();
            var units = (book["units"]?.AsArray() ?? []).Select(u => u!.AsObject()).ToList();
            var named = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var rating in Ratings(book))
            {
                if (Parse(rating) is not { } x) { problems.Add($"{bookName}: a Spawn or Split rating \"{rating}\" is not \"Name [size]\""); continue; }
                if (!units.Any(u => (string?)u["name"] == x.Name)) { problems.Add($"{bookName}: \"{rating}\" names no unit of the book"); continue; }
                named.Add(x.Name);
            }
            if (named.Count == 0) continue;
            var entry = new JsonObject();
            foreach (var name in named) entry[name] = Profile(units.First(u => (string?)u["name"] == name));
            root[uid] = entry;
        }
        return (root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", problems);
    }

    /// <summary>Every rating of a Spawn or Split rule anywhere in the book: on units, their items, and upgrade gains.</summary>
    static IEnumerable<string> Ratings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                if (o["name"] is JsonValue n && n.TryGetValue<string>(out var name) && Rules.Contains(name) && o["rating"] is JsonValue r && r.TryGetValue<string>(out var rating))
                    yield return rating;
                foreach (var (_, v) in o) foreach (var x in Ratings(v)) yield return x;
                break;
            case JsonArray a:
                foreach (var v in a) foreach (var x in Ratings(v)) yield return x;
                break;
        }
    }

    static JsonObject Rule(JsonNode? r)
    {
        var o = new JsonObject { ["name"] = (string?)r!["name"] };
        if (r["rating"] is JsonNode rating) o["rating"] = rating.DeepClone();
        return o;
    }

    /// <summary>The book unit in the TTS export's shape: what ArmyForgeImport reads.</summary>
    static JsonObject Profile(JsonObject u)
    {
        var loadout = new JsonArray();
        foreach (var w in u["weapons"]?.AsArray() ?? [])
            loadout.Add(new JsonObject
            {
                ["name"] = ((string?)w!["name"])?.Trim(), ["type"] = "ArmyBookWeapon", ["count"] = (int?)w["count"] ?? 1,
                ["range"] = w["range"]?.DeepClone(), ["attacks"] = w["attacks"]?.DeepClone(),
                ["specialRules"] = new JsonArray([.. (w["specialRules"]?.AsArray() ?? []).Select(Rule)]),
            });
        foreach (var i in u["items"]?.AsArray() ?? [])
            loadout.Add(new JsonObject
            {
                ["name"] = (string?)i!["name"], ["type"] = "ArmyBookItem", ["count"] = (int?)i["count"] ?? 1,
                ["content"] = new JsonArray([.. (i["content"]?.AsArray() ?? []).Select(Rule)]),
            });
        return new JsonObject
        {
            ["size"] = (int?)u["size"] ?? 1, ["quality"] = u["quality"]?.DeepClone(), ["defense"] = u["defense"]?.DeepClone(),
            ["bases"] = u["bases"]?.DeepClone(), ["rules"] = new JsonArray([.. (u["rules"]?.AsArray() ?? []).Select(Rule)]), ["loadout"] = loadout,
        };
    }
}
