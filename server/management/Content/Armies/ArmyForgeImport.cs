using System.Globalization;
using System.Text.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Armies;

public enum PropertyLevel { Unit, Model }

public sealed class UnsupportedGameSystemException(string gameSystem) : Exception($"Only Grimdark Future and Age of Fantasy lists import; this list is '{gameSystem}'.")
{
    public string GameSystem { get; } = gameSystem;
}

/// <summary>Army Forge's list → a self-contained Army. Expansion to one slot per model happens here and nowhere else.</summary>
public static class ArmyForgeImport
{
    // Grimdark Future and Age of Fantasy are the same mechanics on different content, so both map to the one rules implementation.
    public static readonly IReadOnlyDictionary<string, string> GameRules = new Dictionary<string, string> { ["gf"] = "grimdark-future", ["aof"] = "grimdark-future" };
    /// <summary>What an entry with no published base gets: a vehicle hull, 221 x 119 x 113 mm. Army Forge publishes bases.round
    /// reliably for infantry, so a missing base means a vehicle or monster in practice, and a 32 mm circle would be wrong for
    /// exactly those models — a tank measured from a 1.26" circle at its hull centre. The player corrects it per slot, and
    /// the height comes with the base because HeightFromBase would scale 221 mm into a 13" tall tank.</summary>
    public static readonly ModelBody DefaultBody = new(Base.Rect(221 / 25.4, 119 / 25.4), 113 / 25.4);
    public const double HeightScale = 1.5;

    /// <summary>A starting height for a base that *was* published, scaled off its larger dimension because nothing publishes a height:
    /// 1.5" for a 25.4 mm circle, 3.54" for a 60x35 mm oval. The player corrects it per slot.</summary>
    public static double HeightFromBase(Base b) => HeightScale * Math.Max(b.A, b.B);

    public static Army ToArmy(AfList list, string source, IReadOnlyDictionary<string, PropertyLevel> catalog, string edition = ArmyEditions.Served)
    {
        if (!GameRules.TryGetValue(list.GameSystem ?? "", out var gameRules)) throw new UnsupportedGameSystemException(list.GameSystem ?? "");
        Validate(list, source);
        var merged = Merge(list.Units);
        if (merged.Count > ArmyLimits.MaxUnits) throw new InvalidArmyException($"The list has {merged.Count} units; at most {ArmyLimits.MaxUnits} import.");
        var units = merged.Select((m, i) => Unit(m, i + 1, catalog)).ToList();
        units = ArmyBookReserves.Add(units, merged.Select(m => (IReadOnlyList<AfUnit>)[m.Host, .. m.Combined, .. m.Joined]).ToList(), (entry, ordinal) => Unit(new Merged(entry, [], []), ordinal, catalog), edition);
        return new Army(Guid.CreateVersion7(), list.Name, gameRules, source, units) { Edition = edition };
    }

    /// <summary>Every entry against ArmyLimits, before anything is built. A name is checked before a message quotes it.</summary>
    private static void Validate(AfList list, string source)
    {
        static void Check(string? problem) { if (problem is not null) throw new InvalidArmyException(problem); }
        Check(Limits.Text(source, "source", Limits.Source));
        Check(Limits.Text(list.Name, "Army name", Limits.Name));
        if (list.Units is not { Count: > 0 }) throw new InvalidArmyException("The list has no units.");
        var ids = new HashSet<string>();
        foreach (var u in list.Units)
        {
            if (string.IsNullOrEmpty(u.SelectionId)) throw new InvalidArmyException("Every entry needs a selectionId.");
            Check(Limits.Text(u.Name, "Unit name", Limits.Name));
            var unit = $"Unit '{u.Name}': ";
            if (!ids.Add(u.SelectionId)) throw new InvalidArmyException($"The list names entry '{Quoted(u.SelectionId)}' twice.");
            if (u.Size is < 1 or > ArmyLimits.MaxEntrySize) throw new InvalidArmyException($"{unit}size {u.Size} is outside 1–{ArmyLimits.MaxEntrySize}.");
            TargetNumber(u.Quality, unit, "quality");
            TargetNumber(u.Defense, unit, "defense");
            void Rules(List<AfRule>? rules)
            {
                foreach (var r in rules ?? [])
                {
                    Check(Prefixed(unit, Limits.Text(r.Name, "rule name", Limits.Name)));
                    if (Rating(r.Rating) is { } n and (< 0 or > ArmyLimits.MaxRating)) throw new InvalidArmyException($"{unit}rule '{r.Name}' rating {n} is outside 0–{ArmyLimits.MaxRating}.");
                }
            }
            string Weapon(string name, int? attacksOrNull, JsonElement? range, List<AfRule>? rules)
            {
                Check(Prefixed(unit, Limits.Text(name, "weapon name", Limits.Name)));
                var weapon = $"{unit}weapon '{name}' ";
                var attacks = attacksOrNull ?? 1;
                if (attacks is < ArmyLimits.MinAttacks or > ArmyLimits.MaxAttacks) throw new InvalidArmyException($"{weapon}has {attacks} attacks; the range is {ArmyLimits.MinAttacks}–{ArmyLimits.MaxAttacks}.");
                Range(range, weapon);
                Rules(rules);
                return weapon;
            }
            Rules(u.Rules);
            foreach (var item in u.Loadout ?? [])
            {
                if (item.Type == "ArmyBookWeapon")
                {
                    var weapon = Weapon(item.Name, item.Attacks, item.Range, item.SpecialRules);
                    var maxCount = u.Size * ArmyLimits.WeaponsPerModel;
                    if (item.Count < 0 || item.Count > maxCount) throw new InvalidArmyException($"{weapon}count {item.Count} is outside 0–{maxCount}.");
                }
                else if (item.Type == "ArmyBookItem")
                {
                    Check(Prefixed(unit, Limits.Text(item.Name, "item name", Limits.Name)));
                    if (ParseBase(item.Bases) is { } ib && ArmyLimits.Base(ib) is not null)
                        throw new InvalidArmyException($"{unit}item '{item.Name}' base {Mm(ib)} mm is outside {ArmyLimits.MinBase}–{ArmyLimits.MaxBase} inches.");
                    var content = item.Content ?? [];
                    Rules(content.Where(r => !r.IsWeapon).ToList());
                    foreach (var w in content.Where(r => r.IsWeapon)) Weapon(w.Name, w.Attacks, w.Range, w.SpecialRules);
                }
            }
            if (ParseBase(u.Bases) is { } b && ArmyLimits.Base(b) is not null)
                throw new InvalidArmyException($"{unit}base {Mm(b)} mm is outside {ArmyLimits.MinBase}–{ArmyLimits.MaxBase} inches.");
        }
        var models = list.Units.Sum(u => u.Size);
        if (models > ArmyLimits.MaxModels) throw new InvalidArmyException($"The list has {models} models; at most {ArmyLimits.MaxModels} import.");
    }

    private static string? Prefixed(string prefix, string? problem) => problem is null ? null : prefix + problem;

    /// <summary>A selectionId is never checked text, so a message quotes it only when it would pass as a name.</summary>
    private static string Quoted(string id) => Limits.Text(id, "id", Limits.Name) is null ? id : "(unprintable)";

    private static string Mm(Base b)
    {
        static string F(double inches) => (inches * 25.4).ToString("G4", CultureInfo.InvariantCulture);
        return b.Shape == BaseShape.Circle ? F(b.A) : $"{F(b.A)}x{F(b.B)}";
    }

    /// <summary>A unit as it will be built: the host entry, the entry combined with it, then the entries whose models join it.</summary>
    private sealed record Merged(AfUnit Host, List<AfUnit> Combined, List<AfUnit> Joined);

    /// <summary>Two combined entries are one unit: the partner's models follow the host's, each entry's loadout on its own models. Then a
    /// joining entry becomes a section of the entry it names, an absorbed partner's id resolving to the entry that absorbed it. A name nothing
    /// answers, and a name whose entry itself joins, leave the joining entry as its own unit — which is also what breaks a cycle without
    /// walking one.</summary>
    private static List<Merged> Merge(List<AfUnit> units)
    {
        var survivors = new List<(AfUnit Host, List<AfUnit> Combined)>();
        var absorbed = new Dictionary<string, string>();          // a combined partner's selectionId → the id that absorbed it
        foreach (var u in units)
        {
            if (absorbed.ContainsKey(u.SelectionId)) continue;
            var partner = u.Combined ? units.FirstOrDefault(o => o.Combined && o.SelectionId != u.SelectionId && !absorbed.ContainsKey(o.SelectionId) && (o.JoinToUnit == u.SelectionId || u.JoinToUnit == o.SelectionId)) : null;
            if (partner is null) { survivors.Add((u, [])); continue; }
            absorbed[partner.SelectionId] = u.SelectionId;
            survivors.Add((u with { JoinToUnit = null }, [partner]));
        }

        var byId = survivors.ToDictionary(s => s.Host.SelectionId, s => s.Host);
        AfUnit? Host(string id) => byId.GetValueOrDefault(absorbed.GetValueOrDefault(id, id));
        bool Joins(AfUnit u) => u.JoinToUnit is not null && Host(u.JoinToUnit) is { JoinToUnit: null };

        var result = survivors.Where(s => !Joins(s.Host)).Select(s => new Merged(s.Host, s.Combined, [])).ToList();
        var hosts = result.ToDictionary(m => m.Host.SelectionId);
        foreach (var j in survivors.Where(s => Joins(s.Host))) hosts[Host(j.Host.JoinToUnit!)!.SelectionId].Joined.Add(j.Host);
        return result;
    }

    private static Unit Unit(Merged m, int ordinal, IReadOnlyDictionary<string, PropertyLevel> catalog)
    {
        var host = m.Host;
        var stats = new Dictionary<string, string> { ["Quality"] = Target(host.Quality), ["Defense"] = Target(host.Defense) };
        var unitProps = new List<PropertyInstance>();
        var models = new List<ModelSlot>();
        Section([host, .. m.Combined], catalog, unitProps, models);
        foreach (var joined in m.Joined) Section([joined], catalog, null, models);
        return new Unit(Guid.CreateVersion7(), ordinal, host.Name, stats, Stacked(unitProps), models, Points: Points([host, .. m.Combined, .. m.Joined]));
    }

    /// <summary>What the list says a unit cost: each of its entries' `cost` and, per selected upgrade, the option's cost for that entry (its
    /// `costs` row naming the entry's `id`, else its plain `cost`). Null when no entry carries a cost; kept within ArmyLimits.MaxPoints.</summary>
    private static int? Points(IReadOnlyList<AfUnit> entries)
    {
        long total = 0; var any = false;
        foreach (var u in entries)
        {
            if (u.Cost is not { ValueKind: JsonValueKind.Number } cost || !cost.TryGetInt32(out var own)) continue;
            any = true; total += own;
            if (u.SelectedUpgrades is not { ValueKind: JsonValueKind.Array } selected) continue;
            foreach (var s in selected.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object || !s.TryGetProperty("option", out var option) || option.ValueKind != JsonValueKind.Object) continue;
                int? price = null;
                if (u.Id is not null && option.TryGetProperty("costs", out var costs) && costs.ValueKind == JsonValueKind.Array)
                    foreach (var row in costs.EnumerateArray())
                        if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("unitId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == u.Id
                            && row.TryGetProperty("cost", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n)) { price = n; break; }
                if (price is null && option.TryGetProperty("cost", out var plain) && plain.ValueKind == JsonValueKind.Number && plain.TryGetInt32(out var p)) price = p;
                total += price ?? 0;
            }
        }
        return any ? (int)Math.Clamp(total, 0, ArmyLimits.MaxPoints) : null;
    }

    /// <summary>One section's slots, appended to the unit being built: an entry and the entry combined with it, or a joined entry.
    /// `unitProps` is null for a joined entry: every rule of it goes on its own slots whatever level the catalog gives the name, and its
    /// Quality and Defense become slot stats, because the unit's own stats and properties stay the host's. Which models hold which loadout
    /// line is ArmyForgeModels' reading of each entry alone, so a joined hero's loadout never reaches a host slot, nor one combined
    /// entry's another's.</summary>
    private static void Section(IReadOnlyList<AfUnit> entries, IReadOnlyDictionary<string, PropertyLevel> catalog, List<PropertyInstance>? unitProps, List<ModelSlot> into)
    {
        var u = entries[0];
        var size = entries.Sum(e => e.Size);
        var slotProps = Enumerable.Range(0, size).Select(_ => new List<PropertyInstance>()).ToList();
        var slotWeapons = Enumerable.Range(0, size).Select(_ => new List<Weapon>()).ToList();
        var slotBodies = entries.SelectMany(e => Enumerable.Repeat(Body(e.Bases), e.Size)).ToList();
        var slotBooks = entries.SelectMany(e => Enumerable.Repeat(e.ArmyId, e.Size)).ToList();
        var slotGear = Enumerable.Range(0, size).Select(_ => new List<string>()).ToList();   // the upgrades only some models carry, by name

        // A rule every model of the section carries alike is the unit's (unless the catalog says model), once per copy; anything else is
        // its carriers'.
        void Place(PropertyInstance p, List<int> carriers)
        {
            if (unitProps is not null && carriers.Count % size == 0 && carriers.Distinct().Count() == size && catalog.GetValueOrDefault(p.Name) != PropertyLevel.Model)
            {
                for (var k = 0; k < carriers.Count / size; k++) unitProps.Add(p);
                return;
            }
            foreach (var i in carriers) slotProps[i].Add(p);
        }

        void Arm(List<int> carriers, string name, int? attacks, JsonElement? rangeValue, List<AfRule>? rules)
        {
            var wstats = new Dictionary<string, string> { ["Attacks"] = (attacks ?? 1).ToString(CultureInfo.InvariantCulture) };
            // Melee is `"range": 0` on some entries and `"range": null` on others in the same export; `is { } range and > 0` excludes both.
            if (Range(rangeValue, "") is { } range and > 0) wstats["Range"] = $"{range.ToString(CultureInfo.InvariantCulture)}\"";
            var props = Stacked((rules ?? []).Select(r => new PropertyInstance(r.Name, Rating(r.Rating))));
            foreach (var i in carriers) slotWeapons[i].Add(new(Guid.CreateVersion7(), name, new Dictionary<string, string>(wstats), props));
        }

        foreach (var r in u.Rules ?? []) Place(new PropertyInstance(r.Name, Rating(r.Rating)), Enumerable.Range(0, size).ToList());
        var offset = 0;
        foreach (var entry in entries)
        {
            var lines = entry.Loadout ?? [];
            var carriersOf = ArmyForgeModels.Carriers(entry);
            for (var l = 0; l < lines.Count; l++)
            {
                var item = lines[l];
                var carriers = carriersOf[l].Select(i => i + offset).ToList();
                if (item.Type == "ArmyBookWeapon") Arm(carriers, item.Name, item.Attacks, item.Range, item.SpecialRules);
                else if (item.Type == "ArmyBookItem")
                {
                    // One upgrade's rules and weapons go to the same models.
                    foreach (var i in carriers) if (!slotGear[i].Contains(item.Name)) slotGear[i].Add(item.Name);
                    if (ParseBase(item.Bases) is { } mount) foreach (var i in carriers) slotBodies[i] = new ModelBody(mount, HeightFromBase(mount));
                    foreach (var content in item.Content ?? [])
                        if (content.IsWeapon) Arm(carriers, content.Name, content.Attacks, content.Range, content.SpecialRules);
                        else Place(new PropertyInstance(content.Name, Rating(content.Rating)), carriers);
                }
            }
            offset += entry.Size;
        }

        // An upgrade every model carries (in one line or several) names no one.
        foreach (var everyone in slotGear.SelectMany(g => g).Distinct().Where(g => slotGear.All(x => x.Contains(g))).ToList())
            foreach (var g in slotGear) g.Remove(everyone);

        for (var i = 0; i < size; i++)
        {
            var e = entries[0];
            for (int at = 0, sum = 0; at < entries.Count; sum += entries[at].Size, at++) if (i >= sum) e = entries[at];
            var slotStats = unitProps is null
                ? new Dictionary<string, string> { ["Quality"] = Target(e.Quality), ["Defense"] = Target(e.Defense) }
                : new Dictionary<string, string>();
            slotWeapons[i].AddRange(ArmyBookAdditions.Weapons(slotProps[i].Concat(unitProps ?? []), slotWeapons[i]));
            // A model carrying an upgrade the rest of its unit does not (a banner, a weapon team) is named for it, so it can be told apart
            // when a mesh is picked: "Infantry Squad (Company Standard) 2".
            var named = slotGear[i].Count > 0 ? $"{u.Name} ({string.Join(", ", slotGear[i])}) {i + 1}" : null;
            var slotName = named is { Length: <= Limits.Name } ? named : $"{u.Name} {i + 1}";
            into.Add(new ModelSlot(Guid.CreateVersion7(), into.Count + 1, slotName, slotStats,
                Stacked(slotProps[i]), slotBodies[i].Base, slotBodies[i].Height, null, slotWeapons[i], slotBooks[i]));
        }
    }

    /// <summary>One property per rule name, in first-seen order (an army's tables key properties by name). p.13: "Effects from multiple
    /// instances of the same special rule or spell don't stack, unless it is a rule with (X) in its name", so rated instances add
    /// (Tough(3) and a mount's Tough(12) are Tough(15)) and unrated ones are one.</summary>
    internal static List<PropertyInstance> Stacked(IEnumerable<PropertyInstance> props) =>
        props.GroupBy(p => p.Name)
            .Select(g => g.Any(p => p.Parameter is not null) ? new PropertyInstance(g.Key, g.Sum(p => p.Parameter ?? 0)) : g.First())
            .ToList();

    /// <summary>The published base with HeightFromBase of that base, or DefaultBody when the entry publishes none.</summary>
    private static ModelBody Body(AfBases? bases) =>
        ParseBase(bases) is { } chosen ? new ModelBody(chosen, HeightFromBase(chosen)) : DefaultBody;

    /// <summary>`bases.round` when present: "32" → circle 32 mm, "60x35" → oval 60×35 mm. Otherwise `bases.square`:
    /// "30" → rect 30×30 mm, "50x25" → rect 50×25 mm. Unparseable or absent → null. Inches out.</summary>
    public static Base? ParseBase(AfBases? bases) => Parse(bases?.Round, round: true) ?? Parse(bases?.Square, round: false);

    private static Base? Parse(string? text, bool round)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.ToLowerInvariant().Split('x');
        if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return round ? Base.Circle(d / 25.4) : Base.Rect(d / 25.4, d / 25.4);
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
            return round ? Base.Oval(l / 25.4, w / 25.4) : Base.Rect(l / 25.4, w / 25.4);
        return null;
    }

    /// <summary>A validated quality or defense as its stat value: "4+".</summary>
    private static string Target(JsonElement e) => $"{TargetNumber(e, "", "")}+";

    /// <summary>A number (4) or a string ("4+", "4"), 2 to 6.</summary>
    private static int TargetNumber(JsonElement e, string unit, string field)
    {
        int? n = e.ValueKind switch
        {
            JsonValueKind.Number when e.TryGetInt32(out var v) => v,
            JsonValueKind.String when int.TryParse(e.GetString()!.TrimEnd('+'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) => v,
            _ => null,
        };
        if (n is not { } t) throw new InvalidArmyException($"{unit}{field} must be a number from {ArmyLimits.MinTarget} to {ArmyLimits.MaxTarget}.");
        if (t is < ArmyLimits.MinTarget or > ArmyLimits.MaxTarget) throw new InvalidArmyException($"{unit}{field} {t} is outside {ArmyLimits.MinTarget}–{ArmyLimits.MaxTarget}.");
        return t;
    }

    private static int? Rating(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.Number } r when r.TryGetInt32(out var n) => n,
        { ValueKind: JsonValueKind.String } r when int.TryParse(r.GetString(), out var n) => n,
        _ => null,
    };

    /// <summary>Null for an absent or null range, else a whole number of inches, 0 to 120.</summary>
    private static int? Range(JsonElement? e, string weapon)
    {
        if (e is not { } r || r.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (r.ValueKind != JsonValueKind.Number || !r.TryGetInt32(out var n)) throw new InvalidArmyException($"{weapon}range must be a whole number of inches.");
        if (n is < 0 or > ArmyLimits.MaxRange) throw new InvalidArmyException($"{weapon}range {n} is outside 0–{ArmyLimits.MaxRange}.");
        return n;
    }
}
