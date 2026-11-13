using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tabletop.Management.Tests;

/// <summary>One army-book unit as Army Forge's TTS export carries it after a series of selections, built from the book dump the way the
/// real exports in `Fixtures/` show Army Forge writing it: the book's `weapons` and `items` are the first loadout lines; a "replace"
/// selection lowers its targets' lines by the models it affects (a line at 0 goes) and appends its gains, each with that count times the
/// gain's own count ("Battle Rifle 8, Strike 10, Plas-Charge Rifle 1, Plas-Charge Rifle 1"; "Replace all Electro-Carbines and Strikes"
/// after a Flamer gives "Strike 1, Flamer 1, Shotgun 4, Strike 4", the smaller of its targets' counts); "upgrade" and "attachment"
/// append gains only. `selectedUpgrades` lists each selection in order with its section and option. The builder never decides which
/// model holds what: that is the importer's job, and what the invariant tests check.</summary>
public sealed partial class ArmyForgeExportBuilder
{
    /// <summary>A loadout line, with what the builder knows about it: `Per` copies of it go to each model it affects, `Selection` is the
    /// index of the selection that appended it (-1 for the book's own lines), `Variant` that selection's section variant.</summary>
    public sealed record Line(JsonObject Json, int Per, int Selection, string Variant)
    {
        public string Name => (string)Json["name"]!;
        public string Type => (string)Json["type"]!;
        public int Count { get => Int(Json["count"], 0); set => Json["count"] = value; }
        public int Instances => Count / Math.Max(1, Per);
    }

    public JsonNode Unit { get; }
    /// <summary>The unit at its book loadout, shared by the checks of every case of it.</summary>
    public ArmyForgeExportBuilder? Book { get; set; }
    public int Size { get; }
    public List<Line> Lines { get; } = [];
    public JsonArray Rules { get; }
    public JsonArray Selected { get; } = [];
    /// <summary>The applied selections: section variant, models affected, the gains' lines.</summary>
    public List<(string Variant, int Models, List<Line> Gains, List<string> Targets, string Section)> Applied { get; } = [];

    public ArmyForgeExportBuilder(JsonNode unit)
    {
        Unit = unit;
        Size = Int(unit["size"], 1);
        Rules = new JsonArray([.. (unit["rules"]?.AsArray() ?? []).Select(r => r!.DeepClone())]);
        foreach (var w in (unit["weapons"]?.AsArray() ?? []).Concat(unit["items"]?.AsArray() ?? []))
        {
            var json = w!.DeepClone().AsObject();
            var count = Int(json["count"], 1);
            json["count"] = count;
            json["originalCount"] = count;
            Lines.Add(new Line(json, count >= Size && count % Size == 0 ? count / Size : 1, -1, "book"));
        }
    }

    public static int Int(JsonNode? n, int fallback) =>
        n is null ? fallback : int.TryParse(n.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    [GeneratedRegex(@"^(\d+)x (.+)$")] private static partial Regex Multiple();

    /// <summary>A target as a name and the copies one affected model gives up: "2x Heavy Daemon Hammer" is two.</summary>
    public static (string Name, int Copies) Target(string target) =>
        Multiple().Match(target) is { Success: true } m ? (m.Groups[2].Value, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) : (target, 1);

    /// <summary>Army Forge names targets in the singular or plural ("Battle Rifles", "CCWS" for "CCW").</summary>
    public static bool Names(string line, string target)
    {
        static string Key(string s) { s = s.Trim().ToLowerInvariant(); return s.EndsWith('s') ? s[..^1] : s; }
        return string.Equals(line, target, StringComparison.OrdinalIgnoreCase) || Key(line) == Key(target);
    }

    List<Line> Matching(string target) =>
        Lines.Where(l => string.Equals(l.Name, target, StringComparison.Ordinal)).ToList() is { Count: > 0 } exact ? exact : Lines.Where(l => Names(l.Name, target)).ToList();

    /// <summary>Applies one selection of `option` from `section`, or answers false when its targets are not in the loadout.</summary>
    public bool Apply(JsonNode section, JsonNode option)
    {
        var variant = (string?)section["variant"] ?? "upgrade";
        var affects = (string?)section["affects"]?["type"];
        var value = Int(section["affects"]?["value"], 1);
        var targets = (section["targets"]?.AsArray() ?? []).Select(t => Target((string)t!)).ToList();
        int Available((string Name, int Copies) t) => Matching(t.Name).Sum(l => l.Count) / t.Copies;
        if (variant is "replace" or "attachment" && (targets.Count == 0 || targets.Any(t => Available(t) == 0))) return false;
        // One attachment per target weapon.
        var sectionId = (string?)section["uid"] ?? "";
        var attached = variant == "attachment" ? Applied.Where(a => a.Section == sectionId).Sum(a => a.Models) : 0;
        if (variant == "attachment" && targets.Any(t => Available(t) <= attached)) return false;
        int models = variant switch
        {
            "replace" => affects switch { "exactly" => value, "any" or "up to" => 1, _ => targets.Min(Available) },
            "attachment" => affects switch { "exactly" => value, "all" => targets.Min(Available), _ => 1 },
            _ => affects switch { "all" => Size, "exactly" => Math.Min(value, Size), _ => 1 },
        };
        if (variant == "replace" && targets.Any(t => Available(t) < models)) return false;
        if (variant == "replace")
            foreach (var t in targets)
            {
                var take = models * t.Copies;
                foreach (var l in Matching(t.Name))
                {
                    var off = Math.Min(take, l.Count);
                    l.Count -= off;
                    take -= off;
                }
                Lines.RemoveAll(l => l.Count == 0);
            }
        var gains = new List<Line>();
        foreach (var g in option["gains"]?.AsArray() ?? [])
        {
            var per = Math.Max(1, Int(g!["count"], 1));
            if ((string?)g["type"] == "ArmyBookRule") { Rules.Add(g.DeepClone()); continue; }
            var json = g.DeepClone().AsObject();
            json["count"] = models * per;
            json.Remove("originalCount");
            json["dependencies"] = new JsonArray();
            var line = new Line(json, per, Applied.Count, variant);
            Lines.Add(line);
            gains.Add(line);
        }
        var upgrade = section.DeepClone().AsObject();
        upgrade.Remove("options");
        Selected.Add(new JsonObject { ["instanceId"] = $"i{Selected.Count}", ["upgrade"] = upgrade, ["option"] = option.DeepClone() });
        Applied.Add((variant, models, gains, targets.Select(t => t.Name).ToList(), sectionId));
        return true;
    }

    /// <summary>The export entry. `withSelections` false leaves `selectedUpgrades` out, as an export that lacks it would.</summary>
    public JsonObject Entry(string selectionId, string? book, bool withSelections = true, bool combined = false, string? joinTo = null, IEnumerable<Line>? order = null) => new()
    {
        ["selectionId"] = selectionId, ["name"] = Unit["name"]?.DeepClone(), ["size"] = Size, ["quality"] = Unit["quality"]?.DeepClone(),
        ["defense"] = Unit["defense"]?.DeepClone(), ["bases"] = Unit["bases"]?.DeepClone(), ["rules"] = Rules.DeepClone(),
        ["loadout"] = new JsonArray([.. (order ?? Lines).Select(l => l.Json.DeepClone())]),
        ["weapons"] = Unit["weapons"]?.DeepClone(), ["items"] = Unit["items"]?.DeepClone(),
        ["selectedUpgrades"] = withSelections ? Selected.DeepClone() : null,
        ["combined"] = combined, ["joinToUnit"] = joinTo, ["armyId"] = book,
    };

    public static JsonObject List(params JsonObject[] entries) => new() { ["id"] = "t", ["name"] = "T", ["gameSystem"] = "gf", ["units"] = new JsonArray(entries) };
}
