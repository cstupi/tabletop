using System.Text.Json;

namespace Tabletop.Management.Content.Armies;

/// <summary>Which models of an export entry hold which loadout line. Army Forge's loadout is flat lines with counts; the entry also carries
/// the book's `weapons` and `items` and its `selectedUpgrades` in the order they were taken, which say what each model holds: every model
/// starts with the book's loadout, a "replace" takes its targets from models that hold them all and gives those models its gains (one model
/// per replacement; "all" and an unstated count take every model still holding them), an "upgrade" gives its gains to that many models, an
/// "attachment" to models holding its targets. Where Army Forge does not say which model (any one of the squad), the choice is the model
/// least changed so far, the last of those: a banner, a sergeant and two special weapons go to four different models. `Deal` is used
/// when an entry has no selections, or they do not reproduce its loadout's counts.</summary>
public static class ArmyForgeModels
{
    public const int MaxSelections = 200;
    /// <summary>The most gains or targets one selection may name, and the most entries a model may come to hold, before the replay gives
    /// way to the deal: an export past these is no Army Forge selection, and the bounds keep every scan of the replay small.</summary>
    public const int MaxPerSelection = 16, MaxHeldPerModel = 32;
    const string Weapon = "ArmyBookWeapon", Item = "ArmyBookItem";

    public sealed record Selection(Section? Upgrade, Option? Option);
    public sealed record Section(string? Variant, Affects? Affects, List<string>? Targets);
    public sealed record Affects(string? Type, JsonElement? Value);
    public sealed record Option(List<AfLoadoutItem>? Gains);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    /// <summary>For each loadout line, the indices of the models holding it, a model once per copy it holds (a hero's "2x Hammer" is
    /// its index twice).</summary>
    public static List<List<int>> Carriers(AfUnit u) => FromSelections(u) ?? Deal(u);

    /// <summary>Lines dealt out without selections: a line of `count` gives every model count / size copies, and the rest go to the next
    /// models from a cursor, one for the guns and one for the melee weapons (a model holds one line of each: a sergeant's Pistol 1 and Sword 1
    /// land on one model); an upgrade holding no weapon (a banner, a medic) goes to the next from a cursor of its own, so its carrier keeps
    /// its weapon.</summary>
    public static List<List<int>> Deal(AfUnit u)
    {
        var n = u.Size;
        int ranged = 0, melee = 0, unarmed = 0;
        var result = new List<List<int>>();
        foreach (var item in u.Loadout ?? [])
        {
            var carriers = new List<int>();
            if (item.Type is not (Weapon or Item)) { result.Add(carriers); continue; }
            List<JsonElement?> weapons = item.Type == Weapon ? [item.Range] : [.. (item.Content ?? []).Where(e => e.IsWeapon).Select(e => e.Range)];
            var isRanged = weapons.Any(IsRanged);
            var count = Math.Max(0, item.Count);
            for (var k = 0; k < count / n; k++) carriers.AddRange(Enumerable.Range(0, n));
            var rest = count % n;
            ref var cursor = ref weapons.Count == 0 ? ref unarmed : ref (isRanged ? ref ranged : ref melee);
            carriers.AddRange(Enumerable.Range(cursor, rest).Select(i => i % n));
            cursor += rest;
            result.Add(carriers);
        }
        return result;
    }

    static bool IsRanged(JsonElement? range) => range is { ValueKind: JsonValueKind.Number } r && r.TryGetDouble(out var d) && d > 0;

    /// <summary>Army Forge names a target in the singular or the plural ("Battle Rifles", "CCWS" for "CCW"), with "2x " for two copies.</summary>
    static (string Name, int Copies) Target(string target)
    {
        var x = target.IndexOf("x ", StringComparison.Ordinal);
        return x > 0 && int.TryParse(target[..x], out var copies) && copies is >= 1 and <= ArmyLimits.WeaponsPerModel ? (target[(x + 2)..], copies) : (target, 1);
    }

    static string Singular(string s)
    {
        s = s.Trim().ToLowerInvariant();
        return s.EndsWith('s') ? s[..^1] : s;
    }

    static List<List<int>>? FromSelections(AfUnit u)
    {
        if (u.SelectedUpgrades is not { ValueKind: JsonValueKind.Array } raw || raw.GetArrayLength() > MaxSelections) return null;
        List<Selection> selections;
        List<AfLoadoutItem> book;
        try
        {
            selections = raw.Deserialize<List<Selection>>(Json) ?? [];
            book = [.. Read(u.Weapons), .. Read(u.Items)];
        }
        catch (JsonException) { return null; }

        var n = u.Size;
        var held = Enumerable.Range(0, n).Select(_ => new List<(string Name, string Key, bool? Ranged)>()).ToList();
        var changed = new int[n];
        foreach (var line in book.Where(l => l.Type is Weapon or Item && l.Name is not null))
        {
            if (line.Count < 0 || line.Count > n * ArmyLimits.WeaponsPerModel) return null;
            var entry = (line.Name, Key(line), Kind(line));
            for (var i = 0; i < n; i++) for (var k = 0; k < line.Count / n + (i < line.Count % n ? 1 : 0); k++) held[i].Add(entry);
        }

        foreach (var s in selections)
        {
            var gains = (s.Option?.Gains ?? []).Where(g => g.Type is Weapon or Item && g.Name is not null).ToList();
            var gainKeys = gains.Select(Key).ToList();
            if (gains.Count > MaxPerSelection || gains.Any(g => g.Count > ArmyLimits.WeaponsPerModel)) return null;
            if ((s.Upgrade?.Targets?.Count ?? 0) > MaxPerSelection) return null;
            var targets = (s.Upgrade?.Targets ?? []).Where(t => t is not null).Select(Target).ToList();
            var affects = s.Upgrade?.Affects?.Type;
            var value = s.Upgrade?.Affects?.Value is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var x) ? Math.Clamp(x, 1, n) : 1;
            var chosen = new List<int>();
            var split = new Dictionary<int, List<int>>();     // an instance whose targets came from several models: each gain's model
            int Holds(int i, string target) =>
                held[i].Count(h => h.Name == target) is var exact and > 0 ? exact : held[i].Count(h => Singular(h.Name) == Singular(target));
            bool HoldsAll(int i) => targets.All(t => Holds(i, t.Name) >= t.Copies);
            bool? Take(int i, (string Name, int Copies) t)
            {
                bool? kind = null;
                for (var k = 0; k < t.Copies; k++)
                {
                    var at = held[i].FindIndex(h => h.Name == t.Name);
                    if (at < 0) at = held[i].FindIndex(h => Singular(h.Name) == Singular(t.Name));
                    kind = held[i][at].Ranged;
                    held[i].RemoveAt(at);
                }
                return kind;
            }
            // The model least changed so far, the last of those; `distinct` keeps one selection to one instance per model.
            int Pick(Func<int, bool> eligible, bool distinct) => Enumerable.Range(0, n).Where(i => eligible(i) && !(distinct && chosen.Contains(i)))
                .OrderBy(i => changed[i]).ThenByDescending(i => i).DefaultIfEmpty(-1).First();

            switch (s.Upgrade?.Variant)
            {
                case "replace":
                    if (targets.Count == 0) return null;
                    var want = affects switch { "exactly" => value, "any" or "up to" => 1, _ => n * ArmyLimits.WeaponsPerModel };
                    while (chosen.Count < want)
                    {
                        // Army Forge counts targets over the unit, so when no one model holds them all (a pistol already traded on the
                        // model that holds the sword), each comes from a model that holds it, and each gain goes to the model that gave up a
                        // target of its kind (the new pistol where the pistol was), else with the first.
                        if (Pick(HoldsAll, distinct: false) is var i and >= 0) foreach (var t in targets) Take(i, t);
                        else
                        {
                            var from = targets.Select(t => Pick(m => Holds(m, t.Name) >= t.Copies, distinct: false)).ToList();
                            if (from.Any(m => m < 0)) break;
                            i = from[0];
                            var kinds = targets.Zip(from).Select(p => Take(p.Second, p.First)).ToList();
                            split[chosen.Count] = gains.Select(g => from[Math.Max(0, kinds.IndexOf(Kind(g)))]).ToList();
                        }
                        chosen.Add(i);
                    }
                    if (chosen.Count == 0 || (affects is "exactly" or "any" or "up to" && chosen.Count < want)) return null;
                    break;
                case "attachment":
                    if (targets.Count == 0) return null;
                    var attach = affects switch { "exactly" => value, "all" => n, _ => 1 };
                    // One attachment per target weapon: a model already holding as many of these gains as targets takes no more.
                    bool Free(int i) => HoldsAll(i) && gainKeys.All(g => held[i].Count(h => h.Key == g) < Holds(i, targets[0].Name) / targets[0].Copies);
                    while (chosen.Count < attach)
                    {
                        var a = Pick(Free, distinct: true);
                        if (a < 0) a = Pick(HoldsAll, distinct: true);
                        if (a < 0) break;
                        chosen.Add(a);
                    }
                    if (chosen.Count == 0) return null;
                    break;
                default:
                    var upgrade = affects switch { "all" => n, "exactly" => value, _ => 1 };
                    while (chosen.Count < upgrade && Pick(i => !gainKeys.Any(g => held[i].Any(h => h.Key == g)) || upgrade == n, distinct: true) is var m and >= 0) chosen.Add(m);
                    if (chosen.Count < upgrade) return null;
                    break;
            }
            for (var c = 0; c < chosen.Count; c++)
            {
                var to = split.GetValueOrDefault(c) ?? [.. gains.Select(_ => chosen[c])];
                foreach (var m in to.Distinct()) changed[m]++;
                for (var gi = 0; gi < gains.Count; gi++) for (var k = 0; k < Math.Max(1, gains[gi].Count); k++) held[to[gi]].Add((gains[gi].Name, gainKeys[gi], Kind(gains[gi])));
            }
            if (held.Any(h => h.Count > MaxHeldPerModel)) return null;
        }

        // The selections must give exactly the loadout's lines; each line then takes its copies from the models holding them, in order.
        var lines = u.Loadout ?? [];
        var wanted = lines.Where(l => l.Type is Weapon or Item).GroupBy(Key).ToDictionary(g => g.Key, g => g.Sum(l => l.Count));
        var have = held.SelectMany(h => h).GroupBy(h => h.Key).ToDictionary(g => g.Key, g => g.Count());
        if (wanted.Count != have.Count || wanted.Any(w => have.GetValueOrDefault(w.Key) != w.Value)) return null;
        var result = new List<List<int>>();
        foreach (var l in lines)
        {
            var carriers = new List<int>();
            var key = Key(l);
            if (l.Type is Weapon or Item)
                for (var i = 0; i < n && carriers.Count < l.Count; i++)
                    while (carriers.Count < l.Count && held[i].FindIndex(h => h.Key == key) is var at and >= 0) { held[i].RemoveAt(at); carriers.Add(i); }
            result.Add(carriers);
        }
        return result;
    }

    /// <summary>What tells two lines apart: type and name, and for an upgrade its content (a book's Combat Shield with Bash and a gained
    /// Combat Shield with Shielded only are different things).</summary>
    static string Key(AfLoadoutItem l) =>
        l.Type == Item ? $"{l.Type}|{l.Name}|{string.Join(",", (l.Content ?? []).Select(c => c.Name).Order(StringComparer.Ordinal))}" : $"{l.Type}|{l.Name}";

    /// <summary>A gun (true), a melee weapon (false), or an upgrade arming no one (null); an upgrade arming its carrier is a gun if any
    /// of its weapons is.</summary>
    static bool? Kind(AfLoadoutItem l)
    {
        List<JsonElement?> ranges = l.Type == Weapon ? [l.Range] : [.. (l.Content ?? []).Where(c => c.IsWeapon).Select(c => c.Range)];
        return ranges.Count == 0 ? null : ranges.Any(IsRanged);
    }

    static List<AfLoadoutItem> Read(JsonElement? e) => e is { ValueKind: JsonValueKind.Array } a ? a.Deserialize<List<AfLoadoutItem>>(Json) ?? [] : [];
}
