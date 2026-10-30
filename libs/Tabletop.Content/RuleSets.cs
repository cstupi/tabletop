namespace Tabletop.Content;

/// <summary>The game-rules ids that ship, one per Grimdark Future edition (rule-set versions spec → Editions are game-rules ids), and the army edition each reads.</summary>
public static class RuleSets
{
    public const string Gf351 = "grimdark-future";
    public const string Gf360 = "grimdark-future-3.6";
    /// <summary>The rule set a new game starts on.</summary>
    public const string Default = Gf360;
    public static readonly IReadOnlyList<string> All = [Gf351, Gf360];

    /// <summary>The army edition ("3.5" | "3.6") a game on this rule set takes.</summary>
    public static string EditionOf(string rulesId) => rulesId switch
    {
        Gf351 => ArmyEditions.V35,
        Gf360 => ArmyEditions.V36,
        _ => throw new ArgumentException($"No rule set named '{rulesId}'.", nameof(rulesId)),
    };

    /// <summary>The rule set of an army edition ("3.5" | "3.6").</summary>
    public static string RulesIdOf(string edition) => edition switch
    {
        ArmyEditions.V35 => Gf351,
        ArmyEditions.V36 => Gf360,
        _ => throw new ArgumentException($"No rule set for edition '{edition}'.", nameof(edition)),
    };
}
