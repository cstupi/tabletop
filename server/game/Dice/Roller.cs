using Tabletop.Protocol;

namespace Tabletop.GameServer.Dice;

/// <summary>Draws from the cursor, judges each die under the plan, and records everything. Summing is generic; clamping is the convention's.</summary>
public sealed class Roller(IDiceSource dice, ulong seed, long cursor)
{
    public long Cursor { get; private set; } = cursor;

    /// <summary>A required raw face rules alone; otherwise a thresholded roll fails on the convention's AlwaysFails face, succeeds on its AlwaysSucceeds face, and else compares the modified result.</summary>
    public Die Judge(RollPlan plan, int raw)
    {
        var modified = raw + plan.NetModifier;
        var success = plan.RequiredRaw is { } r ? raw >= r
            : plan.Threshold is { } t && raw != plan.Convention.AlwaysFails && (raw == plan.Convention.AlwaysSucceeds || modified >= t);
        return new Die(raw, modified, success);
    }

    public int Draw(RollPlan plan) => dice.Draw(seed, Cursor++, plan.Convention.Sides);

    /// <summary>The most dice one roll draws. The largest pool the rulebook builds (a big unit's attacks, Blast and extra-hit rules
    /// included) is a few hundred; a larger count comes from a menu edit or a defect, and is clamped with a warning.</summary>
    public const int MaxDice = 1000;
    /// <summary>Where a clamp is reported; Program sets it to the host's logger. The engine has no DI of its own.</summary>
    public static ILogger Log { get; set; } = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public RollResult Roll(Guid frameId, RollPlan plan, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > MaxDice) { Log.LogWarning("A {Roll} roll of {Count} dice was clamped to {Max}", plan.Roll, count, MaxDice); count = MaxDice; }
        var rolled = Enumerable.Range(0, count).Select(_ => Judge(plan, Draw(plan))).ToList();
        var record = new RollRecord(Guid.CreateVersion7(), plan.Roll, plan.Threshold, plan.Modifiers, rolled, plan.RequiredRaw, plan.Basis, plan.Cover, Ignored: plan.Ignored, HitsTriggerNothing: plan.HitsTriggerNothing ? true : null);
        var replaced = new List<DieReplacement>();
        for (var i = 0; i < rolled.Count; i++)
            if (plan.Rerolls(rolled[i])) replaced.Add(new DieReplacement(i, rolled[i], Judge(plan, Draw(plan))));   // once, never again
        return new RollResult(record, replaced.Count == 0 ? null : new DiceRerolled(frameId, record.Id, replaced), Cursor);
    }
}

/// <summary>Reading a record back. A record's dice are what stands after every reroll and edit the applier copied in.</summary>
public static class RollOutcome
{
    public static int Successes(RollRecord r) => r.Dice.Count(d => d.Success);
    public static int Failures(RollRecord r) => r.Dice.Count(d => !d.Success);
    public static int CountRaw(RollRecord r, int face) => r.Dice.Count(d => d.Raw == face);
}
