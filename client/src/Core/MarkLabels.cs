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
        ("path crosses a model", "Through a model"),
        ("through another model", "Through a model"),
        ("through impassable terrain", "Through impassable terrain"),
        ("in impassable terrain", "In impassable terrain"),
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

/// <summary>
/// Where the labels over the table stand on the screen: a model's labels stacked downward Gap (Space.XS) apart, centred over its anchor with
/// the stack's foot on it; each stack placed as one block by RingLabels.Place, so no two labels overlap, none leaves the view and none sits on the
/// Hud's panels. In the input order.
/// </summary>
public static class MarkLabels
{
    public const float Gap = 4;

    public static IReadOnlyList<HudRect> Place(IReadOnlyList<MarkLabel> labels, HudRect view, IReadOnlyList<HudRect>? covered = null)
    {
        var models = labels.Select(l => l.ModelId).Distinct().ToList();
        var blocks = models.Select(id =>
        {
            var mine = labels.Where(l => l.ModelId == id).ToList();
            float w = mine.Max(l => l.W), h = mine.Sum(l => l.H) + Gap * (mine.Count - 1);
            var at = mine[0].Anchor;
            return new HudRect(at.X - w / 2, at.Y - h, w, h);
        }).ToList();
        var placed = RingLabels.Place(blocks, view, covered);
        var result = new HudRect[labels.Count];
        for (var b = 0; b < models.Count; b++)
        {
            var block = placed[b]; var y = block.Y; var cx = block.X + block.W / 2;
            for (var i = 0; i < labels.Count; i++)
            {
                if (labels[i].ModelId != models[b]) continue;
                result[i] = new HudRect(cx - labels[i].W / 2, y, labels[i].W, labels[i].H);
                y += labels[i].H + Gap;
            }
        }
        return result;
    }
}
