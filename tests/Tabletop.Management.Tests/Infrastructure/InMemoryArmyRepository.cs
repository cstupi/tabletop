using Tabletop.Content;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public sealed class InMemoryArmyRepository(InMemoryRows rows) : IArmyRepository
{
    /// <summary>The army tables' keys: unique ordinals per army and per unit, one property row per name.</summary>
    private static void Check(Army army)
    {
        static void Unique<T>(IEnumerable<T> keys, string what) { if (keys.GroupBy(k => k).Any(g => g.Count() > 1)) throw new InvalidOperationException($"duplicate key: {what}"); }
        Unique(army.Units.Select(u => u.Ordinal), "unit ordinal");
        foreach (var u in army.Units)
        {
            Unique(u.Models.Select(m => m.Ordinal), "slot ordinal");
            Unique(u.Properties.Select(p => p.Name), "unit property");
            foreach (var m in u.Models)
            {
                Unique(m.Properties.Select(p => p.Name), "slot property");
                foreach (var w in m.Weapons) Unique(w.Properties.Select(p => p.Name), "weapon property");
            }
        }
    }

    private static Army Ordered(Army a) => a with { Units = a.Units.OrderBy(u => u.Ordinal).Select(u => u with { Models = u.Models.OrderBy(m => m.Ordinal).ToList() }).ToList() };

    public Task Insert(Guid ownerId, Army army)
    {
        lock (rows.Gate)
        {
            rows.RequireAccount(ownerId);
            Check(army);
            rows.Armies.Add(army.Id, new InMemoryRows.ArmyRow { Owner = ownerId, Army = army });
        }
        return Task.CompletedTask;
    }

    public Task<Army?> Get(Guid ownerId, Guid id)
    {
        lock (rows.Gate) return Task.FromResult(rows.Armies.TryGetValue(id, out var a) && a.Owner == ownerId ? Ordered(a.Army) : null);
    }

    public Task<IReadOnlyList<ArmySummary>> List(Guid ownerId, Keyset page)
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyList<ArmySummary>>(rows.Armies
                .Where(a => a.Value.Owner == ownerId && (page.After is null || a.Key.CompareTo(page.After.Value) > 0))
                .OrderBy(a => a.Key).Take(page.Limit)
                .Select(a => new ArmySummary(a.Key, a.Value.Army.Name, a.Value.Army.GameSystem, a.Value.Army.Source, a.Value.Army.Units.Count, a.Value.Army.Units.Sum(u => u.Models.Count), a.Value.Army.Edition))
                .ToList());
    }

    public Task<int> Count(Guid ownerId)
    {
        lock (rows.Gate) return Task.FromResult(rows.Armies.Values.Count(a => a.Owner == ownerId));
    }

    public Task<Army?> Patch(Guid ownerId, Guid id, PatchArmyRequest p)
    {
        lock (rows.Gate)
        {
            if (!rows.Armies.TryGetValue(id, out var row) || row.Owner != ownerId) return Task.FromResult<Army?>(null);
            var army = row.Army with { Name = p.Name ?? row.Army.Name };
            foreach (var u in p.Units ?? [])
                army = army with { Units = army.Units.Select(x => x.Id != u.UnitId ? x : x with { Models = x.Models.Select(m => m with { AssetId = u.AssetId }).ToList() }).ToList() };
            foreach (var s in p.Slots ?? [])
                army = army with { Units = army.Units.Select(x => x with { Models = x.Models.Select(m => m.Id != s.SlotId ? m : m with
                {
                    AssetId = s.ClearAsset ? null : s.AssetId ?? m.AssetId,
                    Base = s.Base ?? m.Base,
                    Height = s.Height ?? m.Height,
                }).ToList() }).ToList() };
            row.Army = army;
            return Task.FromResult<Army?>(Ordered(army));
        }
    }

    public Task<bool> Delete(Guid ownerId, Guid id)
    {
        lock (rows.Gate)
        {
            if (!rows.Armies.TryGetValue(id, out var a) || a.Owner != ownerId) return Task.FromResult(false);
            rows.RemoveArmy(id);
            return Task.FromResult(true);
        }
    }
}

public sealed class InMemoryCatalogKinds(InMemoryRows rows) : ICatalogKinds
{
    public Task<IReadOnlyDictionary<string, PropertyLevel>> For(string gameRules)
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyDictionary<string, PropertyLevel>>(rows.CatalogEntries
                .Where(e => e.Kind is "unit" or "model" && rows.Properties.TryGetValue(e.Hash, out var p) && p.GameRules == gameRules)
                .Select(e => (e.Name, e.Kind)).Distinct()
                .ToDictionary(e => e.Name, e => e.Kind == "model" ? PropertyLevel.Model : PropertyLevel.Unit));
    }
}
