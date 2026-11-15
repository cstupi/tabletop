using System.Text;

namespace Tabletop.Client.Core;

/// <summary>How the log panel draws one paragraph: struck (undone), its colour (`#rrggbbaa`, faded or a team's) and its tint (picked), each optional.</summary>
public sealed record ParagraphLook(bool Struck = false, string? Colour = null, string? Tint = null);

/// <summary>
/// LogSheet's paragraphs as one RichTextLabel text, a line each: every non-empty line a link to its paragraph (`[url=i]`, which a click picks),
/// struck, coloured and tinted by its look, `[` written as `[lb]`. The tags draw only: the label's text without them, and so what a selection
/// of it copies, is LogSheet.Text. LineOf: each paragraph's first line, for a scroll to it.
/// </summary>
public static class LogBbcode
{
    public static string Of(IReadOnlyList<LogParagraph> paragraphs, Func<LogParagraph, ParagraphLook> look, out IReadOnlyList<int> lineOf)
    {
        var starts = new List<int>(); var sb = new StringBuilder(); var line = 0;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var p = paragraphs[i]; var l = look(p); starts.Add(line);
            foreach (var text in p.Indented)
            {
                if (line++ > 0) sb.Append('\n');
                if (text.Length == 0) continue;
                var words = text.Replace("[", "[lb]");
                if (l.Struck) words = $"[s]{words}[/s]";
                if (l.Colour is { } c) words = $"[color={c}]{words}[/color]";
                if (l.Tint is { } t) words = $"[bgcolor={t}]{words}[/bgcolor]";
                sb.Append($"[url={i}]{words}[/url]");
            }
        }
        lineOf = starts;
        return sb.ToString();
    }

    /// <summary>The text the tags draw: every tag dropped, `[lb]` read as `[`.</summary>
    public static string Plain(string bbcode) =>
        System.Text.RegularExpressions.Regex.Replace(bbcode, @"\[(?:lb|/?(?:s|url|color|bgcolor)(?:=[^\]]*)?)\]", m => m.Value == "[lb]" ? "[" : "");
}
