using Tabletop.Protocol;

namespace Tabletop.GameServer.Dice;

/// <summary>
/// What GameRules supplies per roll: the die, whether summed modifiers are clamped (OPR never; 40k ±1), and the raw faces that fail or
/// succeed a thresholded roll whatever its modifiers.
/// </summary>
public sealed record RollConvention(int Sides, int? ModifierCap, int? AlwaysFails = null, int? AlwaysSucceeds = null);

/// <summary>One roll, settled: the threshold GameRules derived, the modifiers the two passes and the players contributed, and the per-die rules.
/// RerollRaw faces are re-rolled whatever the die scored; RerollSuccessfulRaw faces only when the die succeeded ("re-roll successful unmodified results").</summary>
public sealed record RollPlan(string Roll, int? Threshold, IReadOnlyList<RollModifier> Modifiers, RollConvention Convention, int? RequiredRaw, IReadOnlySet<int> RerollRaw,
    IReadOnlySet<int>? RerollSuccessfulRaw = null, ThresholdBasis? Basis = null, CoverVerdict? Cover = null)
{
    public bool Rerolls(Die die) => RerollRaw.Contains(die.Raw) || (die.Success && RerollSuccessfulRaw?.Contains(die.Raw) == true);

    public int NetModifier
    {
        get { var sum = Modifiers.Sum(m => m.Amount); return Convention.ModifierCap is { } cap ? Math.Clamp(sum, -cap, cap) : sum; }
    }
}

public sealed record RollResult(RollRecord Record, DiceRerolled? ForcedRerolls, long NextCursor);
