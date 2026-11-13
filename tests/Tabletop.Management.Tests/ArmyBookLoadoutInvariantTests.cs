using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tabletop.Content;
using Tabletop.Management.Content.Armies;
using Xunit.Abstractions;
using Line = Tabletop.Management.Tests.ArmyForgeExportBuilder.Line;

namespace Tabletop.Management.Tests;

/// <summary>Every unit of every army book in the Army Forge dump (`.superpowers/armyforge/&lt;edition&gt;/books`, git-ignored; skipped without
/// it), at its book loadout and after each upgrade option as Army Forge would export it (`ArmyForgeExportBuilder`): once, as often as a
/// "replace any" / "up to N" section allows, and before and after the first option of another section. Each import is held to the
/// invariants below, and the counts of each are printed. The export gives flat lines with counts and the selections in order; which
/// model holds which line is the importer's reading, so the invariants are what every reading Army Forge allows has in common.</summary>
public class ArmyBookLoadoutInvariantTests(ITestOutputHelper output)
{
    static readonly IReadOnlyDictionary<string, PropertyLevel> Catalog = new Dictionary<string, PropertyLevel>();

    static string? Up(string relative)
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        return null;
    }

    sealed class Tally
    {
        public readonly SortedDictionary<string, (int Pass, int Fail, List<string> Examples)> Rows = new(StringComparer.Ordinal);
        public void Check(string invariant, bool ok, Func<string> example)
        {
            var row = Rows.TryGetValue(invariant, out var r) ? r : (Pass: 0, Fail: 0, Examples: new List<string>());
            if (ok) row.Pass++; else { row.Fail++; if (row.Examples.Count < 6) row.Examples.Add(example()); }
            Rows[invariant] = row;
        }
    }

    [SkippableTheory]
    [InlineData("3.6")]
    [InlineData("3.5")]
    public void Every_unit_of_every_army_book_ends_with_its_loadout_on_the_right_models(string edition)
    {
        var books = Up(Path.Combine(".superpowers", "armyforge", edition, "books"));
        Skip.If(books is null, $".superpowers/armyforge/{edition}/books not found above the working directory (the dump is git-ignored)");
        var tally = new Tally();
        int bookCount = 0, units = 0, options = 0, cases = 0, inapplicable = 0;
        foreach (var file in Directory.GetFiles(books!, "*.json").Order())
        {
            var book = JsonNode.Parse(File.ReadAllText(file))!;
            var uid = (string?)book["uid"];
            bookCount++;
            var packages = (book["upgradePackages"]?.AsArray() ?? []).ToDictionary(p => (string)p!["uid"]!, p => p!);
            var hero = (book["units"]?.AsArray() ?? []).FirstOrDefault(u => ArmyForgeExportBuilder.Int(u!["size"], 1) == 1 && (u["rules"]?.AsArray() ?? []).Any(r => (string?)r!["name"] == "Hero"));
            foreach (var unit in book["units"]?.AsArray() ?? [])
            {
                units++;
                var sections = (unit!["upgrades"]?.AsArray() ?? []).SelectMany(p => packages.GetValueOrDefault((string)p!)?["sections"]?.AsArray() ?? []).Select(s => s!).ToList();
                var at = $"{book["name"]} / {unit["name"]}";
                var book0 = new ArmyForgeExportBuilder(unit);
                void Run(string label, IReadOnlyList<(JsonNode Section, JsonNode Option)> picks, bool shuffle)
                {
                    var b = Build(unit, sections, picks);
                    if (b is not null) b.Book = book0;
                    if (b is null) { inapplicable++; return; }
                    cases++;
                    try { Check(tally, b, uid, edition, $"{at} / {label}", shuffle, bareToo: picks.Count > 1 && picks.All(p => p == picks[0])); }
                    catch (Exception e) { tally.Check("import", false, () => $"{at} / {label}: {e.GetType().Name}: {e.Message}"); }
                }
                Run("book loadout", [], shuffle: false);
                if (ArmyForgeExportBuilder.Int(unit["size"], 1) > 1) Together(tally, unit, sections, hero, uid, edition, at);
                for (var si = 0; si < sections.Count; si++)
                {
                    var section = sections[si];
                    var other = sections.Count > 1 ? sections[(si + 1) % sections.Count] : null;
                    var otherOption = other?["options"]?.AsArray().FirstOrDefault();
                    var affects = (string?)section["affects"]?["type"];
                    foreach (var option in section["options"]?.AsArray() ?? [])
                    {
                        options++;
                        var label = $"{section["label"]} / {option!["label"]}";
                        Run(label, [(section, option)], shuffle: false);
                        if (affects is "any" or "up to")
                        {
                            var times = affects == "up to" ? ArmyForgeExportBuilder.Int(section["affects"]?["value"], 1) : ArmyForgeExportBuilder.Int(unit["size"], 1);
                            Run($"{label} x{times}", Enumerable.Repeat((section, option), times).ToList(), shuffle: true);
                        }
                        if (otherOption is not null)
                        {
                            Run($"{label} + {other!["label"]} / {otherOption["label"]}", [(section, option), (other, otherOption)], shuffle: true);
                            // The other order matters only where both take something away (a pistol traded before or after the sergeant).
                            if ((string?)section["variant"] != "upgrade" && (string?)other["variant"] != "upgrade")
                                Run($"{other["label"]} / {otherOption["label"]} + {label}", [(other, otherOption), (section, option)], shuffle: false);
                        }
                    }
                }
            }
        }
        output.WriteLine($"{edition}: {bookCount} books, {units} units, {options} options, {cases} cases imported, {inapplicable} picks Army Forge would not allow");
        foreach (var (name, (pass, fail, examples)) in tally.Rows)
        {
            output.WriteLine($"  {name}: {pass} pass, {fail} fail");
            foreach (var e in examples) output.WriteLine($"      {e}");
        }
        Assert.True(options > 1000, $"only {options} options read");
        var failed = tally.Rows.Where(r => r.Value.Fail > 0 && !r.Key.StartsWith("info", StringComparison.Ordinal)).ToList();
        Assert.True(failed.Count == 0, string.Join("\n", failed.Select(r => $"{r.Key}: {r.Value.Fail} fail\n    " + string.Join("\n    ", r.Value.Examples))));
    }

    /// <summary>The picks applied in order; a pick whose targets another option's gains provide ("Replace Sgt. Pistol") takes that
    /// option first. Null when Army Forge would not allow a pick.</summary>
    static ArmyForgeExportBuilder? Build(JsonNode unit, List<JsonNode> sections, IReadOnlyList<(JsonNode Section, JsonNode Option)> picks)
    {
        var b = new ArmyForgeExportBuilder(unit);
        var applied = 0;
        foreach (var (section, option) in picks)
        {
            if (b.Apply(section, option)) { applied++; continue; }
            var targets = (section["targets"]?.AsArray() ?? []).Select(t => ArmyForgeExportBuilder.Target((string)t!).Name).ToList();
            var source = sections.Where(s => s != section).SelectMany(s => (s["options"]?.AsArray() ?? []).Select(o => (Section: s, Option: o!)))
                .FirstOrDefault(p => (p.Option["gains"]?.AsArray() ?? []).Any(g => targets.Any(t => ArmyForgeExportBuilder.Names((string)g!["name"]!, t))));
            if (source.Option is null || !b.Apply(source.Section, source.Option) || !b.Apply(section, option)) { if (applied == 0) return null; break; }
            applied++;
        }
        return b;
    }

    static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    /// <summary>The list read as ArmyForgeClient.Parse reads it (the same options), straight from the nodes.</summary>
    static Army Import(string edition, params JsonObject[] entries) => ArmyForgeImport.ToArmy(System.Text.Json.JsonSerializer.Deserialize<AfList>(ArmyForgeExportBuilder.List(entries), Json)!, "x", Catalog, edition);

    static readonly HashSet<string> Added = [ArmyBookAdditions.TakedownShot, ArmyBookAdditions.TakedownStrike];
    static bool Ranged(Weapon w) => w.Stats.ContainsKey("Range");
    static List<string> Arms(ModelSlot m) => m.Weapons.Where(w => !Added.Contains(w.Name)).Select(w => w.Name).Order(StringComparer.Ordinal).ToList();
    static string Loadout(ModelSlot m) => string.Join(", ", Arms(m)) + " | " + string.Join(", ", m.Properties.Select(p => p.Parameter is { } n ? $"{p.Name}({n})" : p.Name).Order(StringComparer.Ordinal));
    static string Models(Unit u) => string.Join("; ", u.Models.Select(m => $"[{m.Name}: {Loadout(m)}]"));

    /// <summary>A line's weapons with their ranges (an item's content weapons, or the line itself).</summary>
    static IEnumerable<(string Name, bool Ranged)> LineWeapons(Line l) =>
        l.Type == "ArmyBookWeapon" ? [(l.Name, Ranges(l.Json))]
        : (l.Json["content"]?.AsArray() ?? []).Where(c => (string?)c!["type"] == "ArmyBookWeapon").Select(c => ((string)c!["name"]!, Ranges(c)));
    static bool Ranges(JsonNode n) => ArmyForgeExportBuilder.Int(n["range"], 0) > 0;
    static IEnumerable<JsonNode> LineRules(Line l) => l.Type == "ArmyBookItem" ? (l.Json["content"]?.AsArray() ?? []).Where(c => (string?)c!["type"] != "ArmyBookWeapon").Select(c => c!) : [];

    static Tabletop.Geometry.Base? BaseOf(JsonNode? bases) =>
        bases is null ? null : ArmyForgeImport.ParseBase(new AfBases((string?)bases["round"]?.ToString(), (string?)bases["square"]?.ToString()));

    static void Check(Tally t, ArmyForgeExportBuilder b, string? book, string edition, string at, bool shuffle, bool bareToo)
    {
        var army = Import(edition, b.Entry("a", book));
        var unit = army.Units[0];
        var n = b.Size;
        var models = unit.Models;
        string Where(string what) => $"{at}: {what}\n        lines: {string.Join(", ", b.Lines.Select(l => $"{l.Name} {l.Count}"))}\n        models: {Models(unit)}";
        t.Check("g. one slot per model", models.Count == n, () => Where($"{models.Count} slots for {n} models"));

        // a. "for every weapon line, exactly `count` models carry that weapon (or every model when count >= size); the totals of each
        // weapon name over the unit equal the export's totals."
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var l in b.Lines) foreach (var (w0, _) in LineWeapons(l)) expected[w0] = expected.GetValueOrDefault(w0) + l.Count;
        foreach (var (weapon, total) in expected)
        {
            if (Added.Contains(weapon)) continue;
            var got = models.Sum(m => m.Weapons.Count(w => w.Name == weapon));
            t.Check("a. weapon totals equal the export's", got == total, () => Where($"{weapon}: {got} carried, the export has {total}"));
            var lines = b.Lines.Where(l => LineWeapons(l).Any(w => w.Name == weapon)).ToList();
            var carriers = models.Count(m => m.Weapons.Any(w => w.Name == weapon));
            if (lines.Any(l => l.Instances >= n))
                t.Check("a. a line of count >= size arms every model", carriers == n, () => Where($"{weapon} on {carriers} of {n}"));
            else if (lines.All(l => l.Per == 1 && l.Variant != "attachment"))
                t.Check("a. a line of count < size arms exactly count models", carriers == Math.Min(n, total), () => Where($"{weapon} on {carriers} models, the export has {total}"));
        }

        // b. "No model carries two different ranged weapons that came from separate partial-count lines unless the export gives the unit
        // more ranged lines than models; same for melee." Partial lines are the book's and a replacement's (an upgrade or attachment
        // adds a weapon to a model that keeps its own). It holds where the book gives each model one weapon of the kind and no
        // replacement gives two (a Missile Rack and a Burst Cyclic Rifle for one rifle): one model of one is trivially exempt.
        foreach (var ranged in new[] { true, false })
        {
            var kind = ranged ? "ranged" : "melee";
            if (n == 1 || (b.Book ?? new ArmyForgeExportBuilder(b.Unit)).Lines.Sum(l => LineWeapons(l).Count(w => w.Ranged == ranged) * l.Count) > n
                || b.Applied.Any(s => s.Gains.Sum(g => LineWeapons(g).Count(w => w.Ranged == ranged) * g.Per) > 1)) continue;
            var partial = b.Lines.Where(l => l.Variant is "book" or "replace" && l.Instances < n && LineWeapons(l).Any(w => w.Ranged == ranged)).ToList();
            var names = partial.SelectMany(l => LineWeapons(l).Where(w => w.Ranged == ranged).Select(w => w.Name)).ToHashSet();
            var full = b.Lines.Where(l => l.Instances >= n).SelectMany(l => LineWeapons(l).Select(w => w.Name)).ToHashSet();
            names.ExceptWith(full);
            if (partial.Sum(l => l.Instances) > n) { t.Check($"info b. more partial {kind} lines than models", false, () => Where("exempt")); continue; }
            foreach (var m in models)
            {
                var held = m.Weapons.Where(w => Ranged(w) == ranged && names.Contains(w.Name)).Select(w => w.Name).Distinct().ToList();
                t.Check($"b. one partial {kind} line per model", held.Count <= 1, () => Where($"{m.Name} holds {string.Join(" and ", held)}"));
            }
        }

        // c. "Every model of a unit whose entry lists any melee weapon for all its models keeps a melee weapon; no model is left with no
        // weapon at all when the export armed every model." A replacement that gives no weapon of a kind (a rifle for a shield) is the
        // export not arming a model, and is left out.
        bool Gives(Line l, bool? ranged) => LineWeapons(l).Any(w => ranged is null || w.Ranged == ranged);
        bool Disarms(bool? ranged) => b.Applied.Any(s => s.Variant == "replace" && !s.Gains.Any(g => Gives(g, ranged)));
        var book0 = b.Book ?? new ArmyForgeExportBuilder(b.Unit);
        if (book0.Lines.Any(l => l.Instances >= n && Gives(l, false)) && !Disarms(false))
            foreach (var m in models) t.Check("c. every model keeps a melee weapon", m.Weapons.Any(w => !Ranged(w)), () => Where($"{m.Name} has none"));
        if (book0.Lines.Where(l => Gives(l, null)).Sum(l => l.Instances) >= n && !Disarms(null) && b.Lines.Where(l => Gives(l, null) && l.Variant is "book" or "replace").Sum(l => l.Instances) >= n)
            foreach (var m in models) t.Check("c. no model is left unarmed", Arms(m).Count > 0, () => Where($"{m.Name} has no weapon"));

        // d. "An upgrade's rules and its weapons land on the same models; an upgrade with no weapon leaves its carrier's weapons
        // untouched; a mount's base goes to its carrier only." One selection's gains (a sergeant's pistol and sword) are one model's.
        var everywhere = b.Rules.Select(r => (string)r!["name"]!).ToHashSet();
        bool Unique(string name, Line owner) => b.Lines.All(l => l == owner || (l.Name != name && LineWeapons(l).All(w => w.Name != name) && LineRules(l).All(r => (string?)r["name"] != name)));
        HashSet<int> Holding(string name) => models.Select((m, i) => (m, i)).Where(x => x.m.Weapons.Any(w => w.Name == name) || x.m.Properties.Any(p => p.Name == name)).Select(x => x.i).ToHashSet();
        foreach (var l in b.Lines.Where(l => l.Type == "ArmyBookItem" && l.Instances < n))
        {
            var marks = LineRules(l).Select(r => (string)r["name"]!).Where(r => !everywhere.Contains(r) && Unique(r, l)).Concat(LineWeapons(l).Select(w => w.Name).Where(w => Unique(w, l))).Distinct().ToList();
            var sets = marks.Select(Holding).ToList();
            if (sets.Count > 1) t.Check("d. an upgrade's rules and weapons share models", sets.All(s => s.SetEquals(sets[0])), () => Where($"{l.Name}: {string.Join(" / ", marks.Zip(sets, (k, s) => $"{k} on {string.Join(",", s)}"))}"));
            if (!LineWeapons(l).Any() && sets.Count > 0 && b.Lines.Where(x => Gives(x, null) && x.Variant is "book" or "replace").Sum(x => x.Instances) >= n && !Disarms(null))
                t.Check("d. a weaponless upgrade's carriers stay armed", sets[0].All(i => Arms(models[i]).Count > 0), () => Where($"{l.Name} carrier unarmed"));
            if (BaseOf(l.Json["bases"]) is { } mount && mount != BaseOf(b.Unit["bases"]))
            {
                var on = models.Count(m => m.Base == mount);
                t.Check("d. a mount's base is its carriers' only", on == b.Lines.Where(x => x.Name == l.Name).Sum(x => x.Instances), () => Where($"{on} models on {l.Name}'s base"));
            }
        }
        for (var si = 0; si < b.Applied.Count; si++)
        {
            var (variant, affected, gains, targets, _) = b.Applied[si];
            // An earlier replacement that took some of these targets but not all (a pistol and shield traded on one model, then the
            // pistol and sword for a sergeant) leaves Army Forge's counts able to force the gains onto two models.
            if (b.Applied.Take(si).Any(e => e.Variant == "replace" && e.Targets.Intersect(targets).Any() && !e.Targets.ToHashSet().SetEquals(targets))) continue;
            // A gain a later selection replaced in part (Flamer for one of the Carbines) is no longer one set of models.
            if (gains.Any(g => g.Count != affected * g.Per || !b.Lines.Contains(g))) continue;
            var marks = gains.Where(g => g.Per == 1 && g.Instances < n).SelectMany(g => LineWeapons(g).Select(w => w.Name).Concat(LineRules(g).Select(r => (string)r["name"]!).Where(r => !everywhere.Contains(r))).Where(x => Unique(x, g))).Distinct().ToList();
            if (marks.Count < 2) continue;
            var sets = marks.Select(Holding).ToList();
            t.Check("d. one selection's gains share models", sets.All(s => s.SetEquals(sets[0])), () => Where($"{string.Join(" / ", marks.Zip(sets, (k, s) => $"{k} on {string.Join(",", s)}"))}"));
        }

        // e. "Slot names: plain `Unit N`, or `Unit (Upgrade) N` exactly for carriers of an upgrade the rest lack; never longer than 64."
        var name = (string)b.Unit["name"]!;
        var pattern = new Regex($@"^{Regex.Escape(name)}(?: \((.+)\))? (\d+)$");
        foreach (var m in models)
        {
            t.Check("e. slot names are at most 64", m.Name.Length <= 64, () => Where(m.Name));
            t.Check("e. a slot is named Unit N or Unit (Upgrade) N", pattern.IsMatch(m.Name), () => Where(m.Name));
        }
        foreach (var item in b.Lines.Where(l => l.Type == "ArmyBookItem").GroupBy(l => l.Name))
        {
            var count = Math.Min(n, item.Sum(l => l.Instances));
            if (count >= n || $"{name} ({item.Key}) {n}".Length > 64) continue;
            var named = models.Count(m => pattern.Match(m.Name) is { Success: true } x && x.Groups[1].Success && x.Groups[1].Value.Split(", ").Contains(item.Key));
            t.Check("e. an upgrade's carriers are named for it", named == count, () => Where($"{named} slots named for {item.Key}, {count} carry it"));
        }

        // f. "A rule every model has is on the unit or on every slot, never on some; rated rules stack as `Stacked` says."
        var rated = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in b.Rules) if (ArmyForgeExportBuilder.Int(r!["rating"], -1) is var v and >= 0) rated[(string)r["name"]!] = rated.GetValueOrDefault((string)r["name"]!) + v;
        foreach (var l in b.Lines.Where(l => l.Instances >= n)) foreach (var r in LineRules(l)) if (ArmyForgeExportBuilder.Int(r["rating"], -1) is var v and >= 0) rated[(string)r["name"]!] = rated.GetValueOrDefault((string)r["name"]!) + v * l.Per;
        foreach (var rule in b.Rules.Select(r => (string)r!["name"]!).Distinct())
        {
            var some = models.Count(m => m.Properties.Any(p => p.Name == rule));
            var onUnit = unit.Properties.Any(p => p.Name == rule);
            var partialItem = b.Lines.Any(l => l.Instances < n && LineRules(l).Any(r => (string?)r["name"] == rule));
            t.Check("f. a rule of every model is the unit's", onUnit && (some == 0 || partialItem), () => Where($"{rule}: unit {onUnit}, {some} slots"));
            if (rated.TryGetValue(rule, out var sum) && onUnit)
                t.Check("f. rated rules of every model stack on the unit", unit.Properties.First(p => p.Name == rule).Parameter == sum, () => Where($"{rule}({unit.Properties.First(p => p.Name == rule).Parameter}), expected ({sum})"));
        }

        Reserves(t, army, b, at);

        // Order independence: the lines in another order are the same models (the selections, in their order, say which lines are one model's).
        if (shuffle)
        {
            var reordered = b.Lines.OrderBy(l => (l.Name.GetHashCode(StringComparison.Ordinal) * 31 + l.Count) & 0x7fffffff).ToList();
            var again = Import(edition, b.Entry("a", book, order: reordered)).Units[0];
            var x = models.Select(Loadout).Order(StringComparer.Ordinal).ToList();
            var y = again.Models.Select(Loadout).Order(StringComparer.Ordinal).ToList();
            t.Check("order: the lines in another order give the same models", x.SequenceEqual(y), () => Where($"reordered: {Models(again)}"));

            // Without `selectedUpgrades` the importer deals lines out; the totals still hold.
            if (!bareToo) return;
            var bare = Import(edition, b.Entry("a", book, withSelections: false)).Units[0];
            foreach (var (w, total) in expected.Where(e => !Added.Contains(e.Key)))
                t.Check("a. weapon totals equal the export's (no selectedUpgrades)", bare.Models.Sum(m => m.Weapons.Count(x => x.Name == w)) == total, () => Where($"{w} without selections: {Models(bare)}"));
        }
    }

    /// <summary>g. "Combined units (two entries merged) and a joined Hero: sizes add, the Hero's loadout never reaches a host slot and
    /// vice versa." h. "A unit where all models hold Reinforcement gets exactly one copy without the rule and with the same weapons per
    /// model; Spawn and Split reserves resolve their X."</summary>
    static void Together(Tally t, JsonNode unit, List<JsonNode> sections, JsonNode? hero, string? book, string edition, string at)
    {
        var first = sections.SelectMany(s => (s["options"]?.AsArray() ?? []).Select(o => (s, o!))).Select(p => Build(unit, sections, [p])).FirstOrDefault(x => x is not null);
        var a = first ?? new ArmyForgeExportBuilder(unit);
        var plain = new ArmyForgeExportBuilder(unit);
        static List<string> Shapes(IEnumerable<ModelSlot> ms) => ms.Select(m => string.Join(", ", Arms(m))).Order(StringComparer.Ordinal).ToList();
        try
        {
            var alone = Shapes(Import(edition, a.Entry("a", book)).Units[0].Models.Concat(Import(edition, plain.Entry("b", book)).Units[0].Models));
            var both = Import(edition, a.Entry("a", book, combined: true), plain.Entry("b", book, combined: true, joinTo: "a")).Units[0];
            t.Check("g. combined: sizes add", both.Models.Count == a.Size * 2, () => $"{at}: {both.Models.Count}");
            t.Check("g. combined: each entry's models as imported alone", Shapes(both.Models).SequenceEqual(alone), () => $"{at}: {Models(both)}\n        alone: {string.Join("; ", alone)}");
            if (hero is not null)
            {
                var h = new ArmyForgeExportBuilder(hero);
                var heroAlone = Arms(Import(edition, h.Entry("h", book)).Units[0].Models[0]);
                var hostAlone = Shapes(Import(edition, a.Entry("a", book)).Units[0].Models);
                var joined = Import(edition, a.Entry("a", book), h.Entry("h", book, joinTo: "a")).Units[0];
                t.Check("g. joined: sizes add", joined.Models.Count == a.Size + 1, () => $"{at}: {joined.Models.Count}");
                t.Check("g. joined: the hero's slot is the hero's loadout", Arms(joined.Models[^1]).SequenceEqual(heroAlone), () => $"{at}: {Models(joined)}");
                t.Check("g. joined: the host's slots are the host's loadout", Shapes(joined.Models.Take(a.Size)).SequenceEqual(hostAlone), () => $"{at}: {Models(joined)}");
            }
        }
        catch (Exception e) { t.Check("import", false, () => $"{at} (together): {e.GetType().Name}: {e.Message}"); }
    }

    static void Reserves(Tally t, Army army, ArmyForgeExportBuilder b, string at)
    {
        var unit = army.Units[0];
        var rules = b.Rules.Select(r => r!).ToList();
        var everyModel = rules.Concat(b.Lines.Where(l => l.Instances >= b.Size).SelectMany(LineRules)).ToList();
        rules = rules.Concat(b.Lines.SelectMany(LineRules)).ToList();
        if (everyModel.Any(r => (string?)r["name"] == ArmyBookReserves.Reinforcement))
        {
            var copies = army.Units.Where(u => u.Reserve?.Property == ArmyBookReserves.Reinforcement && u.Reserve.Of == unit.Id).ToList();
            t.Check("h. reinforcement: one copy", copies.Count == 1, () => $"{at}: {copies.Count} copies");
            if (copies.Count == 1)
            {
                var c = copies[0];
                t.Check("h. reinforcement: the copy lacks the rule", !c.Properties.Concat(c.Models.SelectMany(m => m.Properties)).Any(p => p.Name == ArmyBookReserves.Reinforcement), () => at);
                t.Check("h. reinforcement: the copy's models have the same weapons", c.Models.Select(Arms).Zip(unit.Models.Select(Arms)).All(p => p.First.SequenceEqual(p.Second)) && c.Models.Count == unit.Models.Count, () => $"{at}: {Models(c)}");
            }
        }
        foreach (var r in rules.Where(r => (string?)r["name"] is ArmyBookReserves.Spawn or ArmyBookReserves.Split))
        {
            var x = Regex.Match(r["rating"]?.ToString() ?? "", @"^(.+?)\s*\[(\d+)\]$");
            if (!x.Success) { t.Check("h. spawn/split: X reads as Name [size]", false, () => $"{at}: {r["name"]}({r["rating"]})"); continue; }
            var reserve = army.Units.FirstOrDefault(u => u.Reserve?.Property == (string)r["name"]! && u.Name == x.Groups[1].Value);
            t.Check("h. spawn/split: X resolves to a reserve unit of its size", reserve?.Models.Count == int.Parse(x.Groups[2].Value), () => $"{at}: {r["name"]}({r["rating"]}) gave {reserve?.Models.Count.ToString() ?? "nothing"}");
        }
    }
}
