using System.Globalization;
using Tabletop.Content;

namespace Tabletop.Management.Content.Armies;

/// <summary>
/// What an import adds to a model for an army-book rule that makes an attack no weapon of the list carries (army-book rules spec → Readings
/// of the batches). The added weapon is named after the rule, so the engine lists the rule when it fires.
/// Takedown Shot: "Once per game, when this model shoots, it may make X extra attacks against the target at Quality 2+ with AP(2), Deadly(3),
/// and Takedown." Takedown Strike: "Once per game, when it's this model's turn to strike in melee, it may make X attacks at Quality 2+ with
/// AP(2), Deadly(3), and Takedown." (3.6; 3.5.1's unrated rules say "one", and an unrated rule is one attack.) The weapon's attacks are X,
/// the ratings the model carries added (p.13: rules with (X) stack). Quality 2+ is p.14's Reliable, once per game p.14's Limited. The shot reaches as far as the model's longest
/// gun ("against the target" it shoots), and a model with no gun never shoots, so it gets none.
/// </summary>
public static class ArmyBookAdditions
{
    public const string TakedownShot = "Takedown Shot", TakedownStrike = "Takedown Strike";

    static List<PropertyInstance> Takedown() => [new("AP", 2), new("Deadly", 3), new("Reliable", null), new("Takedown", null), new("Limited", null)];

    /// <summary>The weapons a model carrying these rules gets, after its own.</summary>
    public static IReadOnlyList<Weapon> Weapons(IEnumerable<PropertyInstance> carried, IReadOnlyList<Weapon> own)
    {
        var all = carried.ToList();
        var names = all.Select(p => p.Name).ToHashSet();
        string Attacks(string rule) => Math.Max(1, all.Where(p => p.Name == rule).Sum(p => p.Parameter ?? 0)).ToString(CultureInfo.InvariantCulture);
        var added = new List<Weapon>();
        var range = own.Select(w => w.Stats.TryGetValue("Range", out var r) && double.TryParse(r.TrimEnd('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0).DefaultIfEmpty(0).Max();
        if (names.Contains(TakedownShot) && range > 0)
            added.Add(new Weapon(Guid.CreateVersion7(), TakedownShot, new Dictionary<string, string> { ["Attacks"] = Attacks(TakedownShot), ["Range"] = $"{range.ToString(CultureInfo.InvariantCulture)}\"" }, Takedown()));
        if (names.Contains(TakedownStrike))
            added.Add(new Weapon(Guid.CreateVersion7(), TakedownStrike, new Dictionary<string, string> { ["Attacks"] = Attacks(TakedownStrike) }, Takedown()));
        return added;
    }
}
