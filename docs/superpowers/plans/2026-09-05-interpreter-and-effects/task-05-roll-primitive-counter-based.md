# Task 5: The roll primitive — a counter-based generator over the game's stored seed

**Files:**
- Create: `server/game/Dice/IDiceSource.cs`, `server/game/Dice/DiceCursor.cs`, `server/game/Dice/RollPlan.cs`, `server/game/Dice/Roller.cs`
- Modify: `server/game/Rules/IRulesEngine.cs` (`GameState.RngSeed`)
- Test: `tests/Tabletop.GameServer.Tests/DiceTests.cs`

**Interfaces:**
- Consumes: `GameState`, `EventApplier.EffectiveLog`, `StepCompleted`, `DiceRerolled`, `RollRecord`, `Die`, `RollModifier`, `DieReplacement` (Protocol).
- Produces (namespace `Tabletop.GameServer.Dice`): `interface IDiceSource { int Draw(ulong seed, long index, int sides); }`; `sealed class DiceSource : IDiceSource` (SplitMix64 over `seed ^ index`, uniform in `1..sides`); `sealed class ScriptedDice(params int[] faces) : IDiceSource` (face `faces[index % Length]`, seed ignored); `static class DiceCursor { static long DrawnSoFar(GameState state); }` (Σ `Dice.Count` over every `RollRecord` in every `StepCompleted` of the effective log, plus Σ `Replaced.Count` over every `DiceRerolled`); `GameState.RngSeed : ulong` (`unchecked((ulong)Info.RngSeed)` — the game's stored seed reinterpreted, never derived); `sealed record RollConvention(int Sides, int? ModifierCap)`; `sealed record RollPlan(string Roll, int? Threshold, IRL<RollModifier> Modifiers, RollConvention Convention, int? RequiredRaw, IReadOnlySet<int> RerollRaw)` with `int NetModifier` (summed, then clamped to `±ModifierCap` when set); `sealed record RollResult(RollRecord Record, DiceRerolled? ForcedRerolls, long NextCursor)`; `sealed class Roller(IDiceSource dice, ulong seed, long cursor)` with `RollResult Roll(Guid frameId, RollPlan plan, int count)` and `Die Judge(RollPlan plan, int raw)`; `static class RollOutcome` with `int Successes(RollRecord)`, `int Failures(RollRecord)`, `int CountRaw(RollRecord, int face)`, `IRL<Die> Reroll(RollPlan, RollRecord, IRL<int> indices, Roller) → IRL<DieReplacement>`.
- Semantics: `Modified = Raw + NetModifier`; `Success = RequiredRaw is { } r ? Raw >= r : Threshold is { } t && Modified >= t`; a null threshold (the roll-off) records `Success = false` for every die and compares raw. A forced reroll (`RerollRaw` contains the raw face) draws one more die once and is recorded as a `DiceRerolled` payload following the `step_completed`, so the record shows the replaced die and the cursor counts every draw (action sequences → One roll primitive; resolution → A reroll records which dice it replaced).
- The seed is stored on the game and never changes, and the cursor is counted, not stored: rebuilding from the log reproduces the dice, and `Undone(N)` shortens the effective log so the next roll after an undo repeats the roll it replaced (`architecture.md` → Undo: "rewinding and redoing the same action reproduces the same dice"). Undo and replay are exact because `management.game.rng_seed` is written once at creation and nothing ever writes it again; the seed is stored, not derived from the game id.

Spec: `architecture.md` → Command / Event spine ("RNG lives in server state as a seed"), Undo; action sequences → One roll primitive (the split table: the engine owns the seeded RNG and per-die recording; `GameRules` the convention, threshold and cap); scheduler → Deployment (null threshold; ties rerolled); resolution → Players roll their own dice. The seed is stored, not derived from the game id, and is consumed as the contracts plan's `GameInfo.RngSeed : long` (Task 8).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class DiceTests
{
    static readonly RollConvention D6 = new(6, null);
    static RollPlan Plan(int? threshold, params RollModifier[] mods) => new("hit", threshold, mods, D6, null, new HashSet<int>());
    static GameState State(params EventPayload[] history)
    {
        var applier = new EventApplier(Samples.Board, Samples.Game);
        applier.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, null, p, null)));
        return new GameState(Samples.Game, Samples.Board, applier, GrimdarkFutureRules.Instance);
    }
    static GameState Seeded(GameState state, long seed) => new(Samples.Game with { RngSeed = seed }, Samples.Board, state.Applier, GrimdarkFutureRules.Instance);

    [Fact]
    public void The_source_is_deterministic_uniform_and_seed_dependent()
    {
        var d = new DiceSource();
        Assert.Equal(d.Draw(7, 3, 6), d.Draw(7, 3, 6));
        Assert.NotEqual(Enumerable.Range(0, 20).Select(i => d.Draw(7, i, 6)), Enumerable.Range(0, 20).Select(i => d.Draw(8, i, 6)));
        var faces = Enumerable.Range(0, 60_000).Select(i => d.Draw(1, i, 6)).GroupBy(f => f).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(6, faces.Count); Assert.All(faces.Values, n => Assert.InRange(n, 9_000, 11_000));
        Assert.Equal([3, 4, 5, 3], Enumerable.Range(0, 4).Select(i => new ScriptedDice(3, 4, 5).Draw(0, i, 6)));
    }

    [Fact]
    public void A_roll_records_raw_modified_success_and_advances_the_cursor()
    {
        var roller = new Roller(new ScriptedDice(3, 4, 5), 0, 0);
        var result = roller.Roll(Samples.Frame, Plan(4, new RollModifier("Stealth", -1)), 3);
        Assert.Equal([(3, 2, false), (4, 3, false), (5, 4, true)], result.Record.Dice.Select(x => (x.Raw, x.Modified, x.Success)));
        Assert.Equal((4, "hit", 3L), (result.Record.Threshold, result.Record.Roll, result.NextCursor));
        Assert.Null(result.ForcedRerolls);
        Assert.Equal(1, RollOutcome.Successes(result.Record)); Assert.Equal(2, RollOutcome.Failures(result.Record));
    }

    [Fact]
    public void Caps_required_raw_and_null_threshold()
    {
        var capped = new Roller(new ScriptedDice(4), 0, 0).Roll(Samples.Frame, Plan(4, new RollModifier("a", 1), new RollModifier("b", 1)) with { Convention = new RollConvention(6, 1) }, 1).Record.Dice[0];
        Assert.Equal((4, 5), (capped.Raw, capped.Modified));
        var fatigued = new Roller(new ScriptedDice(5, 6), 0, 0).Roll(Samples.Frame, Plan(2) with { RequiredRaw = 6 }, 2).Record.Dice;
        Assert.Equal([false, true], fatigued.Select(x => x.Success));
        var rollOff = new Roller(new ScriptedDice(2, 6), 0, 0).Roll(Samples.Frame, Plan(null), 2).Record;
        Assert.Null(rollOff.Threshold); Assert.All(rollOff.Dice, x => Assert.False(x.Success)); Assert.Equal(1, RollOutcome.CountRaw(rollOff, 6));
    }

    [Fact]
    public void A_forced_reroll_is_a_dice_rerolled_payload_and_the_cursor_counts_the_extra_draw()
    {
        var plan = Plan(4) with { Roll = "defense", RerollRaw = new HashSet<int> { 6 } };
        var result = new Roller(new ScriptedDice(6, 2, 6), 0, 0).Roll(Samples.Frame, plan, 2);
        Assert.Equal([6, 2], result.Record.Dice.Select(x => x.Raw));                                  // the record holds the dice as rolled
        var rr = Assert.IsType<DiceRerolled>(result.ForcedRerolls);
        Assert.Equal((0, 6, 6), (rr.Replaced.Single().Index, rr.Replaced.Single().Old.Raw, rr.Replaced.Single().New.Raw));   // rerolled once, still a 6: blocked
        Assert.Equal(3, result.NextCursor);
    }

    [Fact]
    public void The_cursor_is_counted_from_the_effective_log_and_the_seed_from_the_game()
    {
        var hit = new RollRecord(Guid.CreateVersion7(), "hit", 4, [], [new Die(3, 3, false), new Die(5, 5, true)]);
        // EventApplier requires the frame's SequenceStarted before a StepCompleted.
        var state = State(new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [Samples.Unit], [], "Advance")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [hit], []),
            new DiceRerolled(Samples.Frame, hit.Id, [new DieReplacement(0, new Die(3, 3, false), new Die(6, 6, true))]),
            new DiceEdited(Samples.Frame, hit.Id, [new DieReplacement(1, new Die(5, 5, true), new Die(1, 1, false))]));
        Assert.Equal(3, DiceCursor.DrawnSoFar(state));
        var undone = State(new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [Samples.Unit], [], "Advance")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [hit], []), new Undone(0));
        Assert.Equal(0, DiceCursor.DrawnSoFar(undone));
        Assert.Equal(state.RngSeed, undone.RngSeed);                                          // undo never touches the seed
        Assert.Equal(unchecked((ulong)Samples.Game.RngSeed), state.RngSeed);                  // the stored value, whole; nothing is derived from the id
        Assert.Equal(7UL, Seeded(state, 7).RngSeed);
        Assert.Equal(ulong.MaxValue, Seeded(state, -1).RngSeed);                              // bigint is signed: a negative seed reinterprets, never throws
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Add to `GameState` in `Rules/IRulesEngine.cs`**

```csharp
    /// <summary>The game's stored dice seed, generated once when the game row was created and never written again. The cursor is Dice.DiceCursor.
    /// The column is bigint and the generator wants a ulong, so the sign bit is reinterpreted rather than clamped: every one of the 2^64 stored values is a distinct seed.</summary>
    public ulong RngSeed { get; } = unchecked((ulong)info.RngSeed);
```

- [ ] **Step 3: Write `IDiceSource.cs` and `DiceCursor.cs`**

```csharp
namespace Tabletop.GameServer.Dice;

public interface IDiceSource { int Draw(ulong seed, long index, int sides); }

/// <summary>Counter-based: die i of a game is a pure function of (seed, i), so the cursor is the whole RNG state.</summary>
public sealed class DiceSource : IDiceSource
{
    public int Draw(ulong seed, long index, int sides)
    {
        var z = seed ^ ((ulong)index * 0x9E3779B97F4A7C15UL) + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z % (ulong)sides) + 1;
    }
}

/// <summary>Tests: the faces in order, cycling. The seed is ignored; the index is the cursor.</summary>
public sealed class ScriptedDice(params int[] faces) : IDiceSource
{
    public int Draw(ulong seed, long index, int sides) => faces[(int)(index % faces.Length)];
}
```

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dice;

public static class DiceCursor
{
    /// <summary>Every die ever drawn in the live log: the dice in each roll record, plus each forced or player reroll. Edits draw nothing.</summary>
    public static long DrawnSoFar(GameState state) => EventApplier.EffectiveLog(state.Applier.Log).Sum(e => e.Payload switch
    {
        StepCompleted s => s.Rolls.Sum(r => (long)r.Dice.Count),
        DiceRerolled d => d.Replaced.Count,
        _ => 0L,
    });
}
```

- [ ] **Step 4: Write `RollPlan.cs` and `Roller.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dice;

/// <summary>What GameRules supplies per roll: the die, and whether summed modifiers are clamped (OPR never; 40k ±1).</summary>
public sealed record RollConvention(int Sides, int? ModifierCap);

/// <summary>One roll, settled: the threshold GameRules derived, the modifiers the two passes and the players contributed, and the per-die rules.</summary>
public sealed record RollPlan(string Roll, int? Threshold, IReadOnlyList<RollModifier> Modifiers, RollConvention Convention, int? RequiredRaw, IReadOnlySet<int> RerollRaw)
{
    public int NetModifier
    {
        get { var sum = Modifiers.Sum(m => m.Amount); return Convention.ModifierCap is { } cap ? Math.Clamp(sum, -cap, cap) : sum; }
    }
}

public sealed record RollResult(RollRecord Record, DiceRerolled? ForcedRerolls, long NextCursor);

/// <summary>Draws from the cursor, judges each die under the plan, and records everything. Summing is generic; clamping is the convention's.</summary>
public sealed class Roller(IDiceSource dice, ulong seed, long cursor)
{
    public long Cursor { get; private set; } = cursor;

    public Die Judge(RollPlan plan, int raw)
    {
        var modified = raw + plan.NetModifier;
        var success = plan.RequiredRaw is { } r ? raw >= r : plan.Threshold is { } t && modified >= t;
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
```

- [ ] **Step 5: Run `dotnet test tests/Tabletop.GameServer.Tests --filter DiceTests`; expect 5 passed. Commit: `Add the roll primitive over a seeded, counter-based dice source`.**
