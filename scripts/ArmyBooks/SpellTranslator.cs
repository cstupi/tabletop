using System.Globalization;
using System.Text.RegularExpressions;
using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks;

/// <summary>Army Forge's `generation` → a spell entry, row by row of the spell library spec's translation table.</summary>
public static partial class SpellTranslator
{
    /// <summary>Hand-written entries keyed by Army Forge spell id, used in place of a translation the printed text disagrees with. Empty.</summary>
    public static IReadOnlyDictionary<string, EntryDocument> Overrides { get; } = new Dictionary<string, EntryDocument>();

    [GeneratedRegex(@"(-?\+?\d+)")] private static partial Regex Number();
    [GeneratedRegex("^Pick [^\"]*? within (\\d+)\"")] private static partial Regex PrintedRangeRx();

    /// <summary>The effect id Army Forge uses for "loses N spell tokens", which the effect grammar has no kind for: the spell is written text-only.</summary>
    const string TokenLossEffect = "Urj6dU6Tua8B";

    /// <summary>The range the printed text states ("Pick one enemy unit within 18" …"), or null when it states none.</summary>
    public static int? PrintedRange(AfSpell spell) => PrintedRangeRx().Match(spell.Effect) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;

    /// <summary>The range the generation names and the printed text states, when they differ: the printed text is the authority.</summary>
    public static (int Generation, int Printed)? RangeCorrection(AfSpell spell)
    {
        var generation = Int(spell.Generation.Assignment.Text("inRange"), spell.Name.Trim());
        return PrintedRange(spell) is { } printed && printed != generation ? (generation, printed) : null;
    }

    /// <summary>True when the translation falls back to a text-only spell: an effect the grammar has no kind for, or an attack type the structured field leaves empty or unknown.</summary>
    public static bool IsTextOnly(AfSpell spell)
    {
        var e = spell.Generation.Effect;
        return e.Id == TokenLossEffect || (e.Parameters.Any(p => p.Name == "attackType") && e.Text("attackType") is not ("when shooting" or "in melee" or "when attacking"));
    }

    public static EntryDocument Translate(AfBook book, AfSpell spell)
    {
        if (Overrides.TryGetValue(spell.Id, out var given)) return given;
        var a = spell.Generation.Assignment; var e = spell.Generation.Effect;
        var where = $"{book.Name}: {spell.Name.Trim()}";
        var targeting = new SpellTargeting(
            a.Text("friendly-enemy") switch { "friendly" => TargetSide.Friendly, "enemy" => TargetSide.Enemy, var x => throw Bad(where, $"side '{x}'") },
            a.Text("target") switch { "unit" => TargetGrain.Unit, "model" => TargetGrain.Model, var x => throw Bad(where, $"target '{x}'") },
            a.Text("numberText") switch { "one" => 1, "up to two" => 2, "up to three" => 3, var x => throw Bad(where, $"count '{x}'") },
            PrintedRange(spell) ?? Int(a.Text("inRange"), where),
            a.Text("inLoS") is { Length: > 0 } ? true : null);   // 3.6: the spell's own "in line of sight"; 3.5 dumps carry none, so 3.5.1 stays null
        if (IsTextOnly(spell)) return new EntryDocument(spell.Name.Trim(), CatalogKind.Spell, false, null, spell.Threshold, null, spell.Effect, targeting, [], false);
        IReadOnlyList<EffectDocument> rows = e.Id switch
        {
            "QFjxWsOHTCSi" => [Damage(spell, e, where)],
            "RWnJrrMKAUbg" => [Grant(e.Rule("unitRule"), EffectSubject.Self, [], where)],
            "1yTPcanTyckX" => [Grant(e.Rule("unitRule"), EffectSubject.Opponent, [], where)],
            "54xO9EKTj-ix" => [Grant(e.Rule("weaponRule"), EffectSubject.Opponent, Attack(e.Text("attackType"), where), where)],
            "i1BVqztsr1V6" => [e.Text("gets/loses/ignores") switch
                {
                    "loses" => Loses(e.Rule("weaponRule"), Attack(e.Text("attackType"), where), where),
                    "gets" => Grant(e.Rule("weaponRule"), EffectSubject.Self, Attack(e.Text("attackType"), where), where),
                    var x => throw Bad(where, $"gets/loses/ignores '{x}'"),
                }],
            "4xqVFHJrq78d" => [RollMod("casting", Int(e.Text("rollModifier"), where), EffectSubject.Self, [])],
            "txtrnRX2fPJJ" => [RollMod("defense", Int(e.Text("rollModifier"), where), EffectSubject.Self, [])],
            "nxIQS1UnUBL_" => [RollMod("morale", Int(e.Text("rollModifier"), where), EffectSubject.Self, [])],
            "gqflE2vy38aQ" => [RollMod("hit", Int(e.Text("rollModifier"), where), EffectSubject.Self, Attack(e.Text("attackType"), where))],
            "YbOvC9oIIli9" => [RollMod("hit", Int(e.Text("rollModifier"), where), EffectSubject.Opponent, Attack(e.Text("attackType"), where))],
            "rz0b1dShgnvg" => [Speed("Advance", Int(e.Text("advanceSpeed"), where), 10), Speed("Rush", Int(e.Text("rushSpeed"), where), 20), Speed("Charge", Int(e.Text("rushSpeed"), where), 30)],
            "7w-FrWUjKqqG" => [Range(Int(e.Text("rangeModifier"), where), EffectSubject.Self)],
            "LixCCuyRq4-_" => [Range(Int(e.Text("rangeModifier"), where), EffectSubject.Opponent)],
            "_m0wja4blbg8" => [Row(EffectKind.CountAsTerrain, EffectSubject.Self, EffectDuration.Once) with { TargetTerrain = e.Text("terrainType") switch
                { "Difficult Terrain" => TargetTerrain.Difficult, "Dangerous Terrain" => TargetTerrain.Dangerous, var x => throw Bad(where, $"terrain '{x}'") } }],
            "ZcZbsyKjR5yN" => [Row(EffectKind.TakeTest, EffectSubject.Self, EffectDuration.Interaction) with { TargetRoll = "morale",
                Children = [Row(EffectKind.SetCondition, EffectSubject.Self, EffectDuration.Interaction, 20) with { TargetCondition = "fatigued" }] }],
            "q9OJ_WVwJw5w" => [Row(EffectKind.TakeTest, EffectSubject.Self, EffectDuration.Interaction) with { TargetRoll = "morale",
                Children = [Row(EffectKind.Move, EffectSubject.Self, EffectDuration.Interaction, 20) with { Amount = Int(e.Text("moraleMove"), where) }] }],
            var id => throw Bad(where, $"effect id '{id}' is not in the translation table"),
        };
        return new EntryDocument(spell.Name.Trim(), CatalogKind.Spell, false, null, spell.Threshold, null, spell.Effect, targeting, rows.ToList());
    }

    static EffectDocument Row(EffectKind kind, EffectSubject subject, EffectDuration duration, int ordinal = 10) =>
        new(subject, ordinal, kind, null, null, null, null, null, null, null, null, false, null, null, null, duration, [], []);

    const string RollSentence = "Roll as many dice as hits";

    /// <summary>Row 1 and 2: hits are numberDigit × the spell's value (all 141 damage spells); weapon rules are grant children; the roll sentence (the generation's or the printed text's) sets target_roll.</summary>
    static EffectDocument Damage(AfSpell spell, AfGenPart e, string where)
    {
        if (e.Text("hits-wounds") != "hits") throw Bad(where, $"hits-wounds '{e.Text("hits-wounds")}'");
        var children = new[] { e.Rule("weaponRule1"), e.Rule("weaponRule2") }.OfType<(string Name, int? Rating)>()
            .Select((r, i) => Row(EffectKind.GrantProperty, EffectSubject.Self, EffectDuration.Interaction, 20 + 10 * i) with { TargetEntry = r.Name, Amount = r.Rating }).ToList();
        return Row(EffectKind.AddHit, EffectSubject.Self, EffectDuration.Interaction) with
        {
            Amount = Int(e.Text("numberDigit"), where) * spell.Threshold,
            // The 3.6 dump's generation can leave spellHitRoll out where the printed text has the sentence: the printed text is the authority.
            TargetRoll = !string.IsNullOrEmpty(e.Text("spellHitRoll")) || spell.Effect.Contains(RollSentence, StringComparison.Ordinal) ? "hit" : null,
            Children = children,
        };
    }

    static EffectDocument Grant((string Name, int? Rating)? rule, EffectSubject subject, IReadOnlyList<ConditionDocument> when, string where) =>
        rule is { } r ? Row(EffectKind.GrantProperty, subject, EffectDuration.Once) with { TargetEntry = r.Name, Amount = r.Rating, Conditions = when } : throw Bad(where, "no rule named");

    static EffectDocument Loses((string Name, int? Rating)? rule, IReadOnlyList<ConditionDocument> when, string where) =>
        rule is { } r ? Row(EffectKind.ModifyParameter, EffectSubject.Self, EffectDuration.Once) with { TargetEntry = r.Name, Amount = -(r.Rating ?? 1), Conditions = when } : throw Bad(where, "no rule named");

    static EffectDocument RollMod(string roll, int amount, EffectSubject subject, IReadOnlyList<ConditionDocument> when) =>
        Row(EffectKind.ModifyRoll, subject, EffectDuration.Once) with { TargetRoll = roll, Amount = amount, Conditions = when };

    static EffectDocument Speed(string action, int amount, int ordinal) => Row(EffectKind.ModifyStat, EffectSubject.Self, EffectDuration.Once, ordinal) with { TargetAction = action, Amount = amount };

    static EffectDocument Range(int amount, EffectSubject subject) => Row(EffectKind.ModifyStat, subject, EffectDuration.Once) with
        { TargetStat = new StatRef(StatLevel.Weapon, "Range"), Amount = amount, Conditions = [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["shooting"])] };

    static IReadOnlyList<ConditionDocument> Attack(string? phrase, string where) => phrase switch
    {
        "when shooting" => [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["shooting"])],
        "in melee" => [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, ["melee"])],
        "when attacking" => [new ConditionDocument(ConditionKind.AttackKind, null, Comparison.Eq, null, Sorted("shooting", "melee"))],
        _ => throw Bad(where, $"attack type '{phrase}'"),
    };

    /// <summary>The store reads a condition's values `order by value`, so a multi-value list is written in ordinal order.</summary>
    static IReadOnlyList<string> Sorted(params string[] values) => values.Order(StringComparer.Ordinal).ToList();

    static int Int(string? text, string where) =>
        text is not null && Number().Match(text) is { Success: true } m ? int.Parse(m.Value.Replace("+", ""), CultureInfo.InvariantCulture) : throw Bad(where, $"no number in '{text}'");

    static GenerationException Bad(string where, string what) => new([$"{where}: {what}"]);
}
