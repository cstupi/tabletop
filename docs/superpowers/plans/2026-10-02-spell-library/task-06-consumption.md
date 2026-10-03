# Task 6: A grant is consumed by the first step that would apply it

**Files:**
- Modify: `server/game/Effects/LiveProperties.cs` (`LiveSet` records the grants it read; `Settle` records a granted suppression or parameter change that changed something)
- Modify: `server/game/Effects/RollEffects.cs` (`Plan`, `Multiplier`, `OwnRolls` record; `ReadPerDie`; `Consume` replaces `ConsumeGrants`)
- Modify: `server/game/Effects/StatReader.cs` (`ActionDistance`, `WeaponRange`, `TargetNumber` record)
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs` (`Consumed`, `DetermineAttacksStep`, `RollToHitStep`, `AssignStep`)
- Modify: `server/game/Rules/GrimdarkFuture/GfMoves.cs` (`MoveStep` consumes after a measured move)
- Modify: `server/game/Rules/GrimdarkFuture/GfMorale.cs` (`TestStep` consumes)
- Test: `tests/Tabletop.GameServer.Tests/GrantRowTests.cs`

**Interfaces:**
- Consumes: `LiveProperty.GrantRow`, `GrantId` (Task 5).
- Produces: `LiveSet.Read(LiveProperty source)` (records a granted source); `LiveSet.GrantsRead` → `IReadOnlyCollection<(Guid UnitId, Guid GrantId)>`; `RollEffects.ReadPerDie(LiveSet live, Side side, string roll)`; `RollEffects.Consume(LiveSet live) → IReadOnlyList<StateChange>` (a `GrantRemoved` for every grant on a read grant's unit with the same `EntryName`).

Spec → *A grant is consumed by the first step that would apply it*. *"Once (next time the effect would apply)"*: the grant's row is used by the step, with every condition holding except a per-die one. A grant read to build a legal set is not consumed; only a step that acts emits the removals. The grants one cast left on a unit go together.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Tabletop.GameServer.Tests/GrantRowTests.cs` (the class from Task 5; it has `Props`, `Grant`, `Attack`, `Row`, `Spell`):

```csharp
    [Fact]
    public void A_speed_grant_is_consumed_by_the_next_move_and_its_three_rows_go_together()
    {
        var a = Unit("A", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Props, new ScriptedDice(4));
        Grant(sim.State, a.Id, "Shock Speed", a.Id);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        Assert.Equal(8, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        var m = a.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + 7 } }))]));
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void Offering_charge_reads_a_speed_grant_without_consuming_it()
    {
        var a = Unit("A", "4+", "4+", [], 1, []); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b], x1: 0, x2: 15);                        // 15" apart: out of a 12" charge, inside 16"
        Grant(state, a.Id, "Shock Speed", a.Id);
        Assert.Contains(b.Id, GfMelee.ChargeTargets(state, Props, a.Id));
        Assert.Equal(3, state.Projection.Units[a.Id].Grants.Count);
    }

    [Fact]
    public void A_per_die_grant_is_consumed_by_the_next_attack_whatever_the_dice()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "6+", [], 3, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Props, new ScriptedDice(2, 2, 2));
        Grant(sim.State, a.Id, "Rending Shots", a.Id);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Empty(sim.Unit(a.Id).Grants);                               // a miss still spends "next time the effect would apply"
    }

    [Fact]
    public void A_grant_whose_conditions_never_hold_is_never_consumed()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Melee("Sword", 1)); var b = Unit("B", "4+", "6+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Props, new ScriptedDice(5, 5, 5, 5));
        Grant(sim.State, a.Id, "Rending Shots", a.Id);                     // shooting only
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [b.Id], GfMelee.MeleeWeapons(sim.State, a.Id), GfNames.Charge));
        Assert.Single(sim.Unit(a.Id).Grants);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~GrantRowTests`
Expected: FAIL. The speed grant survives the move, the Rending grant survives the attack.

- [ ] **Step 3: The live set records what it read**

In `server/game/Effects/LiveProperties.cs`, give `LiveSet` a shared record of read grants. Replace the class header and add the members (keep every existing member):

```csharp
public sealed class LiveSet(Interaction interaction, IReadOnlyList<LiveProperty> live, IReadOnlySet<TargetModifier> suppressedModifiers,
    IReadOnlyList<(LiveProperty Source, Effect Effect)> applied, HashSet<(Guid UnitId, Guid GrantId)>? read = null)
{
    private readonly HashSet<(Guid UnitId, Guid GrantId)> _read = read ?? [];
    /// <summary>Every granted source a consumer used, with the unit holding the grant. A step that acts on this live set emits RollEffects.Consume.</summary>
    public IReadOnlyCollection<(Guid UnitId, Guid GrantId)> GrantsRead => _read;
    public void Read(LiveProperty source) { if (source.GrantId is { } g) _read.Add((Interaction.Of(source.Holder).UnitId, g)); }
```

`WithGranted` passes `_read` as the fifth constructor argument, so a defense plan built on a derived set records into the same one.

In `Settle`, create the set before the loop and record a granted pass-one effect only when it changed something:

```csharp
        var read = new HashSet<(Guid UnitId, Guid GrantId)>();
        void Mark(LiveProperty s) { if (s.GrantId is { } g) read.Add((ix.Of(s.Holder).UnitId, g)); }
```

```csharp
                case EffectKind.SuppressProperty: if (live.RemoveAll(p => p.Holder == side && p.Entry.Name == effect.TargetEntry) > 0) Mark(source); break;
                case EffectKind.ModifyParameter:
                    for (var i = 0; i < live.Count; i++)
                        if (live[i].Holder == side && live[i].Entry.Name == effect.TargetEntry) { live[i] = live[i] with { Parameter = (live[i].Parameter ?? 0) + effect.EffectiveAmount(source.Parameter) }; Mark(source); }
                    break;
                case EffectKind.SuppressModifiers: suppressed.Add(effect.TargetModifier!.Value); if (ix.Defender is not null) Mark(source); break;
```

and `return new LiveSet(ix, live, suppressed, applied, read);`. A `grant_property` is not marked here: the rule it adds carries the grant's id, and that rule's own effects are marked where they are used. So a granted `Regeneration` is spent when it rolls, not by every interaction pass one settles.

- [ ] **Step 4: Each consumer records the granted sources it used**

In `server/game/Effects/RollEffects.cs`:

```csharp
        foreach (var (src, e) in live.Effects(roller, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == roll))
        {
            modifiers.Add(new RollModifier(src.Entry.Name, e.EffectiveAmount(src.Parameter)));
            live.Read(src);
        }
```

In the force-reroll loop, `live.Read(p);` beside `reroll.Add(n);`. In `Multiplier`, `live.Read(src);` inside the loop. `OwnRolls` returns `live.Effects(...)...ToList()` after `foreach (var x in result) live.Read(x.Source);`. Add:

```csharp
    /// <summary>"Next time the effect would apply" for a per-die rule: the attack whose roll it reads, whatever the dice show. Records every granted
    /// source acting for the side with a roll_result condition on this roll whose other conditions hold.</summary>
    public static void ReadPerDie(LiveSet live, Side side, string roll)
    {
        foreach (var p in live.On(side).Where(p => p.GrantId is not null))
            if (p.Effects.Any(e => e.Conditions.Any(c => c.Kind == ConditionKind.RollResult && c.Values.Count == 2 && c.Values[0] == roll)
                    && e.Conditions.Where(c => c.Kind != ConditionKind.RollResult).All(c => Conditions.Holds(live.Interaction, p.Holder, c, p, null))))
                live.Read(p);
    }

    /// <summary>A GrantRemoved for every grant the live set read, and for every other grant on that unit the same spell left (spell library → the grants
    /// a spell left on a unit are consumed together). A grant already gone, or on a unit no longer in play, removes nothing.</summary>
    public static IReadOnlyList<StateChange> Consume(LiveSet live)
    {
        var units = live.Interaction.State.Projection.Units;
        return live.GrantsRead
            .Where(r => units.ContainsKey(r.UnitId))
            .SelectMany(r => units[r.UnitId].Grants.FirstOrDefault(g => g.Id == r.GrantId) is { } read
                ? units[r.UnitId].Grants.Where(g => g.EntryName == read.EntryName).Select(g => (r.UnitId, g.Id)) : [])
            .Distinct()
            .Select(x => (StateChange)new GrantRemoved(x.UnitId, x.Item2)).ToList();
    }
```

Delete `ConsumeGrants`.

In `server/game/Effects/StatReader.cs`, add `live.Read(src);` inside the `ReplaceStat` and `ModifyStat` loops of `TargetNumber`, inside `WeaponRange`'s loop after the range-penalty `continue`, and inside `ActionDistance`'s loop.

- [ ] **Step 5: The steps that act emit the removals**

In `server/game/Rules/GrimdarkFuture/GfAttack.cs`, replace `Consumed`:

```csharp
    /// <summary>The grants this step used (spell library → consumption): every granted modifier in its plans and every granted source it read.</summary>
    static IReadOnlyList<StateChange> Consumed(LiveSet live) => RollEffects.Consume(live);
```

`RollToHitStep` calls `RollEffects.ReadPerDie(live, Side.Attacker, GfNames.Hit);` after its pools are rolled and returns `new Complete(Consumed(live), values)`. `RollToBlockStep` returns `new Complete(Consumed(live), ...)` and calls `RollEffects.ReadPerDie(live, Side.Defender, GfNames.DefenseRoll);` before it. `DetermineAttacksStep` returns its values with `Consumed(live)` as the changes (its `EligibleModels` read the range). In `AssignStep`, replace the line `if (rolled && regen.Source?.GrantId is { } grantId) changes.Add(new GrantRemoved(target, grantId));` with `if (rolled) changes.AddRange(Consumed(live));` (`OwnRolls` read the regeneration source; a roll that did not happen leaves the grant).

In `server/game/Rules/GrimdarkFuture/GfMoves.cs`, `MoveStep`'s `case 0:` that applies a move adds `RollEffects.Consume(live)` to the changes when the move moved anything:

```csharp
                    var (changes, segment, dangerousDice) = Apply(ctx, unit, live, allowance, GfAnswers.Moves(ctx.State, ctx.History.LastClosed("move")!), terrain);
                    if (segment > 0) changes.AddRange(RollEffects.Consume(live));
```

`live` there is the step's own `Lone` set, whose `ActionDistance` read the speed rows.

In `server/game/Rules/GrimdarkFuture/GfMorale.cs`, `TestStep` adds `RollEffects.Consume(live)` to the change list of each of its three outcomes (the morale plan and `OwnRolls` read them).

`GfMelee.ChargeTargets` and `GfShooting.ValidTargets` settle live sets and emit nothing, so offering an action never spends a grant.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS, the built `Ward` and modifier-consumption tests included.

- [ ] **Step 7: Commit**

```bash
git add server/game/Effects server/game/Rules/GrimdarkFuture tests/Tabletop.GameServer.Tests/GrantRowTests.cs
git commit -m "Consume a grant at the first step that would apply it, with the rest of its spell"
```
