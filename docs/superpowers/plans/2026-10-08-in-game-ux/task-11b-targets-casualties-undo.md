# Task 11b: Target assignment, casualty pick and hand-move undo

**Wave:** 2 · **Depends on:** — · **Model:** opus

**Files:**
- Create: `client/src/Core/TargetAssignment.cs`, `client/src/Core/CasualtyPick.cs`, `client/src/Core/HandMoveUndo.cs`
- Test: `tests/Tabletop.Client.Tests/{TargetAssignmentTests,CasualtyPickTests,HandMoveUndoTests}.cs` (new)

**Interfaces:**
- Consumes (existing): `Assignment`, `Allocation`, `Assigned`, `Allocated`, `AskOption`.
- Produces:

```csharp
public sealed class TargetAssignment { public TargetAssignment(Assignment legal);
    public IReadOnlyList<AskOption> Groups { get; } public IReadOnlySet<string> Picked { get; } public IReadOnlyDictionary<string, Guid> Assigned { get; }
    public void PickOnly(string groupId); public bool Aim(Guid targetUnitId); public void Clear(); public bool CanTarget(Guid unitId); public AskAnswer? Answer(); }
public sealed class CasualtyPick { public CasualtyPick(Allocation legal, IReadOnlyDictionary<Guid, int> woundsLeft);
    public IReadOnlyDictionary<Guid, int> Counts { get; } public int Chosen { get; } public int Required { get; } public bool Complete { get; }
    public void Add(Guid modelId); public void Remove(Guid modelId); public void Auto(); public AskAnswer? Answer(); }
public sealed class HandMoveUndo { public long? UndoTo { get; } public IReadOnlyList<Guid> Models { get; }
    public void OnOwnMove(long movedSeq, IReadOnlyList<Guid> models); public void OnEvent(long seq); }
```

Spec: *Table interaction → Shooting targets, Casualties, Moves*.

## Shooting targets

p.8: *"Each group may be fired at a different target, however you may fire only at up to two different targets, and
all weapons from the same group must fire at the same target."* Every group starts picked; `Aim` points every picked
group at the unit. `CanTarget` is the glow: an offered unit that, with the picked groups moved to it, keeps the
distinct targets within `MaxDistinctTargets`. With a third group picked alone after two targets are taken, nothing
else glows; with the only group on the second target picked, re-aiming it elsewhere stays legal and glows.
`Answer` is `Assigned` once every group has a target (`LegalSetMembership` requires every item), else null.

## Casualties

p.8: *"For each wound that the unit has taken, the defending player must remove one model as a casualty."* A click
adds one wound to a model up to its wounds left (max from `Session.MaxWounds` minus `ModelInPlay.Wounds`; an absent
id is 1); a right-click takes one away. `Required` is the lesser of `Amount` and the targets' total wounds left; Enter
is enabled at `Complete` (`Chosen == Required`). The server's `assign` step (`GfAttack.AssignStep`) applies at most a
model's remaining wounds and carries the rest over to the next segment (p.8), and `LegalSetMembership` wants the counts
to sum to `Amount`, so `Answer` adds `Amount − Chosen` to the last target in the Ask's order. `Auto` fills the targets
in the Ask's order.

## Hand-move undo

`OnOwnMove(seq, models)` arms the undo at `seq − 1`; any `OnEvent` with a later seq disarms it, because an undo to that
point would take the later event with it (Review Focus 2). `UndoMoveButton` (Task 18) calls `OnOwnMove` for a
`ModelsMoved` event of mine from a hand move, and `OnEvent` for every other event applied.

- [ ] **Step 1: Write the failing target assignment tests**

Create `tests/Tabletop.Client.Tests/TargetAssignmentTests.cs`:

```csharp
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class TargetAssignmentTests
{
    static readonly Guid T1 = Guid.Parse("0198b7ac-0001-7000-8000-000000000001"), T2 = Guid.Parse("0198b7ac-0002-7000-8000-000000000002"),
        T3 = Guid.Parse("0198b7ac-0003-7000-8000-000000000003"), NotOffered = Guid.Parse("0198b7ac-0009-7000-8000-000000000009");
    static Assignment Legal(params string[] groups) => new(groups.Select(g => new AskOption(g, g, [])).ToList(), [T1, T2, T3], 2);

    [Fact]
    public void Every_group_starts_picked_and_one_click_aims_them_all()
    {
        var t = new TargetAssignment(Legal("Rifles", "Plasma Gun"));
        Assert.Equal(["Rifles", "Plasma Gun"], t.Groups.Select(g => g.Id));
        Assert.Equal(new HashSet<string> { "Rifles", "Plasma Gun" }, t.Picked);
        Assert.Null(t.Answer());
        Assert.True(t.Aim(T1));
        var answer = Assert.IsType<Assigned>(t.Answer());
        Assert.Equal(new Dictionary<string, Guid> { ["Rifles"] = T1, ["Plasma Gun"] = T1 }, answer.Targets);
    }

    [Fact]
    public void A_chip_picks_its_group_alone_for_a_second_target()
    {
        var t = new TargetAssignment(Legal("Rifles", "Plasma Gun"));
        t.Aim(T1);
        t.PickOnly("Plasma Gun");
        Assert.Equal(new HashSet<string> { "Plasma Gun" }, t.Picked);
        Assert.True(t.Aim(T2));
        Assert.Equal(new Dictionary<string, Guid> { ["Rifles"] = T1, ["Plasma Gun"] = T2 }, t.Assigned);
        Assert.True(t.CanTarget(T3));      // the plasma gun's target can still move: Rifles and the new one make two
    }

    [Fact]
    public void Two_targets_taken_and_no_other_unit_glows()
    {
        var t = new TargetAssignment(Legal("Rifles", "Plasma Gun", "Grenades"));
        t.Aim(T1);
        t.PickOnly("Plasma Gun"); t.Aim(T2);
        t.PickOnly("Grenades");
        Assert.False(t.CanTarget(T3));
        Assert.False(t.Aim(T3));
        Assert.True(t.CanTarget(T1)); Assert.True(t.CanTarget(T2));
        Assert.Equal(T1, t.Assigned["Grenades"]);    // unchanged by the refused aim
    }

    [Fact]
    public void A_unit_the_ask_did_not_offer_is_never_a_target()
    {
        var t = new TargetAssignment(Legal("Rifles"));
        Assert.False(t.CanTarget(NotOffered));
        Assert.False(t.Aim(NotOffered));
        Assert.Empty(t.Assigned);
    }

    [Fact]
    public void Every_group_needs_a_target_before_it_fires_and_clear_starts_over()
    {
        var t = new TargetAssignment(Legal("Rifles", "Plasma Gun"));
        t.PickOnly("Rifles"); t.Aim(T1);
        Assert.Null(t.Answer());
        t.Clear();
        Assert.Empty(t.Assigned);
        Assert.Equal(new HashSet<string> { "Rifles", "Plasma Gun" }, t.Picked);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter TargetAssignmentTests`
Expected: compile error: `TargetAssignment` does not exist.

- [ ] **Step 3: Write the target assignment**

Create `client/src/Core/TargetAssignment.cs`:

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The declare-targets pick (in-game UX spec → Shooting targets). p.8: each weapon group fires at one target, at most two targets in
/// all. Every group starts picked; aiming points the picked groups at a unit; a chip picks its group alone. The answer is
/// Assigned, every group to one target.
/// </summary>
public sealed class TargetAssignment
{
    readonly Assignment _legal;
    readonly HashSet<string> _picked;
    readonly Dictionary<string, Guid> _assigned = new();

    public TargetAssignment(Assignment legal)
    {
        _legal = legal;
        _picked = legal.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlyList<AskOption> Groups => _legal.Items;
    public IReadOnlySet<string> Picked => _picked;
    public IReadOnlyDictionary<string, Guid> Assigned => _assigned;

    public void PickOnly(string groupId)
    {
        if (_legal.Items.All(i => i.Id != groupId)) return;
        _picked.Clear(); _picked.Add(groupId);
    }

    /// <summary>An offered unit that, with the picked groups moved onto it, keeps the distinct targets within the Ask's maximum.</summary>
    public bool CanTarget(Guid unitId) =>
        _picked.Count > 0 && _legal.Targets.Contains(unitId)
        && _assigned.Where(kv => !_picked.Contains(kv.Key)).Select(kv => kv.Value).Append(unitId).Distinct().Count() <= _legal.MaxDistinctTargets;

    public bool Aim(Guid targetUnitId)
    {
        if (!CanTarget(targetUnitId)) return false;
        foreach (var g in _picked) _assigned[g] = targetUnitId;
        return true;
    }

    public void Clear()
    {
        _assigned.Clear();
        _picked.Clear(); _picked.UnionWith(_legal.Items.Select(i => i.Id));
    }

    /// <summary>Null until every group has a target: the legal set wants every item answered.</summary>
    public AskAnswer? Answer() =>
        _legal.Items.All(i => _assigned.ContainsKey(i.Id)) ? new Assigned(new Dictionary<string, Guid>(_assigned)) : null;
}
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Client.Tests --filter TargetAssignmentTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Write the failing casualty tests**

Create `tests/Tabletop.Client.Tests/CasualtyPickTests.cs`:

```csharp
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class CasualtyPickTests
{
    static readonly Guid M1 = Guid.Parse("0198b7ac-0011-7000-8000-000000000011"), M2 = Guid.Parse("0198b7ac-0012-7000-8000-000000000012"),
        M3 = Guid.Parse("0198b7ac-0013-7000-8000-000000000013"), Elsewhere = Guid.Parse("0198b7ac-0019-7000-8000-000000000019");

    /// <summary>Review Focus 5: a click on a model already at its wounds left does not count, and Complete is exactly the amount.</summary>
    [Fact]
    public void Add_stops_at_wounds_left()
    {
        var c = new CasualtyPick(new Allocation([M1, M2], 3), new Dictionary<Guid, int> { [M1] = 2, [M2] = 1 });
        c.Add(M2); c.Add(M2);
        Assert.Equal(1, c.Counts[M2]);
        Assert.Equal((1, false), (c.Chosen, c.Complete));
        c.Add(M1);
        Assert.False(c.Complete);
        c.Add(M1); c.Add(M1);
        Assert.Equal((2, 3, true), (c.Counts[M1], c.Chosen, c.Complete));
    }

    [Fact]
    public void Nothing_past_the_amount_and_nothing_outside_the_targets()
    {
        var c = new CasualtyPick(new Allocation([M1, M2], 1), new Dictionary<Guid, int> { [M1] = 1, [M2] = 1 });
        c.Add(Elsewhere);
        Assert.Equal(0, c.Chosen);
        c.Add(M1); c.Add(M2);
        Assert.Equal((1, true, false), (c.Chosen, c.Complete, c.Counts.ContainsKey(M2)));
    }

    [Fact]
    public void Remove_takes_one_away_and_an_absent_count_is_one_wound()
    {
        var c = new CasualtyPick(new Allocation([M1, M2], 2), new Dictionary<Guid, int>());     // no facts: one wound each
        Assert.Equal(2, c.Required);
        c.Add(M1); c.Add(M1);
        Assert.Equal(1, c.Counts[M1]);
        c.Remove(M1); c.Remove(M1); c.Remove(M2);
        Assert.Empty(c.Counts);
        Assert.Null(c.Answer());
    }

    [Fact]
    public void Auto_fills_what_is_left_in_the_ask_s_order()
    {
        var c = new CasualtyPick(new Allocation([M1, M2, M3], 2), new Dictionary<Guid, int> { [M1] = 1, [M2] = 1, [M3] = 1 });
        c.Add(M3);
        c.Auto();
        Assert.Equal(new Dictionary<Guid, int> { [M3] = 1, [M1] = 1 }, c.Counts);
        var answer = Assert.IsType<Allocated>(c.Answer());
        Assert.Equal(new Dictionary<Guid, int> { [M3] = 1, [M1] = 1 }, answer.Counts);
    }

    /// <summary>p.8: one model per wound. More wounds than the offered models can take: every model takes what it can, and the
    /// answer still sums to the amount, the rest on the last target, for the server's assign step to carry over.</summary>
    [Fact]
    public void Wounds_past_every_model_s_capacity_complete_at_capacity_and_answer_the_whole_amount()
    {
        var c = new CasualtyPick(new Allocation([M1, M2], 5), new Dictionary<Guid, int> { [M1] = 1, [M2] = 1 });
        Assert.Equal(2, c.Required);
        c.Auto();
        Assert.True(c.Complete);
        var answer = Assert.IsType<Allocated>(c.Answer());
        Assert.Equal(new Dictionary<Guid, int> { [M1] = 1, [M2] = 4 }, answer.Counts);
        Assert.Equal(2, c.Chosen);            // the pick itself still shows what was clicked
    }
}
```

- [ ] **Step 6: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter CasualtyPickTests`
Expected: compile error: `CasualtyPick` does not exist.

- [ ] **Step 7: Write the casualty pick**

Create `client/src/Core/CasualtyPick.cs`:

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The remove-casualties pick (in-game UX spec → Casualties). A click adds one wound to an offered model up to its wounds left; a
/// right-click takes one away; Auto fills the rest in the Ask's order. Required is the lesser of the amount and what the offered
/// models can take, so a pick whose wounds outnumber the models still completes. The answer is Allocated,
/// summing to the amount, any surplus on the last target for the server to carry over (p.8).
/// </summary>
public sealed class CasualtyPick
{
    readonly Allocation _legal;
    readonly IReadOnlyDictionary<Guid, int> _woundsLeft;
    readonly Dictionary<Guid, int> _counts = new();

    public CasualtyPick(Allocation legal, IReadOnlyDictionary<Guid, int> woundsLeft)
    {
        _legal = legal; _woundsLeft = woundsLeft;
        Required = Math.Min(legal.Amount, legal.Targets.Distinct().Sum(Capacity));
    }

    public IReadOnlyDictionary<Guid, int> Counts => _counts;
    public int Chosen => _counts.Values.Sum();
    public int Required { get; }
    public bool Complete => Chosen == Required;

    /// <summary>Wounds left; a model the facts do not name takes one.</summary>
    int Capacity(Guid modelId) => Math.Max(0, _woundsLeft.GetValueOrDefault(modelId, 1));

    public void Add(Guid modelId)
    {
        if (!_legal.Targets.Contains(modelId) || Chosen >= Required || _counts.GetValueOrDefault(modelId) >= Capacity(modelId)) return;
        _counts[modelId] = _counts.GetValueOrDefault(modelId) + 1;
    }

    public void Remove(Guid modelId)
    {
        if (!_counts.TryGetValue(modelId, out var n)) return;
        if (n <= 1) _counts.Remove(modelId); else _counts[modelId] = n - 1;
    }

    public void Auto()
    {
        foreach (var m in _legal.Targets)
            while (Chosen < Required && _counts.GetValueOrDefault(m) < Capacity(m)) Add(m);
    }

    public AskAnswer? Answer()
    {
        if (!Complete) return null;
        var counts = new Dictionary<Guid, int>(_counts);
        if (_legal.Amount > Chosen && _legal.Targets.Count > 0)
            counts[_legal.Targets[^1]] = counts.GetValueOrDefault(_legal.Targets[^1]) + _legal.Amount - Chosen;
        return new Allocated(counts);
    }
}
```

- [ ] **Step 8: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Client.Tests --filter CasualtyPickTests`
Expected: PASS, 5 tests.

- [ ] **Step 9: Write the failing undo tests**

Create `tests/Tabletop.Client.Tests/HandMoveUndoTests.cs`:

```csharp
using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class HandMoveUndoTests
{
    static readonly Guid M = Guid.Parse("0198b7ac-0021-7000-8000-000000000021");

    [Fact]
    public void A_hand_move_arms_the_undo_to_the_event_before_it()
    {
        var u = new HandMoveUndo();
        Assert.Null(u.UndoTo);
        u.OnOwnMove(5, [M]);
        u.OnEvent(5);                     // the move itself
        Assert.Equal(4L, u.UndoTo);
        Assert.Equal([M], u.Models);
    }

    /// <summary>Review Focus 2: an undo to before the move would take any later event with it.</summary>
    [Fact]
    public void A_later_event_expires_the_undo()
    {
        var u = new HandMoveUndo();
        u.OnOwnMove(5, [M]);
        u.OnEvent(6);
        Assert.Null(u.UndoTo);
        Assert.Empty(u.Models);
        u.OnOwnMove(7, [M]);              // a fresh move arms it again
        Assert.Equal(6L, u.UndoTo);
    }
}
```

- [ ] **Step 10: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter HandMoveUndoTests`
Expected: compile error: `HandMoveUndo` does not exist.

- [ ] **Step 11: Write the undo**

Create `client/src/Core/HandMoveUndo.cs`:

```csharp
namespace Tabletop.Client.Core;

/// <summary>
/// The Undo move button and Ctrl+Z (in-game UX spec → Moves): an Undo to the event before my last hand move, offered until any
/// later event lands, since an undo to that point would take the later event with it.
/// </summary>
public sealed class HandMoveUndo
{
    long? _moved;

    public long? UndoTo => _moved - 1;
    public IReadOnlyList<Guid> Models { get; private set; } = [];

    public void OnOwnMove(long movedSeq, IReadOnlyList<Guid> models) { _moved = movedSeq; Models = models.ToList(); }

    public void OnEvent(long seq)
    {
        if (_moved is { } moved && seq > moved) { _moved = null; Models = []; }
    }
}
```

- [ ] **Step 12: Run everything and check the client**

Run: `dotnet test tests/Tabletop.Client.Tests && dotnet build client/Tabletop.csproj && ./scripts/check-client`
Expected: every test passes (the three new classes: 5 + 5 + 2), the build succeeds, `check-client` prints nothing.

- [ ] **Step 13: Commit**

```bash
git add client/src/Core/TargetAssignment.cs client/src/Core/CasualtyPick.cs client/src/Core/HandMoveUndo.cs \
  tests/Tabletop.Client.Tests/TargetAssignmentTests.cs tests/Tabletop.Client.Tests/CasualtyPickTests.cs tests/Tabletop.Client.Tests/HandMoveUndoTests.cs
git commit -m "Model the target and casualty picks and the hand-move undo" \
  -m "Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
