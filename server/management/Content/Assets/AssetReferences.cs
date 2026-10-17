namespace Tabletop.Management.Content.Assets;

/// <summary>Every write of an asset reference (an army slot's mesh, a layout's texture, terrain and marker visuals) names an
/// uploaded asset the caller owns, of a kind that fits the reference.</summary>
public static class AssetReferences
{
    public static readonly AssetKind[] Miniature = [AssetKind.Miniature];
    public static readonly AssetKind[] Terrain = [AssetKind.Terrain];
    public static readonly AssetKind[] Texture = [AssetKind.Texture];
    /// <summary>A marker's visual is a mesh of either kind.</summary>
    public static readonly AssetKind[] Mesh = [AssetKind.Miniature, AssetKind.Terrain];

    /// <summary>Null when every reference is acceptable, else the problem detail.</summary>
    public static async Task<string?> Check(IAssetRepository assets, Guid ownerId, IReadOnlyCollection<(Guid Id, AssetKind[] Kinds)> refs)
    {
        if (refs.Count == 0) return null;
        var owned = await assets.UploadedKinds(ownerId, refs.Select(r => r.Id).Distinct().ToList());
        foreach (var (id, kinds) in refs)
        {
            if (!owned.TryGetValue(id, out var kind)) return $"Asset {id} is not an uploaded asset of yours.";
            if (!kinds.Contains(kind)) return $"Asset {id} is {AssetRules.Kind(kind)}; this reference takes {string.Join(" or ", kinds.Select(AssetRules.Kind))}.";
        }
        return null;
    }
}
