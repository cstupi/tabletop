namespace Tabletop.Client.Core;

public enum HudPart { Corner, Toasts, Header, Roster, UnitCard, Decision, Dice, LogButton, UndoButton, LogPanel, Banner, EndMessage, Replay, Loading }

public readonly record struct HudRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool SpansX(HudRect o) => X < o.Right && o.X < Right;
    public bool Overlaps(HudRect o) => SpansX(o) && Y < o.Bottom && o.Y < Bottom;
}

/// <summary>Replay: the replay bar takes the left column's foot.</summary>
public sealed record HudVisible(bool Roster, bool UnitCard, bool Decision, bool Dice, bool LogOpen, bool Replay = false);

/// <summary>Spec → HUD frame: an 8 px margin, each part anchored to its edge, no two overlapping from 1280×720 up. Every slot is whole
/// pixels: a box container sums its children's minimums as integers, so a part fitted to a fractional slot never settles.</summary>
public static class HudLayout
{
    public const float M = 8, Column = 420, CornerH = 40, HeaderW = 760, HeaderH = 96, ToastW = 360, ToastH = 120, DecisionW = 480,
        DiceH = 96, ReplayH = 112, LogW = 640, LogMaxH = 720, LogButtonW = 140, LogButtonH = 36, UndoButtonW = 140, LoadingW = 280, BannerH = 136, EndW = 480, EndH = 360;

    public static readonly IReadOnlySet<HudPart> Overlays = new HashSet<HudPart> { HudPart.Banner, HudPart.EndMessage };

    /// <summary>The slots. buttonH is the undo and log buttons' actual height (a theme's button face may be taller than 36 px), never under LogButtonH.
    /// replayH is the replay bar's actual height (three rows in the theme's font may be taller than 112 px), never under ReplayH: the left
    /// column ends M above it. While the log is open it holds the latest roll, so the dice tray takes no slot.</summary>
    public static IReadOnlyDictionary<HudPart, HudRect> Of(float w, float h, HudVisible v, float buttonH = LogButtonH, float replayH = ReplayH)
    {
        var r = new Dictionary<HudPart, HudRect>();
        v = v with { Dice = v.Dice && !v.LogOpen };
        float bh = MathF.Ceiling(Math.Max(LogButtonH, buttonH)), rh = MathF.Ceiling(Math.Max(ReplayH, replayH));
        var corner = r[HudPart.Corner] = new(M, M, Column, CornerH);
        var toasts = r[HudPart.Toasts] = new(w - M - ToastW, M, ToastW, ToastH);
        float bandL = M + Column + M, bandR = toasts.X - M, hw = Math.Min(HeaderW, bandR - bandL);
        var header = r[HudPart.Header] = new(Math.Max(bandL, Math.Min(MathF.Floor(w / 2 - hw / 2), bandR - hw)), M, hw, HeaderH);
        float top = corner.Bottom + M, colH = h - M - top - (v.Replay ? rh + M : 0), half = MathF.Floor((colH - M) / 2);
        if (v.Replay) r[HudPart.Replay] = new(M, h - M - rh, Column, rh);
        if (v.Roster) r[HudPart.Roster] = new(M, top, Column, v.UnitCard ? half : colH);
        if (v.UnitCard) r[HudPart.UnitCard] = v.Roster ? new(M, top + half + M, Column, half) : new(M, top, Column, colH);
        var button = r[HudPart.LogButton] = new(w - M - LogButtonW, h - M - bh, LogButtonW, bh);
        r[HudPart.UndoButton] = new(button.X - M - UndoButtonW, button.Y, UndoButtonW, bh);
        // The loading strip (models, a reconnect): the button row's height at the foot's centre, clear of both columns.
        r[HudPart.Loading] = new(MathF.Floor(w / 2 - LoadingW / 2), button.Y, LoadingW, bh);
        // The right column, below the toasts and the header down to the buttons: the decision box at its foot, the dice above it,
        // and the open log over them. With the decision box showing, the log takes the upper half.
        var span = new HudRect(w - M - Math.Max(LogW, DecisionW), 0, Math.Max(LogW, DecisionW), h);
        float colTop = new[] { toasts, header }.Where(span.SpansX).Max(o => o.Bottom) + M, colBottom = button.Y - M, rightH = colBottom - colTop;
        float stack = v.Decision ? MathF.Floor((rightH - M) / 2) : v.Dice ? DiceH : 0;
        float stackTop = v.LogOpen && stack > 0 ? colBottom - stack : colTop, floor = colBottom, x = w - M - DecisionW;
        if (v.Decision) { var dh = floor - stackTop - (v.Dice ? DiceH + M : 0); r[HudPart.Decision] = new(x, floor - dh, DecisionW, dh); floor -= dh + M; }
        if (v.Dice) r[HudPart.Dice] = new(x, floor - DiceH, DecisionW, DiceH);
        if (v.LogOpen)
        {
            float logBottom = stack > 0 ? stackTop - M : colBottom, lh = Math.Min(LogMaxH, logBottom - colTop);
            r[HudPart.LogPanel] = new(w - M - LogW, logBottom - lh, LogW, lh);
        }
        r[HudPart.Banner] = new(M, MathF.Floor(h / 3 - BannerH / 2), w - 2 * M, BannerH);
        r[HudPart.EndMessage] = new(MathF.Floor(w / 2 - EndW / 2), MathF.Floor(h / 2 - EndH / 2), EndW, EndH);
        return r;
    }

    /// <summary>A slot started M below another part's actual bottom, down to the slot's own bottom: the unit card below the roster as it is.</summary>
    public static HudRect Below(HudRect slot, float aboveBottom)
    {
        var top = aboveBottom + M;
        return slot with { Y = top, H = Math.Max(0, slot.Bottom - top) };
    }
}
