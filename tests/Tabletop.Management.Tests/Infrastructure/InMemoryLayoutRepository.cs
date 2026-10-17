using Tabletop.Content;
using Tabletop.Management.Content.Layouts;

namespace Tabletop.Management.Tests;

public sealed class InMemoryLayoutRepository(InMemoryRows rows) : ILayoutRepository
{
    /// <summary>Every child with a fresh id, as WriteChildren inserts them.</summary>
    private TableState Copy(Guid id, string? name, TableState s) => new(id, name, s.Size, s.Texture,
        s.Terrain.Select(p => p with { Id = rows.NextId(), Volumes = p.Volumes.Select(v => v with { Id = rows.NextId() }).ToList() }).ToList(),
        s.Markers.Select(m => m with { Id = rows.NextId() }).ToList(),
        s.Zones.Select(z => z with { Id = rows.NextId() }).ToList());

    /// <summary>As ReadTree orders them.</summary>
    private static TableState Read(TableState s) => s with
    {
        Terrain = s.Terrain.OrderBy(p => p.Id).Select(p => p with { Volumes = p.Volumes.OrderBy(v => v.Id).ToList() }).ToList(),
        Markers = s.Markers.OrderBy(m => m.Id).ToList(),
        Zones = s.Zones.OrderBy(z => z.TeamOrdinal).ToList(),
    };

    public Task<TableState> Save(Guid ownerId, TableState state)
    {
        lock (rows.Gate)
        {
            rows.RequireAccount(ownerId);
            var id = rows.NextId();
            var saved = Copy(id, state.Name, state);
            rows.Layouts.Add(id, new InMemoryRows.LayoutRow { Owner = ownerId, State = saved });
            return Task.FromResult(Read(saved));
        }
    }

    public Task<TableState?> Get(Guid ownerId, Guid id)
    {
        lock (rows.Gate) return Task.FromResult(rows.Layouts.TryGetValue(id, out var l) && l.Owner == ownerId ? Read(l.State) : null);
    }

    public Task<IReadOnlyList<LayoutSummary>> List(Guid ownerId)
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyList<LayoutSummary>>(rows.Layouts.Where(l => l.Value.Owner == ownerId).OrderBy(l => l.Key)
                .Select(l => new LayoutSummary(l.Key, l.Value.State.Name!, l.Value.State.Size, l.Value.State.Terrain.Count, l.Value.State.Markers.Count)).ToList());
    }

    public Task<TableState?> Patch(Guid ownerId, Guid id, PatchLayoutRequest p)
    {
        lock (rows.Gate)
        {
            if (!rows.Layouts.TryGetValue(id, out var l) || l.Owner != ownerId) return Task.FromResult<TableState?>(null);
            var name = p.Name ?? l.State.Name;
            l.State = p.State is null ? l.State with { Name = name } : Copy(id, name, p.State);
            return Task.FromResult<TableState?>(Read(l.State));
        }
    }

    public Task<bool> Delete(Guid ownerId, Guid id)
    {
        lock (rows.Gate)
        {
            if (!rows.Layouts.TryGetValue(id, out var l) || l.Owner != ownerId) return Task.FromResult(false);
            rows.Layouts.Remove(id);
            return Task.FromResult(true);
        }
    }
}
