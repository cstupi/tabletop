namespace Tabletop.Client.Core;

/// <summary>Every panel the play Hud and the lobby/setup frame place. Setup's own: the top bar, the tool rail, the open tool's panel, the
/// table's box (Table), the selected piece's card and the key hint bar.</summary>
public enum HudPart
{
    Corner, Toasts, Header, Roster, UnitCard, Decision, Dice, LogButton, UndoButton, LogPanel, Banner, EndMessage, Replay, Loading,
    TopBar, Rail, ToolPanel, Table, TerrainCard, Hints,
}

public readonly record struct HudRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool SpansX(HudRect o) => X < o.Right && o.X < Right;
    public bool Overlaps(HudRect o) => SpansX(o) && Y < o.Bottom && o.Y < Bottom;
}

/// <summary>Where a part stands across its container: against its left or right edge (or beside its Beside part), centred in the band
/// earlier parts on its rows leave free, or edge to edge.</summary>
public enum HudX { Left, Right, Centre, Stretch }

/// <summary>Where a part stands down its container: Top (Gap below every earlier Top part spanning its columns), Bottom (Gap above every
/// earlier Bottom part spanning them), centred, or centred on the upper third.</summary>
public enum HudY { Top, Bottom, Middle, Third }

public enum HudSizeKind { Fixed, AtLeast, Fill }

/// <summary>Fixed(n); AtLeast(n): the part's measured size, never under n; Fill(max): the room left, up to max.</summary>
public readonly record struct HudSize(HudSizeKind Kind, float N)
{
    public static HudSize Fixed(float n) => new(HudSizeKind.Fixed, n);
    public static HudSize AtLeast(float n) => new(HudSizeKind.AtLeast, n);
    public static HudSize Fill(float max = float.PositiveInfinity) => new(HudSizeKind.Fill, max);
}

/// <summary>One part's row: its anchor (X, Y), its size rule (W, H) and its neighbours. On is the stacking neighbour: a Fill part splits
/// its room evenly with the later Fill parts On it, and a fixed part On it is carved out of its share. Beside: stands beside that part
/// (left of it when right-anchored) and takes its anchor, so it moves with it; a hidden neighbour passes on to its own Beside. In: placed inside that part's rect instead of the
/// window. HiddenWhile: takes no slot while that part shows (Covered). Keeps: the edge it keeps when it measures short. Overlay: comes and goes over the others (no other part keeps off
/// it, no overlay avoids it). Room: the table's box, not a panel. Always: takes its slot whether it shows or not.</summary>
public sealed record HudRow(HudPart Part, HudX X, HudY Y, HudSize W, HudSize H)
{
    public HudPart? Beside { get; init; }
    public HudPart? On { get; init; }
    public HudPart? In { get; init; }
    public HudPart? HiddenWhile { get; init; }
    /// <summary>The edge a part that measures shorter than its slot keeps: its Y anchor's, unless set (the unit card hangs from its slot's foot).</summary>
    public HudY? Keeps { get; init; }
    public float Gap { get; init; } = HudFrame.M;
    public bool Overlay { get; init; }
    public bool Room { get; init; }
    public bool Always { get; init; }
}

/// <summary>Fits the part to its slot and returns its measured size, or null to take the slot as it is.</summary>
public delegate (float W, float H)? HudFit(HudPart part, HudRect slot);

/// <summary>
/// The one answer to "where is everything" (spec 2026-11-21-one-hud-frame-design.md): every part's slot and measured rect for a window
/// size, the parts that show and their measured sizes, from one table of rows (Play, Setup). Obstacles are the shown panels as they stand
/// (measured), which every overlay keeps off. Every slot is whole pixels: a box container sums its children's minimums as integers, so a
/// part fitted to a fractional slot never settles.
/// </summary>
public sealed class HudFrame
{
    public const float M = 8, Column = 420, CornerH = 40, HeaderW = 760, HeaderH = 96, ToastW = 360, ToastH = 120, DecisionW = 480,
        DiceH = 96, ReplayH = 112, LogW = 640, LogMaxH = 720, CornerButton = 38, LoadingH = 36, LoadingW = 280, BannerH = 136, EndW = 480, EndH = 360,
        TopBarH = 56, RailW = 56, PanelW = 360, HintH = 32, SetupCornerW = 360,
        CardW = 480;   // the unit card, wider than the roster over it, so its weapons and rules wrap less and it stands lower

    /// <summary>Play (spec 2026-11-07-ux-redesign-2-play-hud.md → HUD frame): the corner top left, Log in the top right corner with Undo left of
    /// it and the toasts below them, the header centred between, the left column (roster over the unit card, the replay bar at its foot),
    /// the right column (the open log hanging from its top, the decision box flush at its foot with the dice tray on it), the loading strip
    /// at the foot's centre, the banner and the end message over everything.</summary>
    public static readonly IReadOnlyList<HudRow> Play =
    [
        new(HudPart.Corner, HudX.Left, HudY.Top, HudSize.Fixed(Column), HudSize.Fixed(CornerH)),
        new(HudPart.LogButton, HudX.Right, HudY.Top, HudSize.AtLeast(CornerButton), HudSize.AtLeast(CornerButton)) { Always = true },
        new(HudPart.UndoButton, HudX.Right, HudY.Top, HudSize.AtLeast(CornerButton), HudSize.AtLeast(CornerButton)) { Beside = HudPart.LogButton, Always = true },
        new(HudPart.Toasts, HudX.Right, HudY.Top, HudSize.Fixed(ToastW), HudSize.Fixed(ToastH)) { Overlay = true, Always = true },
        new(HudPart.Header, HudX.Centre, HudY.Top, HudSize.Fill(HeaderW), HudSize.AtLeast(HeaderH)) { Always = true },
        new(HudPart.Replay, HudX.Left, HudY.Bottom, HudSize.Fixed(Column), HudSize.AtLeast(ReplayH)),
        new(HudPart.Roster, HudX.Left, HudY.Top, HudSize.Fixed(Column), HudSize.Fill()),
        new(HudPart.UnitCard, HudX.Left, HudY.Top, HudSize.Fixed(CardW), HudSize.Fill()) { On = HudPart.Roster, Keeps = HudY.Bottom },
        new(HudPart.Loading, HudX.Centre, HudY.Bottom, HudSize.Fixed(LoadingW), HudSize.Fixed(LoadingH)) { Always = true },
        new(HudPart.Decision, HudX.Right, HudY.Bottom, HudSize.Fixed(DecisionW), HudSize.Fill()),
        new(HudPart.Dice, HudX.Right, HudY.Bottom, HudSize.Fixed(DecisionW), HudSize.Fixed(DiceH)) { On = HudPart.Decision, HiddenWhile = HudPart.LogPanel },
        new(HudPart.LogPanel, HudX.Right, HudY.Top, HudSize.Fixed(LogW), HudSize.Fill(LogMaxH)) { On = HudPart.Decision },
        new(HudPart.Banner, HudX.Stretch, HudY.Third, HudSize.Fill(), HudSize.Fixed(BannerH)) { Overlay = true, Always = true },
        new(HudPart.EndMessage, HudX.Centre, HudY.Middle, HudSize.Fixed(EndW), HudSize.Fixed(EndH)) { Overlay = true, Always = true },
    ];

    /// <summary>Lobby and setup (spec 2026-11-21-setup-screen-layout-design.md → Layout): the top bar edge to edge with the corner centred in
    /// its left end, the key hint bar at the foot, the tool rail and the open tool's panel down the left, the table's box the rest, and in
    /// it the loading strip at its foot, the selected piece's card on the right and the toasts left of the card.</summary>
    public static readonly IReadOnlyList<HudRow> Setup =
    [
        new(HudPart.TopBar, HudX.Stretch, HudY.Top, HudSize.Fill(), HudSize.AtLeast(TopBarH)) { Gap = 0, Always = true },
        new(HudPart.Hints, HudX.Stretch, HudY.Bottom, HudSize.Fill(), HudSize.AtLeast(HintH)) { Gap = 0 },
        new(HudPart.Corner, HudX.Left, HudY.Middle, HudSize.Fixed(SetupCornerW), HudSize.Fixed(CornerH)) { In = HudPart.TopBar, Always = true },
        new(HudPart.Rail, HudX.Left, HudY.Top, HudSize.Fixed(RailW), HudSize.Fill()) { Gap = 0 },
        new(HudPart.ToolPanel, HudX.Left, HudY.Top, HudSize.Fixed(PanelW), HudSize.Fill()) { Beside = HudPart.Rail, Gap = 0 },
        new(HudPart.Table, HudX.Left, HudY.Top, HudSize.Fill(), HudSize.Fill()) { Beside = HudPart.ToolPanel, Gap = 0, Room = true, Always = true },
        new(HudPart.Loading, HudX.Centre, HudY.Bottom, HudSize.Fixed(LoadingW), HudSize.Fixed(LoadingH)) { In = HudPart.Table, Always = true },
        new(HudPart.TerrainCard, HudX.Right, HudY.Top, HudSize.Fixed(Column), HudSize.Fill()) { In = HudPart.Table, Keeps = HudY.Bottom, Always = true },
        new(HudPart.Toasts, HudX.Right, HudY.Top, HudSize.Fixed(ToastW), HudSize.Fixed(ToastH)) { In = HudPart.Table, Beside = HudPart.TerrainCard, Overlay = true, Always = true },
    ];

    public static readonly HudFrame Empty = new(new Dictionary<HudPart, HudRect>(), new Dictionary<HudPart, HudRect>(), [], [], new HashSet<HudPart>());

    readonly IReadOnlySet<HudPart> _covered;

    HudFrame(IReadOnlyDictionary<HudPart, HudRect> slots, IReadOnlyDictionary<HudPart, HudRect> rects, IReadOnlyList<HudPart> order,
        IReadOnlyList<HudRect> obstacles, IReadOnlySet<HudPart> covered)
    {
        Slots = slots; Rects = rects; Order = order; Obstacles = obstacles; _covered = covered;
    }

    /// <summary>Each placed part's slot: the room its row gives it.</summary>
    public IReadOnlyDictionary<HudPart, HudRect> Slots { get; }

    /// <summary>Each placed part as it stands: its slot, ended at its measured edge where it measures shorter.</summary>
    public IReadOnlyDictionary<HudPart, HudRect> Rects { get; }

    /// <summary>The placed parts in the order they stack (the table's order: a later part stands on the earlier ones).</summary>
    public IReadOnlyList<HudPart> Order { get; }

    /// <summary>The shown panels as they stand, in the window's pixels: every overlay over the table keeps off them. Overlay rows (the
    /// banner, the end message, the toasts) and the table's box are left out.</summary>
    public IReadOnlyList<HudRect> Obstacles { get; }

    /// <summary>The part gives its slot up while its HiddenWhile part shows (the open log holds the latest roll: the dice tray).</summary>
    public bool Covered(HudPart part) => _covered.Contains(part);

    public HudRect this[HudPart part] => Rects[part];
    public bool Has(HudPart part) => Rects.ContainsKey(part);

    /// <summary>A frame equal to this one: same rects and obstacles (a layout pass that moved nothing raises nothing).</summary>
    public bool SameAs(HudFrame other) =>
        Rects.Count == other.Rects.Count && Rects.All(kv => other.Rects.TryGetValue(kv.Key, out var o) && o == kv.Value)
        && Obstacles.SequenceEqual(other.Obstacles) && _covered.SetEquals(other._covered);

    public static HudFrame Of(float w, float h, IReadOnlySet<HudPart> shown, HudFit? fit = null, IReadOnlyList<HudRow>? rows = null)
    {
        rows ??= Play;
        var window = new HudRect(0, 0, w, h);
        var covered = rows.Where(r => r.HiddenWhile is { } p && shown.Contains(p)).Select(r => r.Part).ToHashSet();
        var visible = rows.Where(r => (r.Always || shown.Contains(r.Part)) && !covered.Contains(r.Part)).ToList();
        var slots = new Dictionary<HudPart, HudRect>(); var rects = new Dictionary<HudPart, HudRect>(); var placed = new List<HudRow>();
        var byPart = rows.ToDictionary(r => r.Part);

        for (var i = 0; i < visible.Count; i++)
        {
            var row = visible[i]; var g = row.Gap;
            // A part beside another takes its neighbour's anchor and stands on its far side: moving the neighbour moves it too.
            HudRect? beside = null;
            for (var b = row.Beside; b is { } bp; b = byPart.TryGetValue(bp, out var br) ? br.Beside : null)
                if (rects.TryGetValue(bp, out var at)) { beside = at; row = row with { X = byPart[bp].X, Y = byPart[bp].Y }; break; }
            var c = row.In is { } inside && rects.TryGetValue(inside, out var cr) ? cr : window;
            var peers = placed.Where(p => p.In == row.In && !p.Room).Select(p => (Row: p, Rect: rects[p.Part])).ToList();
            var solid = peers.Where(p => !p.Row.Overlay).ToList();

            // Across.
            float? measuredW = null, measuredH = null;
            float width = row.W.Kind == HudSizeKind.Fill ? 0 : row.W.N, x;
            if (row.W.Kind == HudSizeKind.AtLeast || row.H.Kind == HudSizeKind.AtLeast)
            {
                // A part sized by its content is measured first, in the room its minimum gives it.
                var probe = fit?.Invoke(row.Part, new HudRect(c.X + g, c.Y + g, row.W.Kind == HudSizeKind.Fill ? Across(row, c, g, beside, peers).W : width, row.H.Kind == HudSizeKind.Fill ? 0 : row.H.N));
                if (probe is { } m) { measuredW = m.W; measuredH = m.H; }
                if (row.W.Kind == HudSizeKind.AtLeast) width = MathF.Ceiling(Math.Max(row.W.N, measuredW ?? 0));
            }
            (x, width) = row.W.Kind == HudSizeKind.Fill || row.X is HudX.Centre or HudX.Stretch ? Across(row, c, g, beside, peers, width) : (Side(row, c, g, beside, width), width);
            var cols = new HudRect(x, 0, width, 1);

            // Down.
            float top = Math.Max(c.Y + g, solid.Where(p => p.Row.Y == HudY.Top && p.Rect.SpansX(cols)).Select(p => p.Rect.Bottom + g).DefaultIfEmpty(c.Y + g).Max());
            float bottom = Math.Min(c.Bottom - g, solid.Where(p => p.Row.Y == HudY.Bottom && p.Rect.SpansX(cols)).Select(p => p.Rect.Y - g).DefaultIfEmpty(c.Bottom - g).Min());
            float height = row.H.Kind switch
            {
                HudSizeKind.Fixed => row.H.N,
                HudSizeKind.AtLeast => MathF.Ceiling(Math.Max(row.H.N, measuredH ?? 0)),
                _ => Share(row, visible.Skip(i + 1).Where(r => r.In == row.In).ToList(), bottom - top, g),
            };
            float y = row.Y switch
            {
                HudY.Top => top,
                HudY.Bottom => bottom - height,
                HudY.Middle => MathF.Floor(c.Y + c.H / 2 - height / 2),
                _ => MathF.Floor(c.Y + c.H / 3 - height / 2),
            };
            var slot = new HudRect(x, y, width, height);
            slots[row.Part] = slot;

            // A part that measures shorter than its slot ends at its measured edge, against its anchor.
            var rect = slot;
            if (row.H.Kind != HudSizeKind.AtLeast && fit?.Invoke(row.Part, slot) is { } fitted && fitted.H < slot.H)
            {
                var mh = Math.Max(0, MathF.Ceiling(fitted.H));
                rect = (row.Keeps ?? row.Y) switch
                {
                    HudY.Bottom => slot with { Y = slot.Bottom - mh, H = mh },
                    HudY.Middle => slot with { Y = MathF.Floor(c.Y + c.H / 2 - mh / 2), H = mh },
                    _ => slot with { H = mh },
                };
            }
            rects[row.Part] = rect; placed.Add(row);   // as placed: a part beside another stands on its anchor
        }
        var obstacles = placed.Where(r => !r.Overlay && !r.Room && shown.Contains(r.Part)).Select(r => rects[r.Part]).ToList();
        return new HudFrame(slots, rects, placed.Select(r => r.Part).ToList(), obstacles, covered);
    }

    /// <summary>A fixed-width part against its side: the container's edge less the gap, or the gap beside its neighbour; never past the
    /// container's other edge.</summary>
    static float Side(HudRow row, HudRect c, float g, HudRect? beside, float width) => row.X == HudX.Left
        ? beside is { } b ? b.Right + g : c.X + g
        : Math.Max(c.X + g, (beside is { } b2 ? b2.X - g : c.Right - g) - width);

    /// <summary>A Fill-width, centred or stretched part across its container. Centred: on the container's centre, kept inside the band
    /// the earlier parts on its rows leave free (an overlay keeps only to the container).</summary>
    static (float X, float W) Across(HudRow row, HudRect c, float g, HudRect? beside, IReadOnlyList<(HudRow Row, HudRect Rect)> peers, float width = 0)
    {
        float max = row.W.Kind == HudSizeKind.Fill ? row.W.N : width;
        switch (row.X)
        {
            case HudX.Stretch: return (c.X + g, Math.Min(max, c.W - 2 * g));
            case HudX.Left: { var x = beside is { } b ? b.Right + g : c.X + g; return (x, Math.Min(max, c.Right - g - x)); }
            case HudX.Right: { var r = beside is { } b ? b.X - g : c.Right - g; var w = Math.Min(max, r - (c.X + g)); return (r - w, w); }
        }
        float minH = row.H.Kind == HudSizeKind.Fill ? 0 : row.H.N;
        var rows = row.Y switch
        {
            HudY.Top => (c.Y + g, c.Y + g + minH),
            HudY.Bottom => (c.Bottom - g - minH, c.Bottom - g),
            HudY.Middle => (c.Y + c.H / 2 - minH / 2, c.Y + c.H / 2 + minH / 2),
            _ => (c.Y + c.H / 3 - minH / 2, c.Y + c.H / 3 + minH / 2),
        };
        float centre = c.X + c.W / 2, left = c.X + g, right = c.Right - g;
        if (!row.Overlay)
            foreach (var (_, p) in peers)
            {
                if (!(p.Y < rows.Item2 && rows.Item1 < p.Bottom)) continue;
                if (p.X + p.W / 2 < centre) left = Math.Max(left, p.Right + g); else right = Math.Min(right, p.X - g);
            }
        var cw = Math.Min(max, right - left);
        return (Math.Max(left, Math.Min(MathF.Floor(centre - cw / 2), right - cw)), cw);
    }

    /// <summary>A Fill part's height: its room, split evenly with the later Fill parts On it (the first share, floored, is its), less the
    /// fixed parts On it, up to its max.</summary>
    static float Share(HudRow row, IReadOnlyList<HudRow> later, float room, float g)
    {
        var on = later.Where(r => r.On == row.Part).ToList();
        int k = on.Count(r => r.H.Kind == HudSizeKind.Fill);
        float carved = on.Where(r => r.H.Kind != HudSizeKind.Fill).Sum(r => r.H.N + g);
        var share = k > 0 ? MathF.Floor((room - k * g) / (k + 1)) : room;
        return Math.Max(0, Math.Min(row.H.N, share - carved));
    }
}
