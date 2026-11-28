using Godot;
using Tabletop.Client.Core;
using GArray = Godot.Collections.Array;

namespace Tabletop.Client.Table;

/// <summary>Low quality's base swap (<see cref="LowDetail"/>): each surface's chosen level of detail becomes its base, the finer levels
/// and the vertices only they drew are dropped. Runs on the parse's thread, before the scene is generated.</summary>
static class LowDetailMesh
{
    /// <summary>The bits of a surface's format that are not the arrays' presence (custom formats, 8 bone weights), which AddSurface takes as flags.</summary>
    const ulong PresenceBits = (1UL << 13) - 1;

    public static void Apply(GltfState state)
    {
        var meshes = state.GetMeshes().Select(m => m.Mesh).Where(m => m is not null).ToList();
        long total = 0;
        foreach (var m in meshes) for (var s = 0; s < m!.GetSurfaceCount(); s++) total += Indices(m, s)?.Length / 3 ?? 0;
        if (LowDetail.Ratio(total) is not { } ratio) return;
        foreach (var m in meshes) if (m!.GetBlendShapeCount() == 0) Swap(m, ratio);
    }

    static int[]? Indices(ImporterMesh m, int s) => m.GetSurfaceArrays(s)[(int)Mesh.ArrayType.Index].AsInt32Array() is { Length: > 0 } i ? i : null;

    static void Swap(ImporterMesh m, double ratio)
    {
        var surfaces = new List<(Mesh.PrimitiveType Type, GArray Arrays, Godot.Collections.Dictionary Lods, Material? Material, string Name, ulong Flags)>();
        for (var s = 0; s < m.GetSurfaceCount(); s++)
        {
            var arrays = m.GetSurfaceArrays(s);
            var lods = new Godot.Collections.Dictionary();
            var levels = Enumerable.Range(0, m.GetSurfaceLodCount(s)).Select(l => (Size: m.GetSurfaceLodSize(s, l), Indices: m.GetSurfaceLodIndices(s, l))).ToList();
            var baseIndices = Indices(m, s);
            if (baseIndices is not null && LowDetail.PickLevel(baseIndices.Length / 3, levels.Select(l => l.Indices.Length / 3).ToList(), ratio) is { } pick)
            {
                var coarser = levels.Skip(pick + 1).ToList();
                var c = LowDetail.Compact(levels[pick].Indices, coarser.Select(l => l.Indices).ToList());
                var vertexCount = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
                for (var a = 0; a < (int)Mesh.ArrayType.Max; a++)
                    if (a != (int)Mesh.ArrayType.Index && arrays[a].VariantType != Variant.Type.Nil) arrays[a] = Gather(arrays[a], vertexCount, c.Used);
                arrays[(int)Mesh.ArrayType.Index] = c.Base;
                for (var l = 0; l < coarser.Count; l++) lods[coarser[l].Size] = c.Levels[l];
            }
            else foreach (var (size, indices) in levels) lods[size] = indices;
            surfaces.Add((m.GetSurfacePrimitiveType(s), arrays, lods, m.GetSurfaceMaterial(s), m.GetSurfaceName(s), m.GetSurfaceFormat(s) & ~PresenceBits));
        }
        m.Clear();
        foreach (var (type, arrays, lods, material, name, flags) in surfaces) m.AddSurface(type, arrays, null, lods, material, name, flags);
    }

    /// <summary>A per-vertex packed array cut to the used vertices; its width (components per vertex) from its length.</summary>
    static Variant Gather(Variant source, int vertexCount, int[] used)
    {
        T[] Cut<T>(T[] a) => LowDetail.Gather(a, a.Length / vertexCount, used);
        return source.VariantType switch
        {
            Variant.Type.PackedVector3Array => Cut(source.AsVector3Array()),
            Variant.Type.PackedVector2Array => Cut(source.AsVector2Array()),
            Variant.Type.PackedFloat32Array => Cut(source.AsFloat32Array()),
            Variant.Type.PackedFloat64Array => Cut(source.AsFloat64Array()),
            Variant.Type.PackedColorArray => Cut(source.AsColorArray()),
            Variant.Type.PackedInt32Array => Cut(source.AsInt32Array()),
            Variant.Type.PackedByteArray => Cut(source.AsByteArray()),
            _ => source,
        };
    }
}
