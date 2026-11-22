using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>The bar over the acting unit (spec 2026-11-21-actions-over-unit-design.md): the Ask it answers, the unit, the point above its
/// tallest model at its models' centroid, the decision box's tiles for the Ask's options in the box's order, the card's foot actions with their
/// numbers (CardActions), and while I pick shooting targets the enemy units I may target, each with its point.</summary>
public sealed record UnitActions(Guid AskId, Guid UnitId, Point3 Anchor, IReadOnlyList<OptionTile> Options, IReadOnlyList<CardAction> Actions,
    IReadOnlyList<UnitTarget> Targets)
{
    /// <summary>A button's tooltip: the option's word and its key cap ("Advance  2"); the word alone with no key.</summary>
    public static string Tip(OptionTile t) => t.Key > 0 ? $"{t.Word}  {DecisionCard.KeyCap(t.Key)}" : t.Word;
}

/// <summary>A unit I may shoot, and the point above its tallest model where its target icon stands.</summary>
public sealed record UnitTarget(Guid UnitId, Point3 Anchor);

public static class UnitActionsView
{
    static readonly IReadOnlyDictionary<Guid, int> NoWounds = new Dictionary<Guid, int>();

    /// <summary>
    /// The bar for my open Ask, over its acting unit: for an activation (activate, act_or_end_phase) the selected unit, with its options;
    /// for any other Ask the unit of mine acting now (the action frame's), else the selected unit of mine the Ask names, with all the Ask's
    /// options. Then the card's foot actions, numbered after the tiles. Null while watching or replaying, for another team's Ask, with no such
    /// unit or one with no model on the table. The keys are the box's numbering when given (numbered), else the box's own (an activation's
    /// narrowed to the unit's options).
    /// </summary>
    public static UnitActions? Of(Projection p, GameInfo game, Guid? myTeam, Guid? selected, bool watching,
        IReadOnlyList<(int Key, string Label, string OptionId)>? numbered = null)
    {
        if (watching || AskViewModel.Mine(p, game, myTeam) is not { } mine) return null;
        var ask = mine.Ask;
        bool Mine(Guid u) => p.Units.ContainsKey(u) && SheetFocus.TeamOf(p, game, u)?.Id == myTeam;
        Guid unit; IReadOnlyList<AskOption> shown;
        if (DecisionCard.Activations.Contains(ask.Question))
        {
            if (selected is not { } s || !p.Units.ContainsKey(s) || ask.Legal is not Options o) return null;
            shown = DecisionView.ChoicesOf(o, p, s);
            if (shown.Count == 0) return null;
            unit = s;
            numbered ??= DecisionView.Number(shown);
        }
        else
        {
            bool Named(Guid u) => mine.Highlights.Any(id => id == u || p.Models.GetValueOrDefault(id)?.UnitId == u);
            if (HeaderView.ActionFrame(p)?.Context.ActorUnitId is { } actor && Mine(actor)) unit = actor;
            else if (selected is { } s && Mine(s) && Named(s)) unit = s;
            else return null;
            shown = ask.Legal is Options o ? o.Choices : [];
            numbered ??= DecisionView.Of(p, game, new Names(game, p), myTeam).Numbered;
        }
        if (Anchor(p, unit) is not { } anchor) return null;
        var tiles = DecisionCard.Groups(ask, shown, numbered, p, game, new Names(game, p), NoWounds).SelectMany(g => g.Tiles).ToList();
        var actions = CardActions.Numbered(CardActions.Of(ask, mine.Mode, p), numbered.Count);
        var targets = ask.Legal is Assignment a ? TargetsOf(p, new TargetAssignment(a).Candidates) : [];
        return new UnitActions(ask.Id, unit, anchor, tiles, actions, targets);
    }

    /// <summary>The point above a unit's tallest model at its models' centroid; null with none on the table.</summary>
    public static Point3? Anchor(Projection p, Guid unit)
    {
        var on = p.Models.Values.Where(m => m.UnitId == unit && m.Location is OnTable).ToList();
        if (on.Count == 0) return null;
        var at = on.Select(m => ((OnTable)m.Location).Placement.Position).ToList();
        return new Point3(at.Average(q => q.X), at.Average(q => q.Y), on.Max(m => ((OnTable)m.Location).Placement.Position.Z + m.Slot.Height));
    }

    /// <summary>The target icons: each unit I may shoot now (TargetAssignment.Candidates) with a model on the table, at its point.</summary>
    public static IReadOnlyList<UnitTarget> TargetsOf(Projection p, IEnumerable<Guid> candidates) =>
        candidates.Select(u => Anchor(p, u) is { } at ? new UnitTarget(u, at) : null).OfType<UnitTarget>().ToList();
}

/// <summary>The unit's name label over it on screen (HoverLabels): its bottom edge and its height. The row keeps above it whether it shows or
/// not, so a button that brings the label up never moves out from under the pointer.</summary>
public readonly record struct UnitLabel(float Bottom, float Height)
{
    public float Top => Bottom - Height;
}

/// <summary>Where the buttons' row stands on screen: centred over the point, its bottom the margin above the models' top or the name label's
/// top, whichever is higher; inside the view less the margin, and off every covered rectangle (the Hud's panels, grown by the margin): the
/// nearest clear place beside, above or below a panel it would sit under.</summary>
public static class UnitActionsPlace
{
    public static HudRect Rect(float centreX, float top, float w, float h, HudRect view, IReadOnlyList<HudRect> covered, float margin, UnitLabel? label = null)
    {
        var bottom = Math.Min(top, label?.Top ?? top) - margin;
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

/// <summary>Where the bar stands from frame to frame: it holds still while held (the pointer over it, a drag or a press in progress), so a
/// click aimed at it after a drag lands where it was aimed, and otherwise eases to where UnitActionsPlace wants it, never jumping. The first
/// place after it was hidden, or for another unit, is taken at once.</summary>
public sealed class UnitActionsHold
{
    /// <summary>The ease's rate per second: the gap closes by 1 - e^(-Rate·dt) a frame, nine tenths of it in about 0.19 s.</summary>
    public const float Rate = 12;
    HudRect? _at; Guid _unit;

    public HudRect Step(Guid unit, HudRect want, bool held, float dt)
    {
        if (_at is not { } at || unit != _unit) { _unit = unit; return (_at = want).Value; }
        if (held) return (_at = at with { W = want.W, H = want.H }).Value;
        var k = 1 - MathF.Exp(-Rate * Math.Max(0, dt));
        float Ease(float from, float to) => Math.Abs(to - from) < 0.5f ? to : from + (to - from) * k;
        return (_at = want with { X = Ease(at.X, want.X), Y = Ease(at.Y, want.Y) }).Value;
    }

    /// <summary>The bar was hidden: its next place is taken at once.</summary>
    public void Reset() => _at = null;
}
