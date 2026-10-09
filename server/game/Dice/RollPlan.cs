using Tabletop.Protocol;

namespace Tabletop.GameServer.Dice;

/// <summary>
/// What GameRules supplies per roll: the die, whether summed modifiers are clamped (OPR never; 40k ±1), and the raw faces that fail or
/// succeed a thresholded roll whatever its modifiers (OPR p.5: 1 and 6 on every roll).
/// </summary>
public sealed record RollConvention(int Sides, int? ModifierCap, int? AlwaysFails = null, int? AlwaysSucceeds = null);

/// <summary>One roll, settled: the threshold GameRules derived, the modifiers the two passes and the players contributed, and the per-die rules.</summary>
public sealed record RollPlan(string Roll, int? Threshold, IReadOnlyList<RollModifier> Modifiers, RollConvention Convention, int? RequiredRaw, IReadOnlySet<int> RerollRaw)
{
    public int NetModifier
    {
        get { var sum = Modifiers.Sum(m => m.Amount); return Convention.ModifierCap is { } cap ? Math.Clamp(sum, -cap, cap) : sum; }
    }
}

public sealed record RollResult(RollRecord Record, DiceRerolled? ForcedRerolls, long NextCursor);
