using System.Text.Json;

namespace Tabletop.Management.Content.Armies;

// The subset of Army Forge's TTS export this project reads. Field names are the export's.
// Only `name`, `type` and `count` are present on every loadout entry, and `rating` is absent (not null) on a rule that takes no
// parameter, so every optional member is nullable and nothing is `required`. A required-field model throws on the real export.
public sealed record AfList(string Id, string Name, string GameSystem, List<AfUnit> Units);
public sealed record AfUnit(string SelectionId, string Name, int Size, JsonElement Quality, JsonElement Defense,
    List<AfRule>? Rules, List<AfLoadoutItem>? Loadout, AfBases? Bases, bool Combined, string? JoinToUnit, string? ArmyId = null);
public sealed record AfRule(string Name, JsonElement? Rating);
public sealed record AfLoadoutItem(string Name, string Type, int Count, JsonElement? Range, int? Attacks, List<AfRule>? SpecialRules, List<AfRule>? Content);
public sealed record AfBases(string? Round, string? Square);
