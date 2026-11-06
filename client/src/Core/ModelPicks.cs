using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A model a Shift+drag box may take: its id, its unit, and its base centre on screen (pixels).</summary>
public readonly record struct PickCandidate(Guid ModelId, Guid UnitId, double X, double Y);

/// <summary>A Shift+drag box on screen, from the press (FromX, FromY) to the cursor (ToX, ToY), drawn either way.</summary>
public readonly record struct ScreenBox(double FromX, double FromY, double ToX, double ToY)
{
    public bool Contains(double x, double y) =>
        x >= Math.Min(FromX, ToX) && x <= Math.Max(FromX, ToX) && y >= Math.Min(FromY, ToY) && y <= Math.Max(FromY, ToY);
}

/// <summary>
/// The picked models (in-game UX spec → Moves, Picking models): one unit's, in pick order. Shift+click toggles a model, a Shift+drag
/// box adds the models inside it, and a pick naming another unit starts over with that unit. A plain drag on a picked model holds
/// the picks; one on any other model holds its whole unit and clears them. Immutable: every change returns the next picks.
/// </summary>
public sealed record ModelPicks(Guid? UnitId, IReadOnlyList<Guid> ModelIds)
{
    public static readonly ModelPicks None = new(null, []);

    public bool IsEmpty => ModelIds.Count == 0;

    public bool Contains(Guid modelId) => ModelIds.Contains(modelId);

    /// <summary>The picks a selection names: its model ids under its piece, none when it names no model.</summary>
    public static ModelPicks Of(Selection? selection) =>
        selection is { ModelIds.Count: > 0 } s ? new ModelPicks(s.PieceId, s.ModelIds) : None;

    /// <summary>The selection that shows the picks: the unit with the picked models, or the whole unit with none.</summary>
    public Selection Selection(Guid unitId) => new(unitId, UnitId == unitId ? ModelIds : []);

    /// <summary>Shift+click: the model in, or out when already picked. A model of another unit starts over with it alone.</summary>
    public ModelPicks Toggle(Guid unitId, Guid modelId)
    {
        if (UnitId != unitId || IsEmpty) return new ModelPicks(unitId, [modelId]);
        if (!Contains(modelId)) return this with { ModelIds = [.. ModelIds, modelId] };
        var left = ModelIds.Where(m => m != modelId).ToList();
        return left.Count == 0 ? None : this with { ModelIds = left };
    }

    /// <summary>
    /// The unit a box takes and its models inside it, in candidate order: the unit with most base centres inside, on a tie the
    /// unit of the model nearest the press (the box's From corner). Null when no centre is inside.
    /// </summary>
    public static (Guid UnitId, IReadOnlyList<Guid> ModelIds)? InBox(IEnumerable<PickCandidate> candidates, ScreenBox box)
    {
        var inside = candidates.Where(c => box.Contains(c.X, c.Y)).ToList();
        if (inside.Count == 0) return null;
        static double Distance2(PickCandidate c, ScreenBox b) => (c.X - b.FromX) * (c.X - b.FromX) + (c.Y - b.FromY) * (c.Y - b.FromY);
        var unit = inside.GroupBy(c => c.UnitId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Min(c => Distance2(c, box)))
            .First().Key;
        return (unit, inside.Where(c => c.UnitId == unit).Select(c => c.ModelId).ToList());
    }

    /// <summary>Shift+drag released: the box's models added to the picks of their unit, or the picks start over with them for
    /// another unit. A box with no model inside changes nothing.</summary>
    public ModelPicks Boxed(IEnumerable<PickCandidate> candidates, ScreenBox box)
    {
        if (InBox(candidates, box) is not { } hit) return this;
        if (UnitId != hit.UnitId || IsEmpty) return new ModelPicks(hit.UnitId, hit.ModelIds);
        return this with { ModelIds = [.. ModelIds, .. hit.ModelIds.Where(m => !Contains(m))] };
    }

    /// <summary>What a plain drag pressed on this model holds: the picks among the unit's drawn models, in the unit's order, when the
    /// model is picked; else every model of its unit.</summary>
    public IReadOnlyList<Guid> Held(Guid pressedModelId, IReadOnlyList<Guid> unitModelIds) =>
        Contains(pressedModelId) ? unitModelIds.Where(Contains).ToList() : unitModelIds;

    /// <summary>The picks after a plain press on this model: kept when it is picked, cleared when it is not.</summary>
    public ModelPicks Pressed(Guid pressedModelId) => Contains(pressedModelId) ? this : None;
}
