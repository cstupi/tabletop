using System.Text.Json;

namespace Tabletop.ArmyBooks;

// The subset of Army Forge's army-book API this tool reads. Field names are the API's. A parameter's value is a string or an object
// ({id, name, rating?}), so it stays a JsonElement until the translator reads it.
public sealed record AfIndexEntry(string Uid, string Name, string? VersionString);
public sealed record AfBook(string Uid, string Name, string? VersionString, List<AfSpell> Spells);
public sealed record AfSpell(string Id, string Name, int Threshold, string Effect, AfGeneration Generation);
public sealed record AfGeneration(AfGenPart Effect, AfGenPart Assignment);
public sealed record AfGenPart(string Id, List<AfParam> Parameters)
{
    public string? Text(string name) => Parameters.FirstOrDefault(p => p.Name == name)?.Value is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    public (string Name, int? Rating)? Rule(string name) =>
        Parameters.FirstOrDefault(p => p.Name == name)?.Value is { ValueKind: JsonValueKind.Object } v
            ? (v.GetProperty("name").GetString()!, v.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : null) : null;
}
public sealed record AfParam(string Name, JsonElement Value);

public static class ArmyForgeJson
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static AfBook ReadBook(string json) => JsonSerializer.Deserialize<AfBook>(json, Options) ?? throw new JsonException("empty book");
    public static List<AfIndexEntry> ReadIndex(string json) => JsonSerializer.Deserialize<List<AfIndexEntry>>(json, Options) ?? [];
}

/// <summary>Every problem one generate run found, each naming its book and spell. The run writes nothing.</summary>
public sealed class GenerationException(IReadOnlyList<string> problems) : Exception(string.Join("\n", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
