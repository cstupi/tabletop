using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using Tabletop.Content;

namespace Tabletop.Management.Content.Armies;

/// <summary>
/// The reserve units an import adds for the army-book rules that place a unit the list does not hold (army-book rules spec → Readings, batch 7):
/// Spawn ("place a new unit of X fully within 6" of it", one per carrying model), Split ("When this unit is fully destroyed, you may place a new unit of X",
/// one per unit) and Reinforcement ("place a new copy of it … this rule doesn't apply to the new copy", one per unit where all models have it). X is the
/// rule's rating, "Name [size]", resolved against the units the generator took from the carrying entry's army book (<c>army-book-units.&lt;edition&gt;.json</c>, the edition the import stamps). An added
/// unit with such a rule of its own gets its reserve too. A reserve unit is appended after the list's units; reserves stop at the army limits.
/// </summary>
public static class ArmyBookReserves
{
    public const string Spawn = "Spawn", Split = "Split", Reinforcement = "Reinforcement";
    /// <summary>The army limits the import holds every army to.</summary>
    public const int MaxUnits = ArmyLimits.MaxUnits, MaxModels = ArmyLimits.MaxModels;

    sealed record BookUnit(int Size, JsonElement Quality, JsonElement Defense, AfBases? Bases, List<AfRule>? Rules, List<AfLoadoutItem>? Loadout);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, Dictionary<string, BookUnit>>> Loaded = new();

    /// <summary>Every edition a units file is embedded for.</summary>
    public static IReadOnlyList<string> Editions => Assembly.GetExecutingAssembly().GetManifestResourceNames()
        .Select(n => Regex.Match(n, @"^army-book-units\.(.+)\.json$")).Where(m => m.Success).Select(m => m.Groups[1].Value).Order().ToList();

    /// <summary>The edition's units, keyed by book uid then unit name.</summary>
    static IReadOnlyDictionary<string, Dictionary<string, BookUnit>> BooksOf(string edition) => Loaded.GetOrAdd(edition, e =>
    {
        var name = $"army-book-units.{e}.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, BookUnit>>>(stream, Json)!;
    });

    /// <summary>Throws unless a units file is embedded for `served` and for every edition in `all`.</summary>
    public static void RequireFiles(string served, IEnumerable<string> all)
    {
        var held = Editions;
        var missing = all.Append(served).Distinct().Where(e => !held.Contains(e)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"No army-book-units file for edition {string.Join(", ", missing)}");
    }

    /// <summary>"Name [size]" from a rating; null for any other.</summary>
    static (string Name, int Size)? Rating(AfRule r)
    {
        if (r.Rating is not { ValueKind: JsonValueKind.String } e) return null;
        var text = e.GetString()!.Trim();
        var open = text.LastIndexOf('[');
        return open > 0 && text.EndsWith(']') && int.TryParse(text[(open + 1)..^1], out var size) && size is >= 1 and <= 50 ? (text[..open].Trim(), size) : null;
    }

    /// <summary>The entry's ratings of a rule, one per model that carries it: a rule of the entry is every model's, an item's goes to `count` models.</summary>
    static IEnumerable<(string Name, int Size)> Ratings(AfUnit entry, string rule) =>
        (entry.Rules ?? []).Where(r => r.Name == rule).Select(Rating).SelectMany(x => Enumerable.Repeat(x, entry.Size))
            .Concat((entry.Loadout ?? []).Where(i => i.Type == "ArmyBookItem").SelectMany(i => (i.Content ?? []).Where(r => r.Name == rule).Select(Rating).SelectMany(x => Enumerable.Repeat(x, i.Count))))
            .Where(x => x is not null).Select(x => x!.Value);

    /// <summary>The book's unit X at X's size, as an export entry: a loadout line every model of the book's unit had goes to every model.</summary>
    public static AfUnit? Entry(string edition, string? book, (string Name, int Size) x)
    {
        if (book is null || !BooksOf(edition).TryGetValue(book, out var units) || !units.TryGetValue(x.Name, out var u)) return null;
        var loadout = (u.Loadout ?? []).Select(l => l with { Count = l.Count >= u.Size ? x.Size : Math.Min(l.Count, x.Size) }).ToList();
        return new AfUnit(Guid.NewGuid().ToString("N"), x.Name, x.Size, u.Quality, u.Defense, u.Rules, loadout, u.Bases, false, null, book);
    }

    static bool Holds(Unit u, string rule, bool all) =>
        u.Properties.Any(p => p.Name == rule) || (all ? u.Models.Count > 0 && u.Models.All(m => m.Properties.Any(p => p.Name == rule)) : u.Models.Any(m => m.Properties.Any(p => p.Name == rule)));

    /// <summary>The list's units, then their reserve units. `entries` are each unit's export entries (host, then joined); `build` makes a unit from one entry.</summary>
    public static List<Unit> Add(List<Unit> units, IReadOnlyList<IReadOnlyList<AfUnit>> entries, Func<AfUnit, int, Unit> build, string edition)
    {
        var all = new List<Unit>(units);
        var sources = units.Select((u, i) => (Unit: u, Entries: entries[i])).ToList();
        var models = all.Sum(u => u.Models.Count);
        bool Room(int more) => all.Count < MaxUnits && models + more <= MaxModels;
        void Append(Unit reserve, IReadOnlyList<AfUnit> from)
        {
            all.Add(reserve);
            models += reserve.Models.Count;
            sources.Add((reserve, from));
        }
        void Reserve(AfUnit? entry, UnitReserve link)
        {
            if (entry is null || !Room(entry.Size)) return;
            Append(build(entry, all.Count + 1) with { Reserve = link }, [entry]);
        }
        for (var i = 0; i < sources.Count; i++)
        {
            var (unit, from) = sources[i];
            // Spawn: each carrying model, in slot order, takes the next of the entries' Spawn ratings.
            var carriers = unit.Models.Where(m => m.Properties.Any(p => p.Name == Spawn) || unit.Properties.Any(p => p.Name == Spawn)).ToList();
            foreach (var (model, x) in carriers.Zip(from.SelectMany(e => Ratings(e, Spawn))))
                Reserve(Entry(edition, model.ArmyBook, x), new UnitReserve(Spawn, model.Id));
            if (Holds(unit, Split, all: false) && from.SelectMany(e => Ratings(e, Split).Select(x => (Book: e.ArmyId, X: x))).FirstOrDefault() is { X.Name: not null } split)
                Reserve(Entry(edition, split.Book, split.X), new UnitReserve(Split, unit.Id));
            if (unit.Reserve?.Property != Reinforcement && Holds(unit, Reinforcement, all: true) && Room(unit.Models.Count))
                Append(Copy(unit, all.Count + 1) with { Reserve = new UnitReserve(Reinforcement, unit.Id) }, from);
        }
        return all;
    }

    /// <summary>"A new copy of it", fresh ids throughout; "this rule doesn't apply to the new copy of the unit".</summary>
    static Unit Copy(Unit u, int ordinal)
    {
        static List<PropertyInstance> Without(IEnumerable<PropertyInstance> props) => props.Where(p => p.Name != Reinforcement).ToList();
        return u with
        {
            Id = Guid.CreateVersion7(), Ordinal = ordinal, Properties = Without(u.Properties),
            Models = u.Models.Select(m => m with
            {
                Id = Guid.CreateVersion7(), Properties = Without(m.Properties),
                Weapons = m.Weapons.Select(w => w with { Id = Guid.CreateVersion7() }).ToList(),
            }).ToList(),
        };
    }
}
