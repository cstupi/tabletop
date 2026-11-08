using Tabletop.Content;

namespace Tabletop.Management.Content.Armies;

/// <summary>The army tree: army, unit, model slot, weapon, each with its stats and properties. ArmyRepository is the Postgres implementation.</summary>
public interface IArmyRepository
{
    /// <summary>Stores the tree with the ids it carries.</summary>
    Task Insert(Guid ownerId, Army army);
    /// <summary>Units and slots by ordinal; stats, properties and weapons in insertion order.</summary>
    Task<Army?> Get(Guid ownerId, Guid id);
    /// <summary>Ascending by id, after the cursor.</summary>
    Task<IReadOnlyList<ArmySummary>> List(Guid ownerId, Http.Keyset page);
    /// <summary>The owner's slots with a mesh, across all armies, grouped by unit name, model name (the slot name without a trailing ordinal) and mesh; most slots first, then by unit
    /// name and model name (ordinal) and mesh id.</summary>
    Task<IReadOnlyList<ArmyMeshUse>> MeshUses(Guid ownerId);
    /// <summary>The owner's armies.</summary>
    Task<int> Count(Guid ownerId);
    /// <summary>Unit patches first (every slot of the unit), then slot patches. Patches naming a unit or slot outside the army are ignored.</summary>
    Task<Army?> Patch(Guid ownerId, Guid id, PatchArmyRequest p);
    /// <summary>A participant row that picked the army keeps no army (on delete set null).</summary>
    Task<bool> Delete(Guid ownerId, Guid id);
}
