using Tabletop.Content;

namespace Tabletop.Management.Content.Layouts;

/// <summary>An owner's saved table states. LayoutRepository is the Postgres implementation.</summary>
public interface ILayoutRepository
{
    /// <summary>A copy with a fresh id for the layout and every child.</summary>
    Task<TableState> Save(Guid ownerId, TableState state);
    /// <summary>Terrain, markers and volumes by id; zones by team ordinal.</summary>
    Task<TableState?> Get(Guid ownerId, Guid id);
    /// <summary>Ascending by id.</summary>
    Task<IReadOnlyList<LayoutSummary>> List(Guid ownerId);
    /// <summary>A name alone renames; a state replaces size, texture and every child, with fresh child ids.</summary>
    Task<TableState?> Patch(Guid ownerId, Guid id, PatchLayoutRequest p);
    Task<bool> Delete(Guid ownerId, Guid id);
}
