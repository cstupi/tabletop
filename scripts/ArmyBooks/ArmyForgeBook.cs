using System.Text.Json;

namespace Tabletop.ArmyBooks;

// The subset of Army Forge's army-book API this tool reads. Field names are the API's. A parameter's value is a string or an object
// ({id, name, rating?}), so it stays a JsonElement until the translator reads it.
public sealed record AfIndexEntry(string Uid, string Name, string? VersionString);
public sealed record AfBook(string Uid, string Name, string? VersionString, List<AfSpell> Spells,
    List<AfRuleText>? SpecialRules = null, List<AfUnit>? Units = null, List<AfPackage>? UpgradePackages = null);
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

/// <summary>A book's rule glossary entry: the name and Army Forge's description (army-book rules spec → Generator).</summary>
public sealed record AfRuleText(string Name, string? Description);
public sealed record AfRuleRef(string Name);
public sealed record AfCommonRules(List<AfRuleText>? Rules);
public sealed record AfWeapon(List<AfRuleRef>? SpecialRules);
/// <summary>An upgrade gain or an item's content: an ArmyBookRule (Name), an ArmyBookWeapon (SpecialRules) or an ArmyBookItem (Content).</summary>
public sealed record AfGain(string? Name, string? Type, List<AfRuleRef>? SpecialRules, List<AfGain>? Content);
public sealed record AfUnit(List<AfRuleRef>? Rules, List<AfWeapon>? Weapons, List<AfGain>? Items);
public sealed record AfOption(List<AfGain>? Gains);
public sealed record AfSection(List<AfOption>? Options);
public sealed record AfPackage(List<AfSection>? Sections);

public static class ArmyForgeJson
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static AfBook ReadBook(string json) => JsonSerializer.Deserialize<AfBook>(json, Options) ?? throw new JsonException("empty book");
    /// <summary>Army Forge's common-rules reply (<c>/api/rules/common/&lt;gameSystemId&gt;</c>): each rule's trimmed description by name.</summary>
    public static Dictionary<string, string> ReadCommonRules(string json) =>
        (JsonSerializer.Deserialize<AfCommonRules>(json, Options)?.Rules ?? []).ToDictionary(r => r.Name, r => (r.Description ?? "").Trim(), StringComparer.Ordinal);
    public static List<AfIndexEntry> ReadIndex(string json) => JsonSerializer.Deserialize<List<AfIndexEntry>>(json, Options) ?? [];

    /// <summary>The edition (major.minor) every versioned entry of the index shares; an entry without a version is tolerated while another fixes the edition.</summary>
    public static string EditionOf(IReadOnlyList<AfIndexEntry> index)
    {
        var editions = index.Where(e => !string.IsNullOrWhiteSpace(e.VersionString))
            .Select(e => string.Join('.', e.VersionString!.Split('.').Take(2))).Distinct().Order().ToList();
        if (editions.Count == 1) return editions[0];
        var versions = string.Join(", ", index.Select(e => e.VersionString ?? "none").Distinct().Order());
        throw new InvalidDataException(editions.Count == 0 ? $"the index carries no version ({versions})" : $"the index mixes editions: {versions}");
    }
}

/// <summary>Every problem one generate run found, each naming its book and spell. The run writes nothing.</summary>
public sealed class GenerationException(IReadOnlyList<string> problems) : Exception(string.Join("\n", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
