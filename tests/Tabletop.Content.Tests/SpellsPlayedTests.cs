using System.Text.Json;

namespace Tabletop.Content.Tests;

public class SpellsPlayedTests
{
    /// <summary>
    /// No catalog entry of kind spell in either edition's content (core, army rules, army-book rules, every book) is `played: false`.
    /// Postgres refuses such a row (`catalog_entry_played_check`, migration 008: `played or kind &lt;&gt; 'spell'`) and the game server then
    /// fails at startup; the in-memory fakes never enforced it.
    /// </summary>
    [Fact]
    public void No_spell_entry_in_any_edition_is_not_played()
    {
        var content = Path.Combine(RepoCatalog.Root, "server", "game", "Content");
        var files = Directory.GetDirectories(content, "grimdark-future-*").SelectMany(d => Directory.GetFiles(d, "*.json", SearchOption.AllDirectories)).ToList();
        Assert.NotEmpty(files);
        var unplayed = new List<string>();
        foreach (var file in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            Walk(doc.RootElement, e => unplayed.Add($"{Path.GetRelativePath(content, file)}: {e.GetProperty("name").GetString()}"));
        }
        Assert.Empty(unplayed);
    }

    static void Walk(JsonElement e, Action<JsonElement> found)
    {
        if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Walk(x, found);
        else if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String && k.GetString() == "spell"
                && e.TryGetProperty("played", out var p) && p.ValueKind == JsonValueKind.False && e.TryGetProperty("name", out _)) found(e);
            foreach (var x in e.EnumerateObject()) Walk(x.Value, found);
        }
    }
}
