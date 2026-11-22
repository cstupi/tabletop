using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>The selected unit's action buttons over it (spec 2026-11-21-actions-over-unit-design.md): the Ask they answer, the unit, the point
/// above its tallest model at its models' centroid, and the decision box's tiles for its options, in the box's order.</summary>
public sealed record UnitActions(Guid AskId, Guid UnitId, Point3 Anchor, IReadOnlyList<OptionTile> Options)
{
    /// <summary>A button's tooltip: the option's word and its key cap ("Advance  2"); the word alone with no key.</summary>
    public static string Tip(OptionTile t) => t.Key > 0 ? $"{t.Word}  {DecisionCard.KeyCap(t.Key)}" : t.Word;
}

public static class UnitActionsView
{
    static readonly IReadOnlyDictionary<Guid, int> NoWounds = new Dictionary<Guid, int>();

    /// <summary>The buttons while my open Ask asks for an activation (activate, act_or_end_phase) and offers the selected unit an option;
    /// null while watching or replaying, for another team's Ask or any other Ask, with nothing selected, for a unit the Ask offers nothing or
    /// one with no model on the table. The keys are the box's numbering when given (numbered), else the box narrowed to the unit's options.</summary>
    public static UnitActions? Of(Projection p, GameInfo game, Guid? myTeam, Guid? selected, bool watching,
        IReadOnlyList<(int Key, string Label, string OptionId)>? numbered = null)
    {
        if (watching || selected is not { } unit || !p.Units.ContainsKey(unit)) return null;
        if (AskViewModel.Mine(p, game, myTeam)?.Ask is not { Legal: Options options } ask || !DecisionCard.Activations.Contains(ask.Question)) return null;
        var hits = DecisionView.ChoicesOf(options, p, unit);
        if (hits.Count == 0) return null;
        var on = p.Models.Values.Where(m => m.UnitId == unit && m.Location is OnTable).ToList();
        if (on.Count == 0) return null;
        var at = on.Select(m => ((OnTable)m.Location).Placement.Position).ToList();
        var anchor = new Point3(at.Average(q => q.X), at.Average(q => q.Y), on.Max(m => ((OnTable)m.Location).Placement.Position.Z + m.Slot.Height));
        var tiles = DecisionCard.Groups(ask, hits, numbered ?? DecisionView.Number(hits), p, game, new Names(game, p), NoWounds).SelectMany(g => g.Tiles).ToList();
        return new UnitActions(ask.Id, unit, anchor, tiles);
    }
}

/// <summary>Where the buttons' row stands on screen: centred over the point with its bottom on it, inside the view less the margin, and off
/// every covered rectangle (the Hud's panels, grown by the margin): the nearest clear place beside, above or below a panel it would sit under.</summary>
public static class UnitActionsPlace
{
    public static HudRect Rect(float centreX, float bottom, float w, float h, HudRect view, IReadOnlyList<HudRect> covered, float margin)
    {
        HudRect Inside(float x, float y) => new(Math.Clamp(x, view.X + margin, Math.Max(view.X + margin, view.Right - margin - w)),
            Math.Clamp(y, view.Y + margin, Math.Max(view.Y + margin, view.Bottom - margin - h)), w, h);
        var grown = covered.Select(c => new HudRect(c.X - margin, c.Y - margin, c.W + 2 * margin, c.H + 2 * margin)).ToList();
        bool Clear(HudRect r) => !grown.Any(g => g.Overlaps(r));
        var want = Inside(centreX - w / 2, bottom - h);
        if (Clear(want)) return want;
        HudRect? best = null; var bestCost = float.MaxValue;
        foreach (var g in grown.Where(g => g.Overlaps(want)))
            foreach (var c in new[] { Inside(g.X - w, want.Y), Inside(g.Right, want.Y), Inside(want.X, g.Y - h), Inside(want.X, g.Bottom) })
            {
                var cost = Math.Abs(c.X - want.X) + Math.Abs(c.Y - want.Y);
                if (Clear(c) && cost < bestCost) { best = c; bestCost = cost; }
            }
        return best ?? want;
    }
}
