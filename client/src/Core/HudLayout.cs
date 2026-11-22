namespace Tabletop.Client.Core;

public enum HudPart { Corner, Toasts, Header, Roster, UnitCard, Decision, Dice, LogButton, UndoButton, LogPanel, Banner, EndMessage, Replay, Loading }

public readonly record struct HudRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool SpansX(HudRect o) => X < o.Right && o.X < Right;
    public bool Overlaps(HudRect o) => SpansX(o) && Y < o.Bottom && o.Y < Bottom;
}

/// <summary>The lobby's and setup's frame (spec 2026-11-21-setup-screen-layout-design.md → Layout): the top bar with the corner inside its left,
/// the tool rail and the open tool's panel down the left, the key hint bar at the foot, the table (Body) the rest, and over the table the
/// selected piece's card on the right, the toasts left of it and the loading strip at its foot.</summary>
public enum SetupPart { TopBar, Corner, Rail, Panel, Body, Card, Toasts, Loading, Hints }

/// <summary>Setup shows the rail and the hint bar, the panel while a tool is open; the lobby none of them.</summary>
public sealed record SetupVisible(bool Rail, bool Panel, bool Hints);

/// <summary>Replay: the replay bar takes the left column's foot.</summary>
public sealed record HudVisible(bool Roster, bool UnitCard, bool Decision, bool Dice, bool LogOpen, bool Replay = false);

/// <summary>Spec → HUD frame: an 8 px margin, each part anchored to its edge, no two overlapping from 1280×720 up. Every slot is whole
/// pixels: a box container sums its children's minimums as integers, so a part fitted to a fractional slot never settles.</summary>
public static class HudLayout
{
    public const float M = 8, Column = 420, CornerH = 40, HeaderW = 760, HeaderH = 96, ToastW = 360, ToastH = 120, DecisionW = 480,
        DiceH = 96, ReplayH = 112, LogW = 640, LogMaxH = 720, CornerButton = 38, LoadingH = 36, LoadingW = 280, BannerH = 136, EndW = 480, EndH = 360;

    public static readonly IReadOnlySet<HudPart> Overlays = new HashSet<HudPart> { HudPart.Banner, HudPart.EndMessage };

    /// <summary>The slots. buttonSize is the undo and log buttons' actual size (square icon buttons; a theme's face may be larger), never under
    /// CornerButton: they stand in the top right corner, Log at the corner and Undo left of it, the toasts below them. replayH is the replay
    /// bar's actual height (three rows in the theme's font may be taller than 112 px), never under ReplayH: the left column ends M above it.
    /// headerH is the header's actual height (its lines in the theme's font), never under HeaderH: the right column starts M below it. While
    /// the log is open it holds the latest roll, so the dice tray takes no slot.</summary>
    public static IReadOnlyDictionary<HudPart, HudRect> Of(float w, float h, HudVisible v, float buttonSize = CornerButton, float replayH = ReplayH, float headerH = HeaderH)
    {
        var r = new Dictionary<HudPart, HudRect>();
        v = v with { Dice = v.Dice && !v.LogOpen };
        float bs = MathF.Ceiling(Math.Max(CornerButton, buttonSize)), rh = MathF.Ceiling(Math.Max(ReplayH, replayH));
        var corner = r[HudPart.Corner] = new(M, M, Column, CornerH);
        var log = r[HudPart.LogButton] = new(w - M - bs, M, bs, bs);
        var undo = r[HudPart.UndoButton] = new(log.X - M - bs, M, bs, bs);
        var toasts = r[HudPart.Toasts] = new(w - M - ToastW, log.Bottom + M, ToastW, ToastH);
        float bandL = M + Column + M, bandR = Math.Min(toasts.X, undo.X) - M, hw = Math.Min(HeaderW, bandR - bandL);
        var header = r[HudPart.Header] = new(Math.Max(bandL, Math.Min(MathF.Floor(w / 2 - hw / 2), bandR - hw)), M, hw, MathF.Ceiling(Math.Max(HeaderH, headerH)));
        float top = corner.Bottom + M, colH = h - M - top - (v.Replay ? rh + M : 0), half = MathF.Floor((colH - M) / 2);
        if (v.Replay) r[HudPart.Replay] = new(M, h - M - rh, Column, rh);
        if (v.Roster) r[HudPart.Roster] = new(M, top, Column, v.UnitCard ? half : colH);
        if (v.UnitCard) r[HudPart.UnitCard] = v.Roster ? new(M, top + half + M, Column, half) : new(M, top, Column, colH);
        // The loading strip (models, a reconnect): at the foot's centre, clear of both columns.
        var loading = r[HudPart.Loading] = new(MathF.Floor(w / 2 - LoadingW / 2), h - M - LoadingH, LoadingW, LoadingH);
        // The right column, below the corner buttons, the toasts and the header down to the margin: the decision box flush at its foot,
        // the dice above it, and the open log hanging from its top. With the decision box showing, the log takes the upper half.
        var span = new HudRect(w - M - Math.Max(LogW, DecisionW), 0, Math.Max(LogW, DecisionW), h);
        float colTop = new[] { log, undo, toasts, header }.Where(span.SpansX).Max(o => o.Bottom) + M, colBottom = h - M, rightH = colBottom - colTop;
        float stack = v.Decision ? MathF.Floor((rightH - M) / 2) : v.Dice ? DiceH : 0;
        float stackTop = v.LogOpen && stack > 0 ? colBottom - stack : colTop, floor = colBottom, x = w - M - DecisionW;
        if (v.Decision) { var dh = floor - stackTop - (v.Dice ? DiceH + M : 0); r[HudPart.Decision] = new(x, floor - dh, DecisionW, dh); floor -= dh + M; }
        if (v.Dice) r[HudPart.Dice] = new(x, floor - DiceH, DecisionW, DiceH);
        if (v.LogOpen)
        {
            var logX = w - M - LogW;
            float logBottom = stack > 0 ? stackTop - M : new HudRect(logX, 0, LogW, h).SpansX(loading) ? loading.Y - M : colBottom;   // alone, it ends above the strip it spans
            r[HudPart.LogPanel] = new(logX, colTop, LogW, Math.Min(LogMaxH, logBottom - colTop));
        }
        r[HudPart.Banner] = new(M, MathF.Floor(h / 3 - BannerH / 2), w - 2 * M, BannerH);
        r[HudPart.EndMessage] = new(MathF.Floor(w / 2 - EndW / 2), MathF.Floor(h / 2 - EndH / 2), EndW, EndH);
        return r;
    }

    public const float TopBarH = 56, RailW = 56, PanelW = 360, HintH = 32, SetupCornerW = 360;

    /// <summary>The setup frame's slots. topH and hintH are the bars' actual heights (the theme's font), never under TopBarH and HintH. The
    /// bars span the window edge to edge; the table's box starts where the rail or the panel ends and reaches the right edge and the hint bar.</summary>
    public static IReadOnlyDictionary<SetupPart, HudRect> Setup(float w, float h, SetupVisible v, float topH = TopBarH, float hintH = HintH)
    {
        var r = new Dictionary<SetupPart, HudRect>();
        float top = MathF.Ceiling(Math.Max(TopBarH, topH)), hint = v.Hints ? MathF.Ceiling(Math.Max(HintH, hintH)) : 0, bottom = h - hint, bodyH = bottom - top;
        r[SetupPart.TopBar] = new(0, 0, w, top);
        r[SetupPart.Corner] = new(M, MathF.Floor((top - CornerH) / 2), SetupCornerW, CornerH);
        if (v.Rail) r[SetupPart.Rail] = new(0, top, RailW, bodyH);
        if (v.Panel) r[SetupPart.Panel] = new(RailW, top, PanelW, bodyH);
        float left = v.Panel ? RailW + PanelW : v.Rail ? RailW : 0;
        var body = r[SetupPart.Body] = new(left, top, w - left, bodyH);
        var loading = r[SetupPart.Loading] = new(left + MathF.Floor((body.W - LoadingW) / 2), bottom - M - LoadingH, LoadingW, LoadingH);
        var card = r[SetupPart.Card] = new(w - M - Column, top + M, Column, Math.Max(0, loading.Y - M - (top + M)));
        r[SetupPart.Toasts] = new(Math.Max(left + M, card.X - M - ToastW), top + M, ToastW, ToastH);
        if (v.Hints) r[SetupPart.Hints] = new(0, bottom, w, hint);
        return r;
    }

    /// <summary>A slot started M below another part's actual bottom, down to the slot's own bottom: the unit card below the roster as it is.</summary>
    public static HudRect Below(HudRect slot, float aboveBottom)
    {
        var top = aboveBottom + M;
        return slot with { Y = top, H = Math.Max(0, slot.Bottom - top) };
    }
}
