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

    public RollResult Roll(Guid frameId, RollPlan plan, int count)
    {
        var rolled = Enumerable.Range(0, count).Select(_ => Judge(plan, Draw(plan))).ToList();
        var record = new RollRecord(Guid.CreateVersion7(), plan.Roll, plan.Threshold, plan.Modifiers, rolled);
        var replaced = new List<DieReplacement>();
        for (var i = 0; i < rolled.Count; i++)
            if (plan.RerollRaw.Contains(rolled[i].Raw)) replaced.Add(new DieReplacement(i, rolled[i], Judge(plan, Draw(plan))));   // once, never again
        return new RollResult(record, replaced.Count == 0 ? null : new DiceRerolled(frameId, record.Id, replaced), Cursor);
    }
}

/// <summary>Reading a record back. A record's dice are what stands after every reroll and edit the applier copied in.</summary>
public static class RollOutcome
{
    public static int Successes(RollRecord r) => r.Dice.Count(d => d.Success);
    public static int Failures(RollRecord r) => r.Dice.Count(d => !d.Success);
    public static int CountRaw(RollRecord r, int face) => r.Dice.Count(d => d.Raw == face);
    /// <summary>A player's reroll: the named dice (all when empty) drawn again under the record's own threshold and modifiers.</summary>
    public static IReadOnlyList<DieReplacement> Reroll(RollPlan plan, RollRecord record, IReadOnlyList<int> indices, Roller roller) =>
        (indices.Count == 0 ? Enumerable.Range(0, record.Dice.Count) : indices).Select(i => new DieReplacement(i, record.Dice[i], roller.Judge(plan, roller.Draw(plan)))).ToList();
}
