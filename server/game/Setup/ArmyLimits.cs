using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.GameServer.Setup;

/// <summary>
/// What an army may hold to be picked and snapshotted (content-path review). An army reaches the game server as the account saved it,
/// and its snapshot is read by every step of the game: a base no geometry can build, a stat no reader parses or a parameter in the
/// millions would fault or stall play. The bounds are far outside anything Army Forge lists (Grimdark Future's largest bases are under
/// 6", Quality and Defense run 2+ to 6+; a parameter is held to 100 rather than 50 so a titan-class Tough is never refused).
/// </summary>
public sealed record ArmyLimits(int Units = 150, int ModelsPerUnit = 50, int Models = 1000, int WeaponsPerModel = 30, int Properties = 60, int Stats = 20, int Name = 100)
{
    public static ArmyLimits Default { get; } = new();
    public const double MinBase = 0.4, MaxBase = 12, MinHeight = 0.1, MaxHeight = 24;
    public const int MinScore = 2, MaxScore = 6, MinAttacks = 1, MaxAttacks = 100, MaxParameter = 100;

    /// <summary>The first thing over a bound, named for the player, or null.</summary>
    public string? Breach(IReadOnlyList<Unit> units)
    {
        if (units.Count > Units) return $"more than {Units} units";
        if (units.Sum(u => u.Models.Count) > Models) return $"more than {Models} models";
        foreach (var u in units)
        {
            var at = $"unit '{Clip(u.Name)}'";
            if (u.Name.Length > Name) return $"{at}: the name is over {Name} characters";
            if (u.Models.Count is 0 || u.Models.Count > ModelsPerUnit) return $"{at}: 1–{ModelsPerUnit} models";
            if (Holder(u.Stats, u.Properties) is { } bad) return $"{at}: {bad}";
            foreach (var m in u.Models)
            {
                var model = $"{at}, model '{Clip(m.Name)}'";
                if (m.Name.Length > Name) return $"{model}: the name is over {Name} characters";
                if (!Enum.IsDefined(m.Base.Shape) || !(m.Base.A >= MinBase && m.Base.A <= MaxBase && m.Base.B >= MinBase && m.Base.B <= MaxBase))
                    return $"{model}: a base is {MinBase}–{MaxBase}\" on each side";
                if (!(m.Height >= MinHeight && m.Height <= MaxHeight)) return $"{model}: a height is {MinHeight}–{MaxHeight}\"";
                if (Holder(m.Stats, m.Properties) is { } slot) return $"{model}: {slot}";
                if (m.Weapons.Count > WeaponsPerModel) return $"{model}: more than {WeaponsPerModel} weapons";
                foreach (var w in m.Weapons)
                {
                    if (w.Name.Length > Name) return $"{model}: a weapon name is over {Name} characters";
                    if (Holder(w.Stats, w.Properties) is { } weapon) return $"{model}, weapon '{Clip(w.Name)}': {weapon}";
                }
            }
        }
        return null;
    }

    string? Holder(IReadOnlyDictionary<string, string> stats, IReadOnlyList<PropertyInstance> properties)
    {
        if (stats.Count > Stats || stats.Any(kv => kv.Key.Length > Name || kv.Value.Length > Name)) return "too many or too long stats";
        foreach (var (name, text) in stats)
        {
            var ok = name switch
            {
                "Quality" or "Defense" => Read(StatKind.TargetNumber, text) is { } s && s.TargetNumber is >= MinScore and <= MaxScore,
                "Attacks" => Read(StatKind.Count, text) is { } a && a.Count is >= MinAttacks and <= MaxAttacks,
                "Range" => Read(StatKind.Distance, text) is { } r && r.Inches <= Table.MaxEdge,
                _ => true,
            };
            if (!ok) return $"{name} '{Clip(text)}' is out of range";
        }
        if (properties.Count > Properties) return $"more than {Properties} rules";
        if (properties.FirstOrDefault(p => p.Name.Length > Name || p.Parameter is < 0 or > MaxParameter) is { } bad) return $"rule '{Clip(bad.Name)}' has a parameter outside 0–{MaxParameter}";
        return null;
    }

    static StatValue? Read(StatKind kind, string text) { try { return StatValue.Read(kind, text); } catch (FormatException) { return null; } }
    static string Clip(string s) => s.Length <= 40 ? s : s[..40] + "…";
}
