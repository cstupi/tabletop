namespace Tabletop.Client.Core;

public enum HudPart { Corner, Toasts, Header, Roster, UnitCard, Decision, Dice, LogButton, LogPanel, Banner, EndMessage }

public readonly record struct HudRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool SpansX(HudRect o) => X < o.Right && o.X < Right;
    public bool Overlaps(HudRect o) => SpansX(o) && Y < o.Bottom && o.Y < Bottom;
}

public sealed record HudVisible(bool Roster, bool UnitCard, bool Decision, bool Dice, bool LogOpen);

/// <summary>Spec → HUD frame: an 8 px margin, each part anchored to its edge, no two overlapping from 1280×720 up.</summary>
public static class HudLayout
{
    public const float M = 8, Column = 420, CornerH = 40, HeaderW = 760, HeaderH = 96, ToastW = 360, ToastH = 120, DecisionW = 480,
        DecisionH = 200, DiceH = 96, LogW = 640, LogMaxH = 720, LogButtonW = 140, LogButtonH = 36, BannerH = 96, EndW = 480, EndH = 360;

    public static readonly IReadOnlySet<HudPart> Overlays = new HashSet<HudPart> { HudPart.Banner, HudPart.EndMessage };

    public static IReadOnlyDictionary<HudPart, HudRect> Of(float w, float h, HudVisible v)
    {
        var r = new Dictionary<HudPart, HudRect>();
        var corner = r[HudPart.Corner] = new(M, M, Column, CornerH);
        var toasts = r[HudPart.Toasts] = new(w - M - ToastW, M, ToastW, ToastH);
        float bandL = M + Column + M, bandR = toasts.X - M, hw = Math.Min(HeaderW, bandR - bandL);
        var header = r[HudPart.Header] = new(Math.Clamp(w / 2 - hw / 2, bandL, bandR - hw), M, hw, HeaderH);
        float top = corner.Bottom + M, colH = h - M - top, half = (colH - M) / 2;
        if (v.Roster) r[HudPart.Roster] = new(M, top, Column, v.UnitCard ? half : colH);
        if (v.UnitCard) r[HudPart.UnitCard] = v.Roster ? new(M, top + half + M, Column, half) : new(M, top, Column, colH);
        var button = r[HudPart.LogButton] = new(w - M - LogButtonW, h - M - LogButtonH, LogButtonW, LogButtonH);
        float left = v.Roster || v.UnitCard ? bandL : M, x = Math.Clamp(w / 2 - DecisionW / 2, left, button.X - M - DecisionW), floor = h - M;
        if (v.Decision) { r[HudPart.Decision] = new(x, floor - DecisionH, DecisionW, DecisionH); floor -= DecisionH + M; }
        if (v.Dice) { r[HudPart.Dice] = new(x, floor - DiceH, DecisionW, DiceH); floor -= DiceH + M; }
        if (v.LogOpen)
        {
            var span = new HudRect(w - M - LogW, 0, LogW, h);
            float logTop = new[] { toasts, header }.Where(span.SpansX).Max(o => o.Bottom) + M;
            float logBottom = (v.Decision || v.Dice) && span.SpansX(new HudRect(x, 0, DecisionW, h)) ? floor : button.Y - M;
            var lh = Math.Min(LogMaxH, logBottom - logTop);
            r[HudPart.LogPanel] = new(span.X, logBottom - lh, LogW, lh);
        }
        r[HudPart.Banner] = new(M, h / 3 - BannerH / 2, w - 2 * M, BannerH);
        r[HudPart.EndMessage] = new(w / 2 - EndW / 2, h / 2 - EndH / 2, EndW, EndH);
        return r;
    }
}
