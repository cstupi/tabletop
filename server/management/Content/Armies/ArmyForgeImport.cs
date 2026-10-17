using System.Globalization;
using System.Text.Json;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Management.Content.Armies;

public enum PropertyLevel { Unit, Model }

/// <summary>Which of Army Forge's two published bases an import writes, for the whole army: `bases.round` (circle or oval)
/// or `bases.square` (square or rectangle).</summary>
public enum BaseSet { Round, Square }

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

    public static Army ToArmy(AfList list, string source, IReadOnlyDictionary<string, PropertyLevel> catalog, BaseSet set = BaseSet.Round)
    {
        if (!GameRules.TryGetValue(list.GameSystem, out var gameRules)) throw new UnsupportedGameSystemException(list.GameSystem);
        var units = Merge(list.Units).Select((m, i) => Unit(m, i + 1, catalog, set)).ToList();
        return new Army(Guid.CreateVersion7(), list.Name, gameRules, source, units);
    }

    /// <summary>A unit as it will be built: the host entry, then the entries whose models join it.</summary>
    private sealed record Merged(AfUnit Host, List<AfUnit> Joined);

    /// <summary>Two combined entries are one unit: sizes add, loadout counts add. Then a joining entry becomes a section of the entry
    /// it names, an absorbed partner's id resolving to the entry that absorbed it. A name nothing answers, and a name whose entry
    /// itself joins, leave the joining entry as its own unit — which is also what breaks a cycle without walking one.</summary>
    private static List<Merged> Merge(List<AfUnit> units)
    {
        var survivors = new List<AfUnit>();
        var absorbed = new Dictionary<string, string>();          // a combined partner's selectionId → the id that absorbed it
        foreach (var u in units)
        {
            if (absorbed.ContainsKey(u.SelectionId)) continue;
            var partner = u.Combined ? units.FirstOrDefault(o => o.Combined && o.SelectionId != u.SelectionId && (o.JoinToUnit == u.SelectionId || u.JoinToUnit == o.SelectionId)) : null;
            if (partner is null) { survivors.Add(u); continue; }
            absorbed[partner.SelectionId] = u.SelectionId;
            var loadout = (u.Loadout ?? []).Concat(partner.Loadout ?? [])
                .GroupBy(l => (l.Type, l.Name))
                .Select(g => g.First() with { Count = g.Sum(l => l.Count) }).ToList();
            survivors.Add(u with { Size = u.Size + partner.Size, Loadout = loadout, JoinToUnit = null });
        }

        var byId = survivors.ToDictionary(u => u.SelectionId);
        AfUnit? Host(string id) => byId.GetValueOrDefault(absorbed.GetValueOrDefault(id, id));
        bool Joins(AfUnit u) => u.JoinToUnit is not null && Host(u.JoinToUnit) is { JoinToUnit: null };

        var result = survivors.Where(u => !Joins(u)).Select(u => new Merged(u, [])).ToList();
        var hosts = result.ToDictionary(m => m.Host.SelectionId);
        foreach (var j in survivors.Where(Joins)) hosts[Host(j.JoinToUnit!)!.SelectionId].Joined.Add(j);
        return result;
    }

    private static Unit Unit(Merged m, int ordinal, IReadOnlyDictionary<string, PropertyLevel> catalog, BaseSet set)
    {
        var host = m.Host;
        var stats = new Dictionary<string, string> { ["Quality"] = TargetNumber(host.Quality), ["Defense"] = TargetNumber(host.Defense) };
        var unitProps = new List<PropertyInstance>();
        var models = new List<ModelSlot>();
        Section(host, catalog, set, unitProps, models);
        foreach (var joined in m.Joined) Section(joined, catalog, set, null, models);
        return new Unit(Guid.CreateVersion7(), ordinal, host.Name, stats, unitProps, models);
    }

    /// <summary>One entry's slots, appended to the unit being built. `unitProps` is null for a joined entry: every rule of it goes on
    /// its own slots whatever level the catalog gives the name, and its Quality and Defense become slot stats, because the unit's own
    /// stats and properties stay the host's. The cursor is per section, so a joined hero's loadout never reaches a host slot.</summary>
    private static void Section(AfUnit u, IReadOnlyDictionary<string, PropertyLevel> catalog, BaseSet set, List<PropertyInstance>? unitProps, List<ModelSlot> into)
    {
        var slotProps = Enumerable.Range(0, u.Size).Select(_ => new List<PropertyInstance>()).ToList();
        var slotWeapons = Enumerable.Range(0, u.Size).Select(_ => new List<Weapon>()).ToList();
        var cursor = 0;

        void Place(PropertyInstance p, int count)
        {
            if (count >= u.Size)
            {
                if (unitProps is null || catalog.GetValueOrDefault(p.Name) == PropertyLevel.Model) slotProps.ForEach(s => s.Add(p));
                else unitProps.Add(p);
                return;
            }
            for (var i = 0; i < count; i++) slotProps[(cursor + i) % u.Size].Add(p);
            cursor += count;
        }

        foreach (var r in u.Rules ?? []) Place(new PropertyInstance(r.Name, Rating(r.Rating)), u.Size);
        foreach (var item in u.Loadout ?? [])
        {
            if (item.Type == "ArmyBookWeapon")
            {
                var wstats = new Dictionary<string, string> { ["Attacks"] = (item.Attacks ?? 1).ToString(CultureInfo.InvariantCulture) };
                // Melee is `"range": 0` on some entries and `"range": null` on others in the same export; `is { } range and > 0` excludes both.
                if (Range(item.Range) is { } range and > 0) wstats["Range"] = $"{range.ToString(CultureInfo.InvariantCulture)}\"";
                var props = (item.SpecialRules ?? []).Select(r => new PropertyInstance(r.Name, Rating(r.Rating))).ToList();
                Weapon W() => new(Guid.CreateVersion7(), item.Name, new Dictionary<string, string>(wstats), props);
                if (item.Count >= u.Size) slotWeapons.ForEach(s => s.Add(W()));
                else { for (var i = 0; i < item.Count; i++) slotWeapons[(cursor + i) % u.Size].Add(W()); cursor += item.Count; }
            }
            else if (item.Type == "ArmyBookItem")
                foreach (var rule in item.Content ?? []) Place(new PropertyInstance(rule.Name, Rating(rule.Rating)), item.Count);
        }

        var body = Body(u.Bases, set);
        var slotStats = unitProps is null
            ? new Dictionary<string, string> { ["Quality"] = TargetNumber(u.Quality), ["Defense"] = TargetNumber(u.Defense) }
            : new Dictionary<string, string>();
        for (var i = 0; i < u.Size; i++)
            into.Add(new ModelSlot(Guid.CreateVersion7(), into.Count + 1, $"{u.Name} {i + 1}", new Dictionary<string, string>(slotStats),
                slotProps[i], body.Base, body.Height, null, slotWeapons[i], u.ArmyId));
    }

    /// <summary>The chosen set's base with HeightFromBase of that base, or DefaultBody when that field is absent.</summary>
    private static ModelBody Body(AfBases? bases, BaseSet set)
    {
        if (ParseBase(set == BaseSet.Round ? bases?.Round : bases?.Square, set) is not { } chosen) return DefaultBody;
        return new ModelBody(chosen, HeightFromBase(chosen));
    }

    /// <summary>Round: "32" → circle 32 mm, "60x35" → oval 60×35 mm. Square: "30" → rect 30×30 mm, "50x25" → rect 50×25 mm.
    /// Anything else → null. Inches out.</summary>
    public static Base? ParseBase(string? text, BaseSet set)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.ToLowerInvariant().Split('x');
        if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return set == BaseSet.Round ? Base.Circle(d / 25.4) : Base.Rect(d / 25.4, d / 25.4);
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
            return set == BaseSet.Round ? Base.Oval(l / 25.4, w / 25.4) : Base.Rect(l / 25.4, w / 25.4);
        return null;
    }

    private static string TargetNumber(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => $"{e.GetInt32()}+",
        JsonValueKind.String => e.GetString()!.TrimEnd('+') + "+",
        _ => throw new JsonException("quality/defense must be a number or a string"),
    };

    private static int? Rating(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.Number } r when r.TryGetInt32(out var n) => n,
        { ValueKind: JsonValueKind.String } r when int.TryParse(r.GetString(), out var n) => n,
        _ => null,
    };

    private static double? Range(JsonElement? e) => e is { ValueKind: JsonValueKind.Number } r ? r.GetDouble() : null;
}
