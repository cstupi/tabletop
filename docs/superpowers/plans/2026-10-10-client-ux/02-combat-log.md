# Combat log, target number basis and dice tray Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every roll in the play log with its target number's basis, its modifiers, its dice, its rerolls and edits and the
rules that acted, grouped by activation and filterable; the dice tray shows the latest roll the same way.

**Architecture:** The server records where a threshold came from (`ThresholdBasis` on `RollRecord`, written by `Roller`
from `RollPlan`, read by `StatReader`). The client's Godot-free `CombatLog` turns the log into `ActivationBlock`s,
`RollEntry`s and `LineEntry`s; `CombatLogPanel` (in `LogDrawer`) and `DiceTray` only render them.

**Tech Stack:** `net10.0`, C# 12, xUnit, Godot 4.7.2 Mono (C#); the viewer's generated TypeScript.

**Spec:** [`docs/superpowers/specs/2026-10-10-client-ux-and-themes-design.md`](../../specs/2026-10-10-client-ux-and-themes-design.md),
sections *Combat log*, *Target number basis*, *Dice tray*, and `LogFilter` under *Settings*.

**Requires landed:** [`01-collision.md`](01-collision.md), [`01a-collision-server.md`](01a-collision-server.md) and
[`01b-collision-client.md`](01b-collision-client.md), on `main` after `7c4ec5d`.

## Global Constraints

- The client names no rule: `./scripts/check-client` prints nothing. Roll names, stat names and rule names are the server's strings.
- `client/src/Core/` is Godot-free and test-driven; Godot nodes render Core models only.
- `ProtocolVersion.Current` does not change; `RollRecord.Basis` is optional and null in logs written before it.
- After the wire change: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, then `cd viewer && npm run check && npm test`.
- `EventApplier` does not read `Basis`; no viewer agreement port, no fixture regeneration.
- Every file stays under 500 lines.
- Hud colours are literals held in one `static readonly` block at the top of each file; plan 04 (themes) replaces them with theme tokens.
- The Animate checkbox stays in `LogDrawer` until plan 04's Settings dialog takes it.
- `RoundHistory` and `StatSheet` stay; plan 03 (unit card) removes them and feeds `LogDrawer.CombatLog.FocusUnit` from its card.
- Server and sim socket tests need `docker compose up -d`.
- Commit after every task; message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and
  `Claude-Session: https://claude.ai/code/session_01PFUffrX9skMRQz57AE55CW`.

## Review Focus

1. **A basis re-read spends or records again.** `LiveSet.Read` spends grants and records `Applied`; the hit pool's basis is read
   in `roll-to-hit` after `determine-attacks` already accounted the value. Test: `Peeking_a_target_number_spends_and_records_nothing` (Task 2).
2. **`Printed` read from the wrong slot.** A joined hero's own Quality or Defense (p.14) and the morale best-of must report the
   slot the winning value came from. Test: the added assertions in `A_joined_hero_hits_at_its_own_quality_...` (Task 2).
3. **A partial undo inside an activation closes its block.** Only an `Undone` whose `ToSeq` is before the block's start closes it.
   Test: `An_undone_activation_closes_struck_through_and_an_undo_inside_one_keeps_it_open` (Task 4).
4. **A reroll lands on the wrong pool.** A step holds one roll per Quality value; rerolls attach by `RollId`. Test:
   `Rerolls_and_edits_attach_to_the_roll_they_name_and_a_players_modifier_is_its_own_line` (Task 4).
5. **A hand-edited `settings.json` breaks the filter.** An unknown or numeric `logFilter` reads `All`. Test:
   `The_log_filter_and_drawer_state_round_trip_and_an_unknown_filter_reads_all` (Task 3).

## File map

Tasks 4 and 5 are in [`02a-combat-log-client.md`](02a-combat-log-client.md); run the tasks in number order.

| Task | Creates | Modifies |
| --- | --- | --- |
| 1 | — | `libs/Tabletop.Protocol/State.cs`, `tests/Tabletop.Protocol.Tests/WireJsonTests.cs`, `viewer/src/lib/protocol/*.generated.ts` |
| 2 | — | `server/game/Effects/StatReader.cs`, `server/game/Dice/{RollPlan,Roller}.cs`, `server/game/Interpreter/StepContext.cs`, `server/game/Rules/IGameRules.cs`, `server/game/Rules/GrimdarkFuture/GfRolls.cs`, `tests/Tabletop.GameServer.Tests/{GfRollsTests,RollEffectsTests}.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs` |
| 3 | — | `client/src/Core/ClientSettings.cs`, `tests/Tabletop.Client.Tests/SettingsTests.cs` |
| 4 | `client/src/Core/{RollEntry,CombatLog}.cs`, `tests/Tabletop.Client.Tests/CombatLogTests.cs` | `client/src/Core/Names.cs` |
| 5 | `client/src/Hud/CombatLogPanel.cs` | `client/src/Hud/{LogDrawer,StatSheet}.cs`, `client/src/Screens/GameScreen.Play.cs` |
| 6 | — | `client/src/Hud/DiceTray.cs`, `client/src/Screens/GameScreen.Play.cs` |
| 7 | — | `client/AGENTS.md`, `server/game/AGENTS.md`, `libs/Tabletop.Protocol/AGENTS.md`, `docs/design/{client,contracts,interpreter}/design.md` |

`docs/schema.md` documents no roll JSON (`grep -n RollRecord docs/schema.md` finds nothing); it is not touched.

---

## Task 1: `ThresholdBasis` on the roll record

**Files:** Modify `libs/Tabletop.Protocol/State.cs`; test `tests/Tabletop.Protocol.Tests/WireJsonTests.cs`; regenerate `viewer/src/lib/protocol/wire.generated.ts`, `recording.generated.ts`.

**Interfaces:** Consumes `RollRecord(Guid Id, string Roll, int? Threshold, IReadOnlyList<RollModifier> Modifiers, IReadOnlyList<Die> Dice, int? RequiredRaw = null)`. Produces:

```csharp
public sealed record ThresholdBasis(string Stat, int Printed, IReadOnlyList<ThresholdChange> Changes);
public sealed record ThresholdChange(string Source, int? SetTo, int Amount);   // SetTo for replace_stat, Amount for modify_stat
public sealed record RollRecord(Guid Id, string Roll, int? Threshold, IReadOnlyList<RollModifier> Modifiers, IReadOnlyList<Die> Dice, int? RequiredRaw = null, ThresholdBasis? Basis = null);
```

- [ ] **Step 1: Write the failing test** in `WireJsonTests`:

```csharp
    [Fact]
    public void A_roll_carries_its_threshold_basis_and_a_record_written_before_it_reads_none()
    {
        var basis = new ThresholdBasis("Quality", 5, [new ThresholdChange("Reliable", 2, 0), new ThresholdChange("Steady", null, -1)]);
        var with = new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit with { Basis = basis }], []);
        RoundTrips<EventPayload>(with);
        Assert.Contains("\"printed\":5", WireJson.Serialize<EventPayload>(with));
        var old = WireJson.Serialize<EventPayload>(new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], []));
        Assert.DoesNotContain("\"basis\"", old);
        Assert.Null(Assert.IsType<StepCompleted>(WireJson.Deserialize<EventPayload>(old)).Rolls[0].Basis);
    }
```

- [ ] **Step 2:** `dotnet test tests/Tabletop.Protocol.Tests --filter WireJsonTests` — expected: compile error, `ThresholdBasis` does not exist.
- [ ] **Step 3: Implement.** In `State.cs`, above `RollRecord`, add the two records with this summary, and append `ThresholdBasis? Basis = null` to `RollRecord`:

```csharp
/// <summary>Where a threshold came from: the stat it was read from, the value printed in the slot it was read from, and each
/// replace_stat (SetTo) or modify_stat (Amount) row in the order applied. Null for a threshold no stat gave, and in logs written before it.</summary>
```

- [ ] **Step 4:** `dotnet test tests/Tabletop.Protocol.Tests` — expected: PASS.
- [ ] **Step 5: Regenerate** `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, then `cd viewer && npm run check && npm test` — expected: `wire.generated.ts` gains `ThresholdBasis`, `ThresholdChange` and `readonly basis?: ThresholdBasis`; check and tests pass. `dotnet test tests/Tabletop.Viewer.Tests` — expected: PASS.
- [ ] **Step 6: Commit** `git add libs/Tabletop.Protocol/State.cs tests/Tabletop.Protocol.Tests/WireJsonTests.cs viewer/src/lib/protocol && git commit -m "Record a roll's threshold basis on the wire"` with the two trailer lines.

---

## Task 2: The server reads and records the basis

**Files:** Modify `server/game/Effects/StatReader.cs`, `server/game/Dice/RollPlan.cs`, `server/game/Dice/Roller.cs`, `server/game/Interpreter/StepContext.cs`, `server/game/Rules/IGameRules.cs`, `server/game/Rules/GrimdarkFuture/GfRolls.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs`; test `tests/Tabletop.GameServer.Tests/{GfRollsTests,RollEffectsTests}.cs`.

**Interfaces:** Consumes Task 1's `ThresholdBasis`, `ThresholdChange`, `RollRecord.Basis`. Produces:

```csharp
public sealed record TargetRead(int Value, ThresholdBasis? Basis);                                           // StatReader.cs
public static TargetRead StatReader.TargetNumber(LiveSet live, Side side, StatRef stat, Guid? modelId = null); // was int
public static TargetRead StatReader.Peek(LiveSet live, Side side, StatRef stat, Guid? modelId = null);         // spends and records nothing
public sealed record RollSpec(string Roll, RollConvention Convention, Func<LiveSet, Side, TargetRead?> Threshold);
public sealed record RollPlan(..., IReadOnlySet<int>? RerollSuccessfulRaw = null, ThresholdBasis? Basis = null);
public RollPlan StepContext.Plan(LiveSet live, Side roller, string roll, IEnumerable<RollModifier> baseModifiers, int? requiredRaw = null, TargetRead? threshold = null);
```

Every caller that plans a roll: `GfRolls.Spec` (hit, defense, morale by default), `HitPlan`, `DefensePlan`, `MoralePlan`, `CastingPlan`.
The `new RollPlan(...)` sites in `GfMoves`, `GfMelee`, `GfAttack`, `GfAttack.AttackStart`, `GfCasting`, `GfMorale`, the fatigued
`HitPlan` and the roll-off carry no basis: none reads a stat.

- [ ] **Step 1: Write the failing tests** in `GfRollsTests`:

```csharp
    static readonly EntryDocument TestSteady = new("Test Steady", CatalogKind.Unit, false, null, null, null, "Quality +1.", null,
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.ModifyStat, null, new StatRef(StatLevel.Unit, GfNames.Quality), null, null, null, null, null, 1, false, null, null, null, EffectDuration.Interaction, [], [])]);
    static readonly StatRef ModelQuality = new(StatLevel.Model, GfNames.Quality);
    static string Of(ThresholdBasis? b) => b is null ? "none" : string.Join("; ", new[] { $"{b.Stat} {b.Printed}" }.Concat(b.Changes.Select(c => $"{c.Source} {c.SetTo?.ToString() ?? "-"} {c.Amount}")));

    static (StepContext Ctx, LiveSet Live) Engage(Tabletop.Content.Unit attacker, Tabletop.Content.Unit defender, PropertySet props)
    {
        var state = State([attacker], [defender], x1: -6, x2: 6);
        var frame = new FrameView(new Frame(Guid.CreateVersion7(), "x", new SequenceContext(attacker.Id, [defender.Id], [], GfNames.Hold)), 0, new Dictionary<string, int>(), StepHistory.Empty, null);
        var ctx = new StepContext(state, props, Gf, frame, new Roller(new ScriptedDice(4), state.RngSeed, 0));
        return (ctx, ctx.Settle(new Interaction(state, props, Party.Whole(state, attacker.Id), Party.Whole(state, defender.Id), AttackKind.Shooting, GfNames.Hold, 12, [])));
    }

    [Fact]
    public void Thresholds_carry_their_basis()
    {
        var (ctx, live) = Engage(Unit("S", "4+", "4+", [P("Test Steady")], 1, [], Gun("Rifle", "24\"", 1)), Unit("T", "4+", "5+", [], 1, []), TestProperties.WithSpells(TestSteady));
        var hit = GfRolls.HitPlan(ctx, live, AttackKind.Shooting, GfRolls.HitQuality(ctx, live, AttackKind.Shooting, live.Interaction.Attacker.ModelIds[0]));
        Assert.Equal(((int?)5, "Quality 4; Test Steady - 1"), (hit.Threshold, Of(hit.Basis)));                   // modify_stat
        Assert.Same(hit.Basis, ctx.Roll(hit, 1).Basis);                                                          // Roller writes it
        Assert.Equal("Defense 5", Of(GfRolls.DefensePlan(ctx, live, inCover: true, defenderModels: 1).Basis));    // cover is a roll modifier
        Assert.Equal("none", Of(GfRolls.CastingPlan(ctx, live, 0).Basis));                                      // a fixed 4+
        var (rctx, reliable) = Engage(Unit("S", "5+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Reliable"))), Unit("T", "4+", "4+", [], 1, []), Core);
        Assert.Equal("Quality 5; Reliable 2 0", Of(GfRolls.HitPlan(rctx, reliable, AttackKind.Shooting, 2).Basis));   // replace_stat
    }

    [Fact]
    public void Peeking_a_target_number_spends_and_records_nothing()
    {
        var (ctx, live) = Engage(Unit("S", "5+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Reliable"))), Unit("T", "4+", "4+", [], 1, []), Core);
        var model = live.Interaction.Attacker.ModelIds[0];
        Assert.Equal(2, StatReader.Peek(live, Side.Attacker, ModelQuality, model).Value);
        Assert.Null(ctx.Applied.Names);
        Assert.Equal(2, StatReader.TargetNumber(live, Side.Attacker, ModelQuality, model).Value);
        Assert.Equal(["Reliable"], ctx.Applied.Names!);
    }
```

In `A_joined_hero_hits_at_its_own_quality_blocks_on_the_units_until_it_is_alone_and_lends_its_quality_to_morale` (p.14: the
basis's `Printed` is the slot it was read from), add each line after the existing assertion on the same plan:

```csharp
        Assert.Equal(("Quality 3", "Quality 5"), (Of(GfRolls.HitPlan(ctx, shooting, AttackKind.Shooting, 3).Basis), Of(GfRolls.HitPlan(ctx, shooting, AttackKind.Shooting, 5).Basis)));
        Assert.Equal(("Defense 2", "Defense 5"), (Of(GfRolls.DefensePlan(ctx, alone, inCover: false, defenderModels: 1).Basis), Of(GfRolls.DefensePlan(ctx, alone, inCover: false, defenderModels: 2).Basis)));
        Assert.Equal("Quality 3", Of(GfRolls.MoralePlan(ctx, morale).Basis));                                     // the winning hero's
```

- [ ] **Step 2:** `dotnet test tests/Tabletop.GameServer.Tests --filter "FullyQualifiedName~GfRollsTests"` — expected: compile errors (`Basis`, `Peek`, `.Value`).
- [ ] **Step 3: Implement.** `StatReader.cs`: add `TargetRead` above the class; `TargetNumber` and `Peek` call one private reader:

```csharp
    public static TargetRead TargetNumber(LiveSet live, Side side, StatRef stat, Guid? modelId = null) => Read(live, side, stat, modelId, account: true);

    /// <summary>The same read with nothing spent or recorded: the basis of a value an earlier step already accounted (a hit pool's Quality).</summary>
    public static TargetRead Peek(LiveSet live, Side side, StatRef stat, Guid? modelId = null) => Read(live, side, stat, modelId, account: false);

    static TargetRead Read(LiveSet live, Side side, StatRef stat, Guid? modelId, bool account)
    {
        // state, unit and text exactly as TargetNumber reads them today
        var printed = StatValue.Read(StatKind.TargetNumber, text).TargetNumber;
        var value = printed;
        var changes = new List<ThresholdChange>();
        bool Acts(Effect e) => e.TargetStat is { Name: var n, Level: StatLevel.Unit or StatLevel.Model } && n == stat.Name;
        foreach (var (src, e) in live.Effects(side, EffectKind.ReplaceStat).Where(x => Acts(x.Effect)))
        {
            value = StatValue.Read(StatKind.TargetNumber, e.Replacement!).TargetNumber;
            changes.Add(new ThresholdChange(src.Entry.Name, value, 0));
            if (account) live.Read(src);
        }
        foreach (var (src, e) in live.Effects(side, EffectKind.ModifyStat).Where(x => Acts(x.Effect)))
        {
            var amount = e.EffectiveAmount(src.Parameter);
            value += amount;
            changes.Add(new ThresholdChange(src.Entry.Name, null, amount));
            if (account) live.Read(src);
        }
        return new TargetRead(value, new ThresholdBasis(stat.Name, printed, changes));
    }
```

`IGameRules.cs`: `RollSpec.Threshold` becomes `Func<LiveSet, Side, TargetRead?>`. `RollPlan.cs`: append `ThresholdBasis? Basis = null`.
`Roller.Roll`: `new RollRecord(Guid.CreateVersion7(), plan.Roll, plan.Threshold, plan.Modifiers, rolled, plan.RequiredRaw, plan.Basis)`.
`StepContext.Plan`:

```csharp
    public RollPlan Plan(LiveSet live, Side roller, string roll, IEnumerable<RollModifier> baseModifiers, int? requiredRaw = null, TargetRead? threshold = null)
    {
        var spec = Rules.RollSpec(roll);
        var read = threshold ?? spec.Threshold(live, roller);
        var (attributed, rerolls) = RollEffects.Attributed(live, roller, roll, read?.Value, baseModifiers, spec.Convention, requiredRaw, Rules.HitRoll);
        var plan = attributed with { Basis = read?.Basis };   // before keying _rerolls, which compares by reference
        if (rerolls.Count > 0) _rerolls[plan] = (live, rerolls);
        return plan;
    }
```

`GfRolls.cs`: `Spec`'s casting row `(_, _) => new TargetRead(4, null)`; `HitQuality` ends `StatReader.TargetNumber(...).Value`;
`CastingPlan` passes `threshold: new TargetRead(4, null)`; `HitPlan`'s non-fatigued branch passes
`threshold: new TargetRead(quality, HitBasis(live, quality))`; `MoralePlan` keeps the reads whole:

```csharp
    /// <summary>One pool's basis: the first attacking model, in party order, that brings the value. Peeked: determine-attacks accounted it.</summary>
    static ThresholdBasis? HitBasis(LiveSet live, int quality) =>
        live.Interaction.Attacker.ModelIds.Select(m => StatReader.Peek(live, Side.Attacker, ModelQuality, m)).FirstOrDefault(r => r.Value == quality)?.Basis;

    // MoralePlan
        var own = StatReader.TargetNumber(live, Side.Attacker, Quality);
        var heroes = Lookup.LivingModels(ctx.State, unit)
            .Where(m => ctx.Projection.Models[m].Slot.Properties.Any(p => p.Name == GfNames.Hero))
            .Select(m => StatReader.TargetNumber(live, Side.Attacker, ModelQuality, m)).ToList();
        var best = heroes.Prepend(own).MinBy(r => r.Value)!;   // the first minimum: a tie stays the unit's own
        if (best.Value < own.Value) ctx.Applied.Add(GfNames.Hero);
        return ctx.Plan(live, Side.Attacker, GfNames.Morale, [], threshold: best);
```

`DefensePlan` compiles unchanged. `StubRules.RollSpec`: `(_, _) => new TargetRead(4, null)`. `RollEffectsTests`: append `.Value`
to each `StatReader.TargetNumber(...)` used as a number (lines 28, 45, 46, 58–61, 66, 67). `GfRollsTests.Roll_specs_are_per_roll_and_thresholds_are_functions`:
`Threshold(live, side)?.Value` on lines 27–30.

- [ ] **Step 4:** `dotnet build && dotnet test tests/Tabletop.GameServer.Tests` — expected: build clean, all tests PASS (Postgres up).
- [ ] **Step 5: Commit** `git add server/game tests/Tabletop.GameServer.Tests && git commit -m "Write each roll's threshold basis from the stat read"` with the two trailer lines.

---

## Task 3: The log's filter and open state in `ClientSettings`

**Files:** Modify `client/src/Core/ClientSettings.cs`; test `tests/Tabletop.Client.Tests/SettingsTests.cs`.

**Interfaces:** Produces `public enum LogFilter { All, Rolls, Mine, Unit }` (in `ClientSettings.cs`; the order is the filter menu's) and `ClientSettings(..., string GameServerUrl, LogFilter LogFilter = LogFilter.All, bool LogOpen = false)`;
`settings.json` keys `logFilter` (the enum's name) and `logOpen`. Plan 04 (Task 4) adds `Theme`, `CameraFollow`, `TurnBanner`, `RangeRings` as `init` properties after these.

- [ ] **Step 1: Write the failing test** in `SettingsTests`:

```csharp
    [Fact]
    public void The_log_filter_and_drawer_state_round_trip_and_an_unknown_filter_reads_all()
    {
        var path = Path.Combine(_dir, "settings.json");
        var s = ClientSettings.Default with { LogFilter = LogFilter.Unit, LogOpen = true };
        s.Save(path);
        Assert.Equal(s, ClientSettings.Load(path));
        Assert.NotEqual(ClientSettings.Default, s);
        foreach (var json in new[] { """{ "logFilter": "sideways" }""", """{ "logFilter": "7" }""", """{ "animate": false }""" })
        {
            File.WriteAllText(path, json);
            Assert.Equal((LogFilter.All, false), (ClientSettings.Load(path).LogFilter, ClientSettings.Load(path).LogOpen));
        }
    }
```

- [ ] **Step 2:** `dotnet test tests/Tabletop.Client.Tests --filter SettingsTests` — expected: compile error, no `LogFilter` member on `ClientSettings`.
- [ ] **Step 3: Implement.** Add the `LogFilter` enum above the record. Append `LogFilter LogFilter = LogFilter.All, bool LogOpen = false` to the record and
  `string? LogFilter = null, bool? LogOpen = null` to `Dto`. `Load` passes
  `Enum.TryParse<LogFilter>(d.LogFilter, true, out var f) && Enum.IsDefined(f) ? f : Default.LogFilter, d.LogOpen ?? Default.LogOpen`
  (`IsDefined` refuses a numeric string `TryParse` accepts); `Save` writes `LogFilter.ToString(), LogOpen`; `Equals` adds
  `&& LogFilter == other.LogFilter && LogOpen == other.LogOpen` and `GetHashCode` adds `LogFilter`.
- [ ] **Step 4:** `dotnet test tests/Tabletop.Client.Tests` — expected: PASS.
- [ ] **Step 5: Commit** `git add client/src/Core/ClientSettings.cs tests/Tabletop.Client.Tests/SettingsTests.cs && git commit -m "Remember the log's filter and open state"` with the two trailer lines.

---

## Tasks 4 and 5

In [`02a-combat-log-client.md`](02a-combat-log-client.md): Task 4 builds `CombatLog`, `RollEntry` and `ActivationBlock` in Core; Task 5
puts `CombatLogPanel` in the log drawer.

---

## Task 6: The dice tray shows the latest roll entry

**Files:** Modify `client/src/Hud/DiceTray.cs`, `client/src/Screens/GameScreen.Play.cs`.

**Interfaces:** Consumes `CombatLog.Latest`, `CombatLog.Pip`, `RollEntry.{Collapsed, Successes, Dice, Record}` (Task 4), `LogDrawer.OpenAt(Guid)` (Task 5).
Produces `public event Action<Guid>? DiceTray.RollClicked`. `Bind`, `Follow`, `Place` and `_ExitTree` keep their signatures and bodies.
The faces are `CombatLog.Pip` glyphs until plan 04 draws the theme's.

- [ ] **Step 1: Rewrite the rendering.** In `DiceTray.cs` replace the fields, `Render` and `Face` with the members below; `Bind` adds
  `AddChild(_line)` in place of `AddChild(_rolls)` and `_line.Pressed += () => { if (_shown is { } r) RollClicked?.Invoke(r.Record.Id); };`:

```csharp
    static readonly Color Pass = new(0.35f, 0.8f, 0.4f), Fail = new(0.5f, 0.5f, 0.5f), Tumbling = Colors.White;
    const double TumbleSeconds = 0.4;
    Session _s = null!; Control? _below;
    readonly Button _line = new() { Flat = true };
    readonly Random _faces = new();   // decoration while tumbling; the dice shown settled are the record's
    RollEntry? _shown; double _tumble;
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard, _place;

    /// <summary>The shown roll's RollRecord id, when the player clicks it.</summary>
    public event Action<Guid>? RollClicked;

    void Render()
    {
        var latest = _s.Projection is { } p && _s.Game is { } g ? CombatLog.Latest(_s.Log, new Names(g, p)) : null;
        Visible = latest is not null;
        var changed = latest is not null && (_shown is null || _shown.Record.Id != latest.Record.Id || !_shown.Dice.SequenceEqual(latest.Dice));
        _shown = latest;
        if (latest is null) return;
        if (changed) _tumble = _s.Settings.Animate ? TumbleSeconds : 0;
        Paint();
        Place();
    }

    public override void _Process(double delta)
    {
        if (_tumble <= 0) return;
        _tumble -= delta;
        Paint();
    }

    void Paint()
    {
        if (_shown is not { } r) return;
        var settled = _tumble <= 0;
        _line.Text = r.Collapsed(face => CombatLog.Pip(settled ? face : _faces.Next(1, 7)));
        _line.Modulate = !settled ? Tumbling : r.Successes > 0 ? Pass : Fail;
    }
```

  The class summary becomes: *Bottom centre, above the decision box: the latest roll as the combat log's collapsed entry (client UX
  spec → Dice tray). Clicking it opens the log there; with Animate on the faces tumble for 0.4 s before settling.* In
  `GameScreen.Play.cs`, after the `_logDrawer.Bind(...)` line add `_dice.RollClicked += _logDrawer.OpenAt;`.
- [ ] **Step 2:** `dotnet build client/Tabletop.csproj && ./scripts/check-client` — expected: build clean; check-client prints nothing.
- [ ] **Step 3: Run it.** `./scripts/client`: after a roll the tray reads like the log's collapsed entry; with Animate on the faces
  tumble briefly, off they appear settled; a reroll re-tumbles; clicking the tray opens the drawer with that roll expanded and in view.
- [ ] **Step 4: Commit** `git add client/src/Hud/DiceTray.cs client/src/Screens/GameScreen.Play.cs && git commit -m "Show the latest roll entry in the dice tray"` with the two trailer lines.

---

## Task 7: Validate end to end and level the documents

**Files:** Modify `client/AGENTS.md`, `server/game/AGENTS.md`, `libs/Tabletop.Protocol/AGENTS.md`, `docs/design/client/design.md`,
`docs/design/contracts/design.md`, `docs/design/interpreter/design.md`.

- [ ] **Step 1:** `docker compose up -d && dotnet build && dotnet test` — expected: every project PASS.
- [ ] **Step 2:** `./scripts/check-client` — expected: no output. `cd viewer && npm run check && npm test` — expected: PASS.
- [ ] **Step 3: Socket run.** `docker compose restart game` (its `dotnet watch` then serves Task 2's build), then
  `dotnet run --project sim -- suite --driver socket --only core --keep --out recordings/combat-log` — expected: every game passes.
- [ ] **Step 4: Check the records.** Every hit, defense and morale record judged on its modified result carries a basis:

```bash
jq -s '[.[] | .. | objects | select(has("dice") and (.roll == "hit" or .roll == "defense" or .roll == "morale") and (has("requiredRaw") | not))]
  | (length > 0) and all(has("basis"))' recordings/combat-log/*.json
```

  Expected: `true`. On `false`, the same filter with `map(select(has("basis") | not)) | .[0]` names the record to trace.
- [ ] **Step 5: Documents.**
  - `client/AGENTS.md`: the play screen's models gain `CombatLog` (`RollEntry`, `ActivationBlock`, `LogEntry`, `LineEntry`); `src/Hud/`
    gains `CombatLogPanel`; `settings.json` lists `LogFilter` and `LogOpen`; a How-it-works bullet: *The play log is `CombatLog` over
    `Session.Log` in `CombatLogPanel` inside `LogDrawer`; lobby and setup keep `LogPanel`. The dice tray shows `CombatLog.Latest`.*
  - `server/game/AGENTS.md`, How it works: *A threshold read from a stat carries its basis: `StatReader.TargetNumber` returns a
    `TargetRead` (value, printed value, each replace_stat and modify_stat row), `StepContext.Plan` puts its `ThresholdBasis` on the
    `RollPlan` and `Roller` on the `RollRecord`. `StatReader.Peek` reads without spending or recording, for a hit pool's basis in
    `roll-to-hit`. Casting's 4+, fatigue and a property's own roll carry none.*
  - `libs/Tabletop.Protocol/AGENTS.md`, the `State.cs` bullet: *`RollRecord.Basis` (`ThresholdBasis`, `ThresholdChange`) is null for a
    threshold no stat gave and in logs written before it; `EventApplier` does not read it.*
  - `docs/design/contracts/design.md` line 1171: the `RollRecord` row gains `ThresholdBasis? Basis = null`, described as above, with a
    row each for `ThresholdBasis(string Stat, int Printed, IRL<ThresholdChange> Changes)` and `ThresholdChange(string Source, int? SetTo, int Amount)`.
  - `docs/design/interpreter/design.md` line 338: `RollPlan`'s fields end `RerollRaw`, `RerollSuccessfulRaw` and `Basis`, the threshold's
    `ThresholdBasis` from `StatReader`'s `TargetRead`.
  - `docs/design/client/design.md`: the Hud list (line 176) adds `CombatLogPanel`; the `DiceTray` row (698) reads *the latest roll
    as the combat log's collapsed entry; a click opens the log there*; the `LogDrawer` row (699) reads *`CombatLogPanel`: activation
    blocks, roll entries, filters All/Rolls/My units/This unit; 640 px; the Animate toggle until the Settings dialog; G opens it*; the
    log paragraphs (864–871) and the dice readout (992–1000) describe `CombatLog`'s blocks, roll entries and attached rerolls and edits
    in place of `LogPanel`'s one row per event in play.
  - `grep -rn "RequiredRaw = null)" docs/design` — each `RollRecord` signature found gains `, ThresholdBasis? Basis = null`.
- [ ] **Step 6: Commit** `git add client/AGENTS.md server/game/AGENTS.md libs/Tabletop.Protocol/AGENTS.md docs/design && git commit -m "Document the combat log and the threshold basis"` with the two trailer lines.
