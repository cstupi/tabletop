using System.Text.RegularExpressions;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

public partial class Core36TextTests
{
    /// <summary>
    /// Every 3.6 core special rule named by a heading on pp. 13–15 of the skill's v3.6.0 pages carries that page's text.
    /// A rule's text is the lines after its heading up to the next heading, joined by spaces; a paragraph that starts
    /// with "Note" is a paragraph of the entry (<c>\n</c> between paragraphs). Quotes and apostrophes are normalised.
    /// Every "Example:" paragraph is omitted from the entries (Blast, Furious, Hero, Takedown, Transport and the rest
    /// that have one): an example is not a rule.
    /// </summary>
    [Fact]
    public void Every_3_6_core_entry_text_is_the_3_6_0_books()
    {
        var pages = Pages();
        var mismatches = new List<string>();
        var checkedCount = 0;
        foreach (var entry in TestProperties.CoreDocument36.Entries)
        {
            if (!pages.TryGetValue(entry.Name, out var expected)) continue;
            checkedCount++;
            var actual = Normalise(entry.RuleText);
            if (actual != expected) mismatches.Add($"{entry.Name}\n  entry: {actual}\n  page:  {expected}");
        }
        Assert.True(checkedCount >= 35, $"only {checkedCount} entries matched a heading");
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    static string Normalise(string text) =>
        string.Join('\n', text.Split('\n').Select(p => Spaces().Replace(p.Replace('’', '\'').Replace('‘', '\'').Replace('”', '"').Replace('“', '"'), " ").Trim()));

    /// <summary>Rule name (without "(X)") to the normalised text the page holds, Examples left out.</summary>
    static Dictionary<string, string> Pages()
    {
        var dir = Path.GetDirectoryName(typeof(Core36TextTests).Assembly.Location)!;
        while (dir != null && !Directory.Exists(Path.Combine(dir, ".claude/skills/licensed/skills/grimdark-future-rules/v3.6.0/pages"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var pagesDir = Path.Combine(dir, ".claude/skills/licensed/skills/grimdark-future-rules/v3.6.0/pages");
        var lines = new[] { "p13.md", "p14.md", "p15.md" }.SelectMany(f => File.ReadAllLines(Path.Combine(pagesDir, f))).Where(l => !l.StartsWith("# ")).ToList();
        var result = new Dictionary<string, string>();
        string? name = null;
        var paragraphs = new List<string>();
        void Flush()
        {
            if (name != null) result[name] = string.Join('\n', paragraphs.Where(p => !p.StartsWith("Example:")).Select(Normalise));
        }
        foreach (var line in lines)
        {
            if (Heading().IsMatch(line))
            {
                Flush();
                name = line.Replace("(X)", "").Trim();
                paragraphs = [];
            }
            else if (name != null)
            {
                if (paragraphs.Count == 0 || line.StartsWith("Note") || line.StartsWith("Example:")) paragraphs.Add(line);
                else paragraphs[^1] += " " + line;
            }
        }
        Flush();
        return result;
    }

    [GeneratedRegex(@"^[A-Z][A-Za-z]*( [A-Z][a-z]*)?(\(X\))?$")] private static partial Regex Heading();
    [GeneratedRegex(@" {2,}")] private static partial Regex Spaces();
}
