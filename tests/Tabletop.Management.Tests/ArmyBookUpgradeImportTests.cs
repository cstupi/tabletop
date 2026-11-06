using System.Globalization;
using System.Text.Json.Nodes;
using Tabletop.Content;
using Tabletop.Management.Content.Armies;

namespace Tabletop.Management.Tests;

/// <summary>Every upgrade option of every army book in the Army Forge dump (`.superpowers/armyforge/<edition>/books`, git-ignored;
/// skipped without it), taken alone on its unit and imported as Army Forge's TTS export would carry it. Each import stores (one
/// property per name on a unit, a slot and a weapon), an upgrade's weapons are weapons of its carriers and never rule names, rated
/// rules add on a one-model unit (p.13: rules with (X) stack), and a mount's base is its carriers' base.</summary>
public class ArmyBookUpgradeImportTests
{
    static readonly IReadOnlyDictionary<string, PropertyLevel> Catalog = new Dictionary<string, PropertyLevel>();

    static string? Up(string relative)
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        return null;
    }

    [SkippableTheory]
    [InlineData("3.5")]
    [InlineData("3.6")]
    public void Every_upgrade_option_of_every_army_book_imports_whole(string edition)
    {
        var books = Up(Path.Combine(".superpowers", "armyforge", edition, "books"));
        Skip.If(books is null, $".superpowers/armyforge/{edition}/books not found above the working directory (the dump is git-ignored)");
        var problems = new List<string>();
        var options = 0;
        foreach (var file in Directory.GetFiles(books!, "*.json").Order())
        {
            var book = JsonNode.Parse(File.ReadAllText(file))!;
            var packages = (book["upgradePackages"]?.AsArray() ?? []).ToDictionary(p => (string)p!["uid"]!, p => p!);
            foreach (var unit in book["units"]?.AsArray() ?? [])
                foreach (var uid in unit!["upgrades"]?.AsArray() ?? [])
                    foreach (var section in packages.GetValueOrDefault((string)uid!)?["sections"]?.AsArray() ?? [])
                        foreach (var option in section!["options"]?.AsArray() ?? [])
                        {
                            options++;
                            var at = $"{book["name"]} / {unit["name"]} / {option!["label"]}";
                            try { problems.AddRange(Check(unit, option["gains"]?.AsArray() ?? []).Select(p => $"{at}: {p}")); }
                            catch (Exception e) { problems.Add($"{at}: {e.GetType().Name}: {e.Message}"); }
                        }
        }
        Assert.True(options > 1000, $"only {options} options read");
        Assert.True(problems.Count == 0, $"{problems.Count} of {options} options:\n" + string.Join("\n", problems.Take(40)));
    }

    /// <summary>The unit with one option's gains, as Army Forge's export carries a selected upgrade: a weapon joins the loadout, an
    /// item joins the loadout with its content and bases, a rule joins the unit's rules.</summary>
    static IEnumerable<string> Check(JsonNode unit, JsonArray gains)
    {
        // Some 3.5 books write numbers as text ("count": "02"); the export parser reads both.
        static int Int(JsonNode? n, int fallback) => n is null ? fallback : int.Parse(n.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture);
        var size = Int(unit["size"], 1);
        var rules = new JsonArray([.. (unit["rules"]?.AsArray() ?? []).Select(r => r!.DeepClone())]);
        var loadout = new JsonArray([.. (unit["weapons"]?.AsArray() ?? []).Select(w => w!.DeepClone())]);
        foreach (var g in gains)
        {
            var copy = g!.DeepClone();
            copy["count"] = Math.Min(Int(g["count"], 1), size);
            if ((string?)g["type"] == "ArmyBookRule") rules.Add(copy); else loadout.Add(copy);
        }
        var entry = new JsonObject
        {
            ["selectionId"] = "a", ["name"] = unit["name"]?.DeepClone(), ["size"] = size, ["quality"] = unit["quality"]?.DeepClone(),
            ["defense"] = unit["defense"]?.DeepClone(), ["bases"] = unit["bases"]?.DeepClone(), ["rules"] = rules, ["loadout"] = loadout,
            ["combined"] = false, ["joinToUnit"] = null,
        };
        var list = new JsonObject { ["id"] = "t", ["name"] = "T", ["gameSystem"] = "gf", ["units"] = new JsonArray(entry) };
        var army = ArmyForgeImport.ToArmy(ArmyForgeClient.Parse(list.ToJsonString()), "x", Catalog);
        var built = army.Units.Single();

        foreach (var (what, names) in new[] { ("unit property", built.Properties.Select(p => p.Name)) }
                     .Concat(built.Models.Select(m => ("slot property", m.Properties.Select(p => p.Name))))
                     .Concat(built.Models.SelectMany(m => m.Weapons).Select(w => ("weapon property", w.Properties.Select(p => p.Name)))))
            foreach (var dup in names.GroupBy(n => n).Where(g => g.Count() > 1))
                yield return $"{what} '{dup.Key}' twice";

        var items = gains.Where(g => (string?)g!["type"] == "ArmyBookItem").ToList();
        var itemRules = items.SelectMany(i => i!["content"]?.AsArray() ?? []).Where(c => (string?)c!["type"] != "ArmyBookWeapon").ToList();
        var ruleNames = rules.Concat(itemRules).Select(r => (string?)r!["name"]).ToHashSet();
        var properties = built.Properties.Concat(built.Models.SelectMany(m => m.Properties)).ToList();
        foreach (var w in items.SelectMany(i => i!["content"]?.AsArray() ?? []).Where(c => (string?)c!["type"] == "ArmyBookWeapon"))
        {
            var name = ((string?)w!["name"])!;
            if (!built.Models.Any(m => m.Weapons.Any(x => x.Name == name))) yield return $"item weapon '{name}' is no model's weapon";
            if (!ruleNames.Contains(name) && properties.Any(p => p.Name == name)) yield return $"item weapon '{name}' became a rule";
        }

        if (size == 1)
            foreach (var g in rules.Concat(itemRules).Where(r => r!["rating"] is not null).GroupBy(r => (string)r!["name"]!))
            {
                var ratings = g.Select(r => int.TryParse(r!["rating"]!.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null).ToList();
                if (ratings.Any(n => n is null)) continue;
                var got = properties.Single(p => p.Name == g.Key).Parameter;
                if (got != ratings.Sum()) yield return $"{g.Key}({got}), expected ({string.Join(" + ", ratings)})";
            }

        foreach (var i in items)
            if (ArmyForgeImport.ParseBase(ArmyForgeClient.Parse($$"""{"id":"t","name":"T","gameSystem":"gf","units":[{"selectionId":"b","name":"U","size":1,"quality":4,"defense":4,"bases":{{i!["bases"]?.ToJsonString() ?? "null"}},"combined":false}]}""").Units[0].Bases) is { } mount
                && !built.Models.Any(m => m.Base == mount))
                yield return $"no model stands on item '{i["name"]}' base";
    }
}
