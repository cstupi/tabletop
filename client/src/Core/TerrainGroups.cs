using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>A group of the setup terrain list: its title and its pieces, sorted by name.</summary>
public sealed record TerrainGroup(string Title, IReadOnlyList<BuiltinTerrain> Entries);

/// <summary>
/// The setup panel's terrain list (the browser's <c>terrainGroups.ts</c>): the catalog searched by name and grouped by what a piece does
/// on the table, in the order a table is balanced: sight blockers, then impassable, dangerous, cover, difficult, then the rest.
/// </summary>
public static class TerrainGroups
{
    public const string Open = "Open ground";

    /// <summary>A piece's group: the first of these declarations it carries, else <see cref="Open"/>.</summary>
    static readonly (string Title, TerrainDeclarations Any)[] Groups =
    [
        ("Blocks line of sight", TerrainDeclarations.BlocksLineOfSight),
        ("Impassable", TerrainDeclarations.Impassable),
        ("Dangerous", TerrainDeclarations.Dangerous | TerrainDeclarations.DangerousOnRushCharge),
        ("Cover", TerrainDeclarations.Cover),
        ("Difficult", TerrainDeclarations.Difficult | TerrainDeclarations.Steep),
    ];

    /// <summary>The browser's declaration words (<c>DECLARATION_WORDS</c>), in its order.</summary>
    static readonly (TerrainDeclarations Flag, string Text)[] Words =
    [
        (TerrainDeclarations.Cover, "Cover"), (TerrainDeclarations.Difficult, "Difficult"), (TerrainDeclarations.Dangerous, "Dangerous"),
        (TerrainDeclarations.DangerousOnRushCharge, "Dangerous when rushing or charging"), (TerrainDeclarations.Steep, "Steep"), (TerrainDeclarations.Impassable, "Impassable"),
        (TerrainDeclarations.BlocksLineOfSight, "Blocks line of sight"), (TerrainDeclarations.SeeInAndOut, "See in and out"),
    ];

    /// <summary>The piece's declarations in words: the server's (Session.TerrainWords) where given, else the list's own.</summary>
    static IEnumerable<string> WordsOf(BuiltinTerrain entry, IReadOnlyDictionary<string, string>? given) =>
        Words.Where(w => entry.Volume.Declarations.HasFlag(w.Flag)).Select(w => TerrainLabel.WordOf(w.Flag, w.Text, given));

    /// <summary>What the piece does, in words; "Open" with no declaration.</summary>
    public static string Summary(BuiltinTerrain entry, IReadOnlyDictionary<string, string>? given = null) =>
        WordsOf(entry, given).ToList() is { Count: > 0 } words ? string.Join(", ", words) : "Open";

    /// <summary>True when every word of the query starts a word of the piece's name or of what it does, ignoring case.</summary>
    public static bool Matches(BuiltinTerrain entry, string query, IReadOnlyDictionary<string, string>? given = null)
    {
        var wanted = query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var words = $"{entry.Label} {string.Join(' ', WordsOf(entry, given))}".ToLowerInvariant()
            .Split(c => !char.IsAsciiLetterOrDigit(c)).Where(w => w.Length > 0).ToList();
        return wanted.All(q => words.Any(w => w.StartsWith(q, StringComparison.Ordinal)));
    }

    /// <summary>The catalog's pieces matching the query, grouped and sorted by name; empty groups are left out. A group of one declaration is titled
    /// with the server's word for it where given.</summary>
    public static IReadOnlyList<TerrainGroup> Of(IEnumerable<BuiltinTerrain> catalog, string query = "", IReadOnlyDictionary<string, string>? given = null)
    {
        var titles = Groups.Select(g => g.Title).Append(Open).ToList();
        var shown = Groups.ToDictionary(g => g.Title, g => TerrainLabel.WordOf(g.Any, g.Title, given));
        var byTitle = titles.ToDictionary(t => t, _ => new List<BuiltinTerrain>());
        foreach (var entry in catalog)
        {
            if (!Matches(entry, query, given)) continue;
            var title = Groups.FirstOrDefault(g => (entry.Volume.Declarations & g.Any) != 0).Title ?? Open;
            byTitle[title].Add(entry);
        }
        return titles.Where(t => byTitle[t].Count > 0)
            .Select(t => new TerrainGroup(shown.GetValueOrDefault(t, t), byTitle[t].OrderBy(e => e.Label, StringComparer.CurrentCulture).ToList())).ToList();
    }

    static string[] Split(this string text, Func<char, bool> separator)
    {
        var parts = new List<string>(); var start = 0;
        for (var i = 0; i <= text.Length; i++)
            if (i == text.Length || separator(text[i])) { parts.Add(text[start..i]); start = i + 1; }
        return [.. parts];
    }
}
