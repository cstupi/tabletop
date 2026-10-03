# Task 2: The breach and the policy

**Files:**
- Modify: `server/game/Dispatch/EnforcementPolicy.cs`, `server/game/Dispatch/CommandDispatcher.cs`
- Test: `tests/Tabletop.GameServer.Tests/EnforcementPolicyTests.cs`

**Interfaces:**
- Consumes: `Enforcement`, `RefusalReason`, `Projection`, `GameEnded` (Task 1).
- Produces: `Breach(string Rule, string Detail, StrictConsequence Strict)`; `StrictConsequence`, `StrictRefuses(RefusalReason Reason)`, `StrictEndsGame(IReadOnlyList<Guid> Winners)`; `EndGame(string Reason, IReadOnlyList<Guid> Winners) : Verdict`; `EnforcementPolicy.Decide(Enforcement, Breach?)`, `EnforcementPolicy.BreachOf(Legality, string) : Breach?`, `EnforcementPolicy.EndGamePayloads(Projection, string, IReadOnlyList<Guid>) : IReadOnlyList<EventPayload>`, `const string OutsideLegalSetRule = "outside_legal_set"`, `const string IllegalRule = "illegal"`. The existing `Decide(Enforcement, Legality, string)` keeps its signature and behaviour.

Spec: enforcement spec → *The breach*, *The policy*, *Who raises a breach* (the dispatcher).

| Breach | `silent` | `warn` | `strict` |
| --- | --- | --- | --- |
| none | `Accept(null)` | `Accept(null)` | `Accept(null)` |
| `StrictRefuses(r)` | `Accept(null)` | `Accept(Detail)` | `Refuse(r, Detail)` |
| `StrictEndsGame(w)` | `Accept(null)` | `Accept(Detail)` | `EndGame(Detail, w)` |

- [ ] **Step 1: Write the failing tests**

Append to `tests/Tabletop.GameServer.Tests/EnforcementPolicyTests.cs`, inside the class:

```csharp
    static readonly Breach Ends = new("mission_length", "4 rounds have been played", new StrictEndsGame([Samples.Team1]));
    static readonly Breach Refuses = new("outside_legal_set", "not an option", new StrictRefuses(RefusalReason.OutsideLegalSet));

    [Fact]
    public void A_breach_takes_the_settings_three_positions()
    {
        Assert.Equal(new Accept(null), EnforcementPolicy.Decide(Enforcement.Silent, Ends));
        Assert.Equal(new Accept("4 rounds have been played"), EnforcementPolicy.Decide(Enforcement.Warn, Ends));
        var end = Assert.IsType<EndGame>(EnforcementPolicy.Decide(Enforcement.Strict, Ends));
        Assert.Equal("4 rounds have been played", end.Reason);
        Assert.Equal([Samples.Team1], end.Winners);

        Assert.Equal(new Accept(null), EnforcementPolicy.Decide(Enforcement.Silent, Refuses));
        Assert.Equal(new Accept("not an option"), EnforcementPolicy.Decide(Enforcement.Warn, Refuses));
        Assert.Equal(new Refuse(RefusalReason.OutsideLegalSet, "not an option"), EnforcementPolicy.Decide(Enforcement.Strict, Refuses));

        foreach (var e in Enum.GetValues<Enforcement>()) Assert.Equal(new Accept(null), EnforcementPolicy.Decide(e, (Breach?)null));
    }

    [Fact]
    public void Legality_never_ends_the_game_under_any_setting()
    {
        foreach (var e in Enum.GetValues<Enforcement>())
            foreach (var l in Enum.GetValues<Legality>())
                Assert.IsNotType<EndGame>(EnforcementPolicy.Decide(e, l, "detail"));
    }

    [Fact]
    public void Legality_maps_to_the_two_generic_rules()
    {
        Assert.Null(EnforcementPolicy.BreachOf(Legality.Legal, "d"));
        Assert.Null(EnforcementPolicy.BreachOf(Legality.AlwaysAccepted, "d"));
        Assert.Equal(new Breach(EnforcementPolicy.OutsideLegalSetRule, "d", new StrictRefuses(RefusalReason.OutsideLegalSet)), EnforcementPolicy.BreachOf(Legality.OutsideLegalSet, "d"));
        Assert.Equal(new Breach(EnforcementPolicy.IllegalRule, "d", new StrictRefuses(RefusalReason.Illegal)), EnforcementPolicy.BreachOf(Legality.Illegal, "d"));
    }

    [Fact]
    public void Ending_the_game_is_game_ended_alone_and_only_at_rest()
    {
        var applier = new EventApplier(Samples.Board, Samples.Game);
        var payloads = EnforcementPolicy.EndGamePayloads(applier.Projection, "over", [Samples.Team2]);
        var only = Assert.IsType<GameEnded>(Assert.Single(payloads));
        Assert.Equal("over", only.Reason);
        Assert.Equal([Samples.Team2], only.Winners);

        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.EndRound, "End the round", [Samples.Team1], new Confirmation(), false, false, null, null);
        applier.Apply(new GameEvent(1, null, new AskOpened(ask), null));
        Assert.Throws<InvalidOperationException>(() => EnforcementPolicy.EndGamePayloads(applier.Projection, "over", []));

        var framed = new EventApplier(Samples.Board, Samples.Game);
        framed.Apply(new GameEvent(1, null, new SequenceStarted(Guid.CreateVersion7(), "roll-off", new SequenceContext(null, [], [], null)), null));
        Assert.Throws<InvalidOperationException>(() => EnforcementPolicy.EndGamePayloads(framed.Projection, "over", []));
    }
```

Record equality on `IReadOnlyList` members is reference equality, which is why the `EndGame` and `GameEnded` assertions compare `Reason` and `Winners` separately rather than whole records.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter EnforcementPolicyTests`
Expected: compile errors: `Breach`, `StrictEndsGame`, `EndGame`, `BreachOf` and `EndGamePayloads` do not exist.

- [ ] **Step 3: Write the policy**

Replace the body of `server/game/Dispatch/EnforcementPolicy.cs` below the `using` lines with:

```csharp
using System.Diagnostics;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>AlwaysAccepted: the override path (menu, hand moves, markers, undo). Legal / OutsideLegalSet: an Ask answer against its set. Illegal: the engine's word for a command the rules cannot execute.</summary>
public enum Legality { AlwaysAccepted, Legal, OutsideLegalSet, Illegal }

public abstract record Verdict;
public sealed record Accept(string? Warning) : Verdict;
public sealed record Refuse(RefusalReason Reason, string Detail) : Verdict;
/// <summary>Strict's answer to a breach whose consequence is the end: game_ended with these winners in place of what was proposed.</summary>
public sealed record EndGame(string Reason, IReadOnlyList<Guid> Winners) : Verdict;

/// <summary>What was broken (a snake_case rule name, never on the wire), the sentence players read, and what strict does about it (enforcement spec → The breach).</summary>
public sealed record Breach(string Rule, string Detail, StrictConsequence Strict);
public abstract record StrictConsequence;
public sealed record StrictRefuses(RefusalReason Reason) : StrictConsequence;
/// <summary>One winner is a win, several a tie between them, none no result.</summary>
public sealed record StrictEndsGame(IReadOnlyList<Guid> Winners) : StrictConsequence;

/// <summary>silent | warn | strict as a decision over a breach (architecture.md → Rules enforcement; enforcement spec → The policy).</summary>
public static class EnforcementPolicy
{
    public const string OutsideLegalSetRule = "outside_legal_set", IllegalRule = "illegal";

    public static Verdict Decide(Enforcement enforcement, Breach? breach) => (breach, enforcement) switch
    {
        (null, _) => new Accept(null),
        (_, Enforcement.Silent) => new Accept(null),
        (_, Enforcement.Warn) => new Accept(breach.Detail),
        ({ Strict: StrictRefuses r }, Enforcement.Strict) => new Refuse(r.Reason, breach.Detail),
        ({ Strict: StrictEndsGame e }, Enforcement.Strict) => new EndGame(breach.Detail, e.Winners),
        _ => throw new UnreachableException($"no verdict for {breach.Strict.GetType().Name} under {enforcement}"),
    };

    /// <summary>A command's legality as a breach. Always StrictRefuses: command legality never ends the game.</summary>
    public static Breach? BreachOf(Legality legality, string detail) => legality switch
    {
        Legality.OutsideLegalSet => new Breach(OutsideLegalSetRule, detail, new StrictRefuses(RefusalReason.OutsideLegalSet)),
        Legality.Illegal => new Breach(IllegalRule, detail, new StrictRefuses(RefusalReason.Illegal)),
        _ => null,
    };

    public static Verdict Decide(Enforcement enforcement, Legality legality, string detail) => Decide(enforcement, BreachOf(legality, detail));

    /// <summary>
    /// The one place an EndGame verdict becomes payloads: game_ended alone. Only at rest — an empty stack and no open Ask — which the
    /// round boundary always is; a breach that could end the game mid-sequence would need a closing this does not provide.
    /// </summary>
    public static IReadOnlyList<EventPayload> EndGamePayloads(Projection projection, string reason, IReadOnlyList<Guid> winners)
    {
        if (projection.Stack.Count > 0) throw new InvalidOperationException($"the game cannot end with {projection.Stack.Count} frame(s) on the stack");
        if (projection.OpenAsks.Count > 0) throw new InvalidOperationException($"the game cannot end with {projection.OpenAsks.Count} open Ask(s)");
        return [new GameEnded(reason, winners)];
    }
}
```

`breach.Detail` in the `Warn` and `Strict` arms needs no `!`: the first arm has already matched `null`, and the compiler's flow analysis over a tuple switch knows it. If the build reports CS8602 there, write `breach!.Detail`.

- [ ] **Step 4: Make the dispatcher's two call sites total**

In `server/game/Dispatch/CommandDispatcher.cs`, add `using System.Diagnostics;`. In `Answer`, replace

```csharp
        if (verdict is Refuse r) return new Refused(r.Reason, r.Detail);
```

with

```csharp
        switch (verdict)
        {
            case Refuse r: return new Refused(r.Reason, r.Detail);
            case EndGame: throw new UnreachableException("a legality breach is always StrictRefuses");
        }
```

and make the identical replacement in `Engine`. The later `((Accept)verdict).Warning` casts are then safe.

- [ ] **Step 5: Run the GameServer tests that touch the dispatcher**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "EnforcementPolicyTests|CommandDispatcherTests|OutOfSetAnswerTests"`
Expected: PASS, the nine original `Three_positions` rows included.

- [ ] **Step 6: Commit**

```bash
git add server/game/Dispatch tests/Tabletop.GameServer.Tests/EnforcementPolicyTests.cs
git commit -m "Decide a breach under silent, warn and strict, with ending the game as strict's second consequence"
```
