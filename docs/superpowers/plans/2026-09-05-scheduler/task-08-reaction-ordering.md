# Task 8: Reaction ordering

**Files:**
- Test: `tests/Tabletop.GameServer.Tests/ReactionOrderingTests.cs`

**Interfaces:**
- Consumes: `FakeRules` and `TestGames` (Tasks 5 and 2); `Projection.Scheduler.Order/Passed`; the interpreter plan's `Windows.ReactionOrder(GameState, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams)` (its Task 9) — **the one ordering policy**: `IGameRules.OrderReactions` when non-null, else turn order from the team after the actor, the actor's own team last, passed teams dropped; `Sequence`, `LambdaStep`, `Complete` (interpreter plan Task 8).
- Produces: no production code — the ordering lives in `Windows.ReactionOrder`, which `Windows.Run` calls, and this plan's `IGameRules.OrderReactions` (Task 2) is the hook it consults; these tests pin the policy from the scheduler's side. No test infrastructure either: `FakeRules` is Task 5's, and this task only sets its `OrderReactionsHook`.

Spec: scheduler → Windows ("Eligibility is the sequence's business ... Ordering is the scheduler's. When more than one team answers react, their reactions resolve in the current turn order, starting with the team after the actor. Teams that passed drop out. `GameRules` may replace the ordering with a hook"); What the interface must not assume 5 (a window may belong to a non-active team: the actor is whoever's step opened the window, not the active team) and 6 (ordering is a policy with a default). The actor's own team is not excluded — OPR's casting window is offered to it (interpreter plan Task 11, `GfHooks.ReactionsAt`) — it simply comes last. OPR's one reaction outside casting, the optional **Overwatch** action (page 33), is not built by this plan or any other: it is one of the optional extra actions the handoff defers, and the scheduler's whole share of it is constraint 5 — a window belonging to a non-active team — which Task 9 tests through a fake ruleset.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class ReactionOrderingTests
{
    static TestGames.Built Four() => TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1), TestArmies.Build("D", 1, 1)]);

    [Fact]
    public void Reactions_resolve_in_turn_order_starting_after_the_actor_and_passed_teams_drop_out()
    {
        var g = Four(); var t = g.Teams;
        TestGames.Apply(g, new RoundStarted(1, [t[2], t[0], t[3], t[1]], []));
        Assert.Equal([t[3], t[1], t[2]], Windows.ReactionOrder(g.State, t[0], [t[1], t[2], t[3]]));
        Assert.Equal([t[1], t[2]], Windows.ReactionOrder(g.State, t[0], [t[1], t[2]]));                 // only those who reacted
        Assert.Equal([t[3], t[1], t[2], t[0]], Windows.ReactionOrder(g.State, t[0], t));                // the actor's own team may react (OPR's casting window): last
        TestGames.Apply(g, new TurnStarted(t[3]), new TurnEnded(t[3], true, []));
        Assert.Equal([t[1], t[2]], Windows.ReactionOrder(g.State, t[0], [t[1], t[2], t[3]]));           // t3 passed
        Assert.Equal([t[0], t[1]], Windows.ReactionOrder(g.State, t[2], [t[0], t[1], t[3]]));           // after t2 comes t0, then t3 (passed, dropped), then t1
    }

    [Fact]
    public void A_non_active_actor_is_still_the_anchor_and_no_order_yet_means_team_order()
    {
        var g = Four(); var t = g.Teams;
        Assert.Equal([t[1], t[2]], Windows.ReactionOrder(g.State, t[0], [t[2], t[1]]));                 // round 0, no order yet: Info.Teams order after the actor
        TestGames.Apply(g, new RoundStarted(1, [t[0], t[1], t[2], t[3]], []), new TurnStarted(t[0]));
        Assert.Equal([t[3], t[0], t[1]], Windows.ReactionOrder(g.State, t[2], [t[0], t[1], t[3]]));     // t2 reacted inside t0's turn; the order still starts after t2
    }

    [Fact]
    public void Game_rules_may_replace_the_ordering()
    {
        var rules = new FakeRules { OrderReactionsHook = (_, actor, reacting) => reacting.Reverse().ToList() };
        var g = TestGames.Build([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)], rules: rules);
        TestGames.Apply(g, new RoundStarted(1, g.Teams, []));
        Assert.Equal([g.Teams[2], g.Teams[1]], Windows.ReactionOrder(g.State, g.Teams[0], [g.Teams[1], g.Teams[2]]));
    }
}
```

- [ ] **Step 2: There is no ordering class to write.** `Windows.ReactionOrder` (interpreter plan, `Interpreter/Windows.cs`) is the one policy, and it already consults `state.Rules.OrderReactions`. If the interpreter plan's Task 9 has not landed, land it first; nothing here can substitute for it.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter ReactionOrdering`; expect 3 passed. Commit: `Pin reaction ordering from the scheduler's side`.**
