using System.Text.RegularExpressions;

namespace Tabletop.Client.Core;

/// <summary>
/// The words a mark's label over the table reads: a short noun phrase per reason, from the client's contact words, the move's warnings and the
/// server's refusal sentences alike. The full sentence is read once, in the decision box's gate or refusal line.
/// </summary>
public static partial class MarkWords
{
    /// <summary>The one word-list: a phrase found in the reason (any case) and the label it reads as, first match wins.</summary>
    static readonly (string Found, string Words)[] Phrases =
    [
        (MovePaths.TestWords, MovePaths.TestWords),
        ("out of coherency", "Out of coherency"),
        ("overlaps a base", "On a base"),
        ("on another model's base", "On a base"),
        ("through impassable terrain", "Through impassable terrain"),
        ("in impassable terrain", "In impassable terrain"),
        ("path crosses ", "Through a model"),
        ("through another model", "Through a model"),
        ("would move through ", "Through a model"),
        ("leave the table", "Off the table"),
        ("outside its deployment zone", "Outside its zone"),
    ];

    public static string Short(string reason)
    {
        foreach (var (found, words) in Phrases)
            if (reason.Contains(found, StringComparison.OrdinalIgnoreCase)) return words;
        if (Beyond().Match(reason) is { Success: true } b) return $"Beyond {b.Groups[1].Value}\"";
        if (Within().Match(reason) is { Success: true } w) return $"Too close {w.Groups[1].Value}\"";
        return "Against the rules";
    }

    [GeneratedRegex("beyond ([0-9]+(?:\\.[0-9]+)?)\"")] private static partial Regex Beyond();
    [GeneratedRegex("within ([0-9]+(?:\\.[0-9]+)?)\"")] private static partial Regex Within();
}

/// <summary>One label over the table: its model, where over the model it hangs from on the screen, and its size in pixels.</summary>
public readonly record struct MarkLabel(Guid ModelId, (float X, float Y) Anchor, float W, float H);

/// <summary>A coherency gap's words (`1.4" apart, needs 1"`): the middle of its line on the screen, and its size in pixels.</summary>
public readonly record struct GapLabel((float X, float Y) Anchor, float W, float H);

/// <summary>
/// Where the labels over the table stand on the screen, mark labels and coherency gap labels in one placement: a model's labels stacked downward
/// Gap (Space.XS) apart, centred over its anchor with the stack's foot on it, and each gap label Gap over its line's middle; each stack and each gap
/// label placed as one block by RingLabels.Place, so no two labels overlap, none leaves the view and none sits on the Hud's panels or the bar over
/// the acting unit (UnitActionsNode). In the input order.
/// </summary>
public static class MarkLabels
{
    public const float Gap = 4;

    public static IReadOnlyList<HudRect> Place(IReadOnlyList<MarkLabel> labels, HudRect view, IReadOnlyList<HudRect>? covered = null) =>
        Place(labels, [], view, covered, null).Marks;

    public static (IReadOnlyList<HudRect> Marks, IReadOnlyList<HudRect> Gaps) Place(IReadOnlyList<MarkLabel> labels, IReadOnlyList<GapLabel> gaps,
        HudRect view, IReadOnlyList<HudRect>? covered, HudRect? bar)
    {
        var models = labels.Select(l => l.ModelId).Distinct().ToList();
        var blocks = models.Select(id =>
        {
            var mine = labels.Where(l => l.ModelId == id).ToList();
            float w = mine.Max(l => l.W), h = mine.Sum(l => l.H) + Gap * (mine.Count - 1);
            var at = mine[0].Anchor;
            return new HudRect(at.X - w / 2, at.Y - h, w, h);
        }).ToList();
        blocks.AddRange(gaps.Select(g => new HudRect(g.Anchor.X - g.W / 2, g.Anchor.Y - Gap - g.H, g.W, g.H)));
        var obstacles = bar is { } b ? [.. covered ?? [], b] : covered;
        var placed = RingLabels.Place(blocks, view, obstacles);
        var result = new HudRect[labels.Count];
        for (var m = 0; m < models.Count; m++)
        {
            var block = placed[m]; var y = block.Y; var cx = block.X + block.W / 2;
            for (var i = 0; i < labels.Count; i++)
            {
                if (labels[i].ModelId != models[m]) continue;
                result[i] = new HudRect(cx - labels[i].W / 2, y, labels[i].W, labels[i].H);
                y += labels[i].H + Gap;
            }
        }
        return (result, placed.Skip(models.Count).ToList());
    }
}
