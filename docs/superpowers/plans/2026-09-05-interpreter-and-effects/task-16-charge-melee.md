# Task 16: Charge and melee

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfMelee.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`
- Test: `tests/Tabletop.GameServer.Tests/GfMeleeTests.cs`

**Interfaces:**
- Consumes: Tasks 11–15; `PieceSelection`, `SelectedPieces` (Protocol).
- Produces: `GfMelee.Charge()` (`charge`: `activation-terrain` (`GfMoves.ActivationTerrainStep`, p.11's *activated in it*, Task 14), `charge-target`, `move` (`MoveStep(Charge, ignoreSeparation: true)`), `defender-closes`, `fight`), `GfMelee.Melee()` (`melee`: `melee-start`, `counter`, `impact`, `charger-attacks`, `strike-back`, `compare`, `consolidate`); `static IRL<Guid> MeleeWeapons(GameState, Guid unit, Func<Weapon,bool>? filter = null)`; `static int WoundsCaused(StepContext, Guid by, Guid to, string beforeKey, bool attacked)` (the target's `WoundsThisActivation` less the value recorded at `melee-start`, plus `Fear(X)` summed over the attacker's models when it attacked).
- `charge-target`: `PieceSelection(min 1, max 1)` over enemy units with a model within charge distance (`StatReader.ActionDistance(Charge)`, `Fast`/`Slow` included) of a charger model whose straight path crosses no impassable volume (`Movement.Check(...).CrossesImpassable`); none → `Finish` (p.7: a charge needs a target in distance). **A charger inside a container measures from the container instead** — p.15 says *"Units may use Charge actions to disembark"*, and an occupant has no position of its own, so its reach is `GfNames.ExitWithin` from the container it is inside: an enemy within that is one it could be placed fully within 6" of and then be in contact with. The step's own `move` (`MoveStep(Charge, ignoreSeparation: true)`) is then the disembark, since `MoveStep.Containers` offers the exit on a Charge and never the enterable list (Task 14). The mirror also falls out: **a unit inside a container cannot be charged**, because the predicate measures to the target's *living* models and an occupant has none — which is p.15's no line of sight in. `defender-closes`: `MoveRegion(3")` to the defender's team for its models not in base contact (`Distance.EdgeToEdge > 0`) — `Passed` moves nobody. `fight`: no charger model in melee reach → `Finish` (p.7: "the unit may complete its move in any direction and ends its activation"); else `Push(melee, (charger, [target], charger's melee weapons, Charge))`.
- `melee`: `melee-start` records both units' `WoundsThisActivation` (`charger_before`, `defender_before`); `counter` pushes an `attack-sequence` for the defender's `Counter` weapons before anything else (the 09-03 ruling); `impact` — charger models with `Impact(X)`, unless fatigued: `Σ X − (defender models in reach with a Counter weapon)` dice on the effect's threshold, recorded as roll `Impact`, `Values["hits"]`, then `Push(resolve-hits)` when hits > 0; `charger-attacks` pushes the charger's `attack-sequence` and, when it finishes, sets `fatigued` on the charger; `strike-back` — a living defender is asked `Options([strike, pass])`; `strike` pushes the defender's `attack-sequence` (its `Action` null: fatigue and `Furious` read the conditions, not the charge) and then sets `fatigued` on the defender; `compare` — wounds caused each way (`Fear` counting for a unit that attacked), a destroyed unit always loses, the loser pushes `morale-test` with `Action = "melee"`, a tie tests nobody; `consolidate` — one unit destroyed or routed → the other's team gets `MoveRegion(3")`; otherwise the charger's team gets `MoveRegion(1")` to fall back (p.10).

Spec: action sequences → Grimdark Future's sequences (Charge, melee's seven bullets), Rulings (`Counter` before `Impact`); rulebook p.7 (charge distance, path), p.9 (melee reach, striking back, fatigue), p.10 (who won, consolidation), p.13 `Counter`, `Fear`, p.14 `Impact`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfMeleeTests
{
    static Destinations Into(Sim sim, Tabletop.Content.Unit charger, Tabletop.Content.Unit target) =>
        new(charger.Models.Select((m, i) => new ModelRelocation(m.Id, sim.Model(m.Id).Location,
            new OnTable(new Placement(((OnTable)sim.Model(target.Models[Math.Min(i, target.Models.Count - 1)].Id).Location).Placement.Position with { X = ((OnTable)sim.Model(target.Models[0].Id).Location).Placement.Position.X - 1.3 }, new Yaw(0))))).ToList());

    [Fact]
    public void Pages_9_and_10_warriors_charge_dwarves_who_lose_the_melee_and_are_shaken()
    {
        var warriors = Unit("Dynasty Warriors", "4+", "4+", [], 3, [], Melee("CCW", 1));
        var dwarves = Unit("Dwarves", "4+", "4+", [], 4, [], Melee("Hammer", 1));
        // The charger's half is page 9's worked example verbatim — 3 attacks at Quality 4+ rolling 3,4,5 for two hits, Defense 4+ rolling 3,4 for one wound.
        // The rest is constructed. In draw order: charger hits 3,4,5; blocks 3,4; strike-back hits 6,1,1 (one hit); block 5 (blocked); morale 2 (failed).
        var sim = Sim.Gf(State([warriors], [dwarves], x1: -8, x2: 0), 3, 4, 5, 3, 4, 6, 1, 1, 5, 2);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(warriors.Id, [], [], GfNames.Charge));
        var target = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([dwarves.Id], target.Eligible);
        sim.Answer(Samples.Team1, new SelectedPieces([dwarves.Id]));
        Assert.Equal(12.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        sim.Answer(Samples.Team1, Into(sim, warriors, dwarves));
        sim.Answer(Samples.Team2, new Passed());                                                    // no caster, so no checkpoint; the defender declines its 3" close
        Assert.Contains(sim.State.Projection.Stack, f => f.Sequence == GfNames.Melee);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [dwarves.Models[0].Id] = 1 }));   // one dwarf falls
        Assert.Contains(GfNames.Fatigued, sim.Unit(warriors.Id).Conditions);
        Assert.Equal(["strike", "pass"], Assert.IsType<Options>(sim.AskFor(Samples.Team2).Legal).Choices.Select(c => c.Id));
        sim.Answer(Samples.Team2, new Chosen("strike"));                                            // one hit, blocked: no wound to allocate
        var compare = sim.Steps("compare").Single();
        Assert.Equal((1, 0, 2), (compare.Values!["charger_wounds"], compare.Values["defender_wounds"], compare.Values["loser"]));
        Assert.Contains(GfNames.Shaken, sim.Unit(dwarves.Id).Conditions);                           // 3 of 4 alive: Shaken, not routed
        Assert.Equal(1.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);      // nobody destroyed: the charger falls back 1"
        sim.Answer(Samples.Team1, new Passed());
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void Counter_strikes_before_impact_and_reduces_impact_dice_and_fatigue_leaves_only_sixes()
    {
        var riders = Unit("Riders", "4+", "4+", [], 2, [P("Impact", 2)], Melee("Thrust", 1));
        var pikes = Unit("Pikes", "4+", "4+", [], 1, [], Melee("Pike", 1, P("Counter")));
        var sim = Sim.Gf(State([riders], [pikes], x1: -3, x2: 0), 6, 1, 1, 5, 3, 1, 1);           // pike hits 6, rider blocks 1; Impact 1,5,3; pike blocks 1,1
        sim.Start(GfNames.ChargeSeq, new SequenceContext(riders.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([pikes.Id]));
        sim.Answer(Samples.Team1, Into(sim, riders, pikes));
        sim.Answer(Samples.Team2, new Passed());
        var started = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).ToList();
        Assert.Equal(pikes.Id, started[0].Context.ActorUnitId);                                      // Counter first
        sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [riders.Models[0].Id] = 1 }));  // the pike's hit (6) got through (block 1)
        var impact = sim.Steps("impact").First().Rolls.Single();                                     // the step completes again after pushing resolve-hits
        Assert.Equal(("Impact", 2, 3), (impact.Roll, impact.Threshold, impact.Dice.Count));         // 2 × Impact(2) = 4, −1 for the Counter model
        Assert.Equal(2, sim.Steps("impact").First().Values!["hits"]);                               // 1,5,3 → two at 2+
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [pikes.Models[0].Id] = 2 }));   // both hits wound (1,1): the pike dies
        Assert.Equal(0, sim.Alive(pikes.Id));
        Assert.Equal(2, sim.Steps("compare").Single().Values!["loser"]);                            // a destroyed unit always loses; nobody left to test
        Assert.Equal(3.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);    // consolidate 3"
        sim.Answer(Samples.Team1, new Passed());
        Assert.Contains(GfNames.Fatigued, sim.Unit(riders.Id).Conditions);
        // A second melee this round: fatigued, the riders hit only on unmodified 6s.
        var again = Unit("Again", "4+", "4+", [], 2, [], Melee("Club", 1));
        var sim2 = Sim.Gf(State([riders], [again], x1: -3, x2: 0), 5, 6, 1, 1, 1);
        sim2.Append([new ConditionSet(new Subject(SubjectKind.Unit, riders.Id), GfNames.Fatigued, true)]);
        sim2.Start(GfNames.Melee, new SequenceContext(riders.Id, [again.Id], riders.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        var hit = sim2.Steps("roll-to-hit").First().Rolls.Single();
        Assert.Equal((6, [false, true]), (hit.Threshold, hit.Dice.Select(d => d.Success).ToArray()));
        Assert.Empty(sim2.Steps("impact").Single().Rolls);                                          // Impact does not roll when fatigued
    }

    [Fact]
    public void No_target_in_charge_distance_ends_the_activation()
    {
        var u = Unit("U", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -20, x2: 20), 1);
        // sequence_started, then activation-terrain (no dangerous terrain), then charge-target with nothing in range: Finish.
        Assert.Equal(["step_completed", "step_completed", "sequence_finished"], sim.Start(GfNames.ChargeSeq, new SequenceContext(u.Id, [], [], GfNames.Charge)).Skip(1).Select(EventKinds.Of));
    }

    [Fact]
    public void A_charge_disembarks_measuring_from_the_transport_and_a_unit_inside_one_cannot_be_charged()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var riders = Unit("Riders", "4+", "4+", [], 1, [], Melee("Axe", 1));
        var enemy = Unit("Enemy", "4+", "4+", [], 1, [], Melee("Club", 1));
        var truck = Unit("Truck", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var hidden = Unit("Hidden", "4+", "4+", [], 1, [], Melee("Knife", 1));
        // Armies.State rows: team1's bus and riders and team2's enemy, truck and hidden each 12" apart in y; the enemy is the
        // only enemy model within 6" of the bus, and the truck's row is 12" away.
        var sim = Sim.Gf(State([bus, riders], [enemy, truck, hidden], x1: -4, x2: 0), 1, 1, 1, 1);
        var container = bus.Models[0].Id;
        sim.Append([new ModelsMoved([
            new ModelRelocation(riders.Models[0].Id, sim.Model(riders.Models[0].Id).Location, new Inside(container)),
            new ModelRelocation(hidden.Models[0].Id, sim.Model(hidden.Models[0].Id).Location, new Inside(truck.Models[0].Id))])]);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(riders.Id, [], [], GfNames.Charge));
        // p.15: the reach is 6" from the bus, and a unit inside a container of its own is not a target at all.
        var pick = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal([enemy.Id], pick.Eligible);
        sim.Answer(Samples.Team1, new SelectedPieces([enemy.Id]));
        var move = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((container, GfNames.ExitWithin), (move.Containment!.ExitingId!.Value, move.Containment.ExitWithin!.Value));
        Assert.Empty(move.Containment.Enterable);                                                   // p.14: a Charge disembarks; it never embarks
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `GfMelee.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMelee
{
    public static Sequence Charge() => new(GfNames.ChargeSeq, [new GfMoves.ActivationTerrainStep(), new ChargeTargetStep(), new GfMoves.MoveStep(GfNames.Charge, ignoreSeparation: true), new DefenderClosesStep(), new FightStep()]);
    public static Sequence Melee() => new(GfNames.Melee, [new MeleeStartStep(), new CounterStep(), new ImpactStep(), new ChargerAttacksStep(), new StrikeBackStep(), new CompareStep(), new ConsolidateStep()]);

    public static IReadOnlyList<Guid> MeleeWeapons(GameState state, Guid unit, Func<Weapon, bool>? filter = null) =>
        Lookup.LivingModels(state, unit).SelectMany(m => state.Projection.Models[m].Slot.Weapons).Where(w => !w.Stats.ContainsKey(GfNames.Range) && (filter?.Invoke(w) ?? true)).Select(w => w.Id).ToList();

    static Guid Charger(StepContext ctx) => ctx.Frame.Context.ActorUnitId!.Value;
    static Guid Defender(StepContext ctx) => ctx.Frame.Context.TargetUnitIds[0];
    /// <summary>By step name, not by index: charge's first step is the activation's dangerous terrain test (p.11).</summary>
    static Guid Target(StepContext ctx) => ((SelectedPieces)ctx.Frame.Frame.Steps.Last(s => s.Step == "charge-target").Answer!).PieceIds[0];
    static bool InReach(GameState state, Guid a, Guid b) => Lookup.LivingModels(state, a).Any(m => Lookup.LivingModels(state, b).Any(t => Battlefield.Distance(state, m, t) <= GfNames.MeleeHorizontal && Battlefield.Vertical(state, m, t) <= GfNames.MeleeVertical));
    static StepOutcome AfterChild(StepContext ctx, Func<StepOutcome> push, IReadOnlyList<StateChange>? onDone = null) =>
        ctx.History.Children.Count == 0 ? push() : ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned ? new Complete(onDone ?? []) : new OpenAsks([]);
    static IReadOnlyList<StateChange> Fatigue(Guid unit) => [new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Fatigued, true)];

    /// <summary>Wounds the target took since melee-start, plus Fear(X) over the attacker's models when it attacked (p.13).</summary>
    public static int WoundsCaused(StepContext ctx, Guid by, Guid to, string beforeKey, bool attacked)
    {
        var wounds = ctx.Projection.Units[to].WoundsThisActivation - ctx.Frame.Value(beforeKey);
        var fear = attacked ? Lookup.LivingModels(ctx.State, by).Sum(m => Lookup.ModelParameter(ctx.State, m, "Fear") ?? 0) : 0;
        return wounds + fear;
    }

    sealed class ChargeTargetStep : IStep
    {
        public string Name => "charge-target";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var unit = Charger(ctx);
            var live = LiveProperties.Settle(new Interaction(ctx.State, ctx.Properties, Party.Whole(ctx.State, unit), null, AttackKind.Melee, GfNames.Charge, null, []));
            var distance = StatReader.ActionDistance(live, Side.Attacker, GfNames.Charge);
            var terrain = Battlefield.Terrain(ctx.State);
            var impassable = terrain.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();
            var team = ctx.ActorTeam;
            // p.15: "Units may use Charge actions to disembark". An occupant has no position, so its reach is the exit radius from
            // the container it is inside — anything it could be placed fully within 6" of is a unit it could then be in contact with.
            var inside = Lookup.ContainerOf(ctx.State, unit);
            var targets = ctx.Projection.Units.Values.Where(u => ctx.TeamOf(u.Id) != team).Select(u => u.Id).Where(t => inside is { } container
                ? Lookup.LivingModels(ctx.State, t).Any(e => Battlefield.Distance(ctx.State, container, e) <= GfNames.ExitWithin)
                : Lookup.LivingModels(ctx.State, unit).Any(m => Lookup.LivingModels(ctx.State, t).Any(e =>
                {
                    var (from, to) = (Battlefield.Placed(ctx.State, m)!.Value, Battlefield.Placed(ctx.State, e)!.Value);
                    var check = Movement.Check(from, to.Placement.Position.Horizontal, distance, impassable, [], null, ctx.State.Board.Table.Size);
                    return Battlefield.Distance(ctx.State, m, e) <= distance && !check.CrossesImpassable;
                }))).ToList();
            if (targets.Count == 0) return new Finish([]);
            return new OpenAsks([ctx.Ask("charge_target", "Charge which unit?", team!.Value, new PieceSelection(targets, 1, 1))]);
        }
    }

    /// <summary>p.9: the target's models not in base contact move up to 3" into contact — their models, their Ask.</summary>
    sealed class DefenderClosesStep : IStep
    {
        public string Name => "defender-closes";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, target) = (Charger(ctx), Target(ctx));
            if (ctx.History.LastClosed() is { } closed)
            {
                var moves = (closed.Single as Destinations)?.Moves.Where(m => m.To is OnTable).Select(m => new ModelRelocation(m.ModelId, m.From, new OnTable(Battlefield.Settle(ctx.State, ((OnTable)m.To).Placement)))).ToList() ?? [];
                return new Complete(moves.Count > 0 ? [new ModelsMoved(moves)] : []);
            }
            var free = Lookup.LivingModels(ctx.State, target).Where(t => Lookup.LivingModels(ctx.State, charger).All(m => Battlefield.Distance(ctx.State, m, t) > 0)).ToList();
            if (free.Count == 0 || ctx.TeamOf(target) is not { } team) return new Complete([]);
            return new OpenAsks([ctx.Ask("move", $"Move up to {GfNames.ChargeCloseIn}\" into contact", team, new MoveRegion(free, GfNames.ChargeCloseIn, [], [], null), allowPass: true)]);   // a close-in: no terrain parameters, no cap
        }
    }

    sealed class FightStep : IStep
    {
        public string Name => "fight";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, target) = (Charger(ctx), Target(ctx));
            if (ctx.History.Children.Count == 0 && !InReach(ctx.State, charger, target)) return new Finish([]);
            return AfterChild(ctx, () => new Push(GfNames.Melee, new SequenceContext(charger, [target], MeleeWeapons(ctx.State, charger), GfNames.Charge)));
        }
    }

    sealed class MeleeStartStep : IStep
    {
        public string Name => "melee-start";
        public StepOutcome Run(StepContext ctx) => new Complete([], new Dictionary<string, int>
            { ["charger_before"] = ctx.Projection.Units[Charger(ctx)].WoundsThisActivation, ["defender_before"] = ctx.Projection.Units[Defender(ctx)].WoundsThisActivation });
    }

    sealed class CounterStep : IStep
    {
        public string Name => "counter";
        public StepOutcome Run(StepContext ctx)
        {
            var counters = MeleeWeapons(ctx.State, Defender(ctx), w => w.Properties.Any(p => p.Name == "Counter"));
            if (counters.Count == 0) return new Complete([]);
            return AfterChild(ctx, () => new Push(GfNames.AttackSequence, new SequenceContext(Defender(ctx), [Charger(ctx)], counters, null)), Fatigue(Defender(ctx)));
        }
    }

    /// <summary>p.14's `Impact(X)` and p.13's `Counter`: X dice per Impact(X) model, unless fatigued, less one per defending model with Counter; hits at the effect's threshold. Two phases: roll (Repeat), then push resolve-hits.</summary>
    sealed class ImpactStep : IStep
    {
        public string Name => "impact";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.Frame.Value("impact_phase") == 1)
                return AfterChild(ctx, () => new Push(GfNames.ResolveHits, new SequenceContext(charger, [defender], [], GfNames.Charge)));
            var live = LiveProperties.Settle(GfAttack.AttackInteraction(ctx, charger, defender, [], AttackKind.Melee, GfNames.Charge));
            var impacts = RollEffects.OwnRolls(live, Side.Attacker, c => c.Kind == EffectKind.AddHit);   // Impact's conditions (charging, not fatigued) were judged in pass one's read
            if (impacts.Count == 0) return new Complete([], new Dictionary<string, int> { ["hits"] = 0 });
            var counterModels = Lookup.LivingModels(ctx.State, defender).Count(m => ctx.Projection.Models[m].Slot.Weapons.Any(w => w.Properties.Any(p => p.Name == "Counter")));
            var dice = Math.Max(0, impacts.Sum(x => x.Roll.EffectiveAmount(x.Source.Parameter)) - counterModels);
            var hits = dice == 0 ? 0 : Dice.RollOutcome.Successes(ctx.Roll(new Dice.RollPlan(impacts[0].Source.Entry.Name, impacts[0].Roll.Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), dice));
            return new Complete([], new Dictionary<string, int> { ["hits"] = hits, ["impact_phase"] = 1 }, Repeat: hits > 0);
        }
    }
```

`RollEffects.OwnRolls` returns only rolls whose conditions hold now (`LiveSet.Effects` judges them), so a fatigued charger has no live `Impact` roll and the step records zero hits without drawing a die.


```csharp
    sealed class ChargerAttacksStep : IStep
    {
        public string Name => "charger-attacks";
        public StepOutcome Run(StepContext ctx) =>
            AfterChild(ctx, () => new Push(GfNames.AttackSequence, new SequenceContext(Charger(ctx), [Defender(ctx)], ctx.Frame.Context.WeaponIds, GfNames.Charge)), Fatigue(Charger(ctx)));
    }

    /// <summary>
    /// p.9: the defender may strike back (its Ask), and doing so fatigues it; a destroyed defender cannot. A **Shaken** defender may strike
    /// back too, and it is fatigued for doing so: 3.5.1 p.10 reads "must stay idle, but may strike back counting as fatigued".
    /// No Shaken guard belongs here.
    /// </summary>
    sealed class StrikeBackStep : IStep
    {
        public string Name => "strike-back";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (Lookup.LivingModels(ctx.State, defender).Count == 0 || Lookup.LivingModels(ctx.State, charger).Count == 0) return new Complete([]);
            if (ctx.History.Children.Count > 0) return AfterChild(ctx, () => throw new UnreachableException(), Fatigue(defender));
            if (ctx.History.LastClosed() is { } closed)
                return closed.Single is Chosen { OptionId: "strike" } ? new Push(GfNames.AttackSequence, new SequenceContext(defender, [charger], MeleeWeapons(ctx.State, defender), null)) : new Complete([]);
            return new OpenAsks([ctx.Ask("strike_back", "Strike back?", ctx.TeamOf(defender)!.Value, new Options([new AskOption("strike", "Strike back", [defender]), new AskOption("pass", "Don't", [])]))]);
        }
    }

    /// <summary>p.10: sum wounds caused each way; a destroyed unit always loses; the loser tests morale; a tie tests nobody. Values: charger_wounds, defender_wounds, loser (0 none, 1 charger, 2 defender).</summary>
    sealed class CompareStep : IStep
    {
        public string Name => "compare";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.Frame.Value("compared") == 1)   // second run: the loser tests morale on the melee path
            {
                var loserUnit = ctx.Frame.Value("loser") == 1 ? charger : defender;
                return AfterChild(ctx, () => new Push(GfNames.MoraleTest, new SequenceContext(loserUnit, [], [], "melee")));
            }
            var struck = ctx.Frame.Frame.Steps.Any(s => s.Step == "strike-back" && s.Answer is Chosen { OptionId: "strike" }) || ctx.Frame.Frame.Steps.Any(s => s.Step == "counter" && s.Changes.Count > 0);
            var chargerWounds = WoundsCaused(ctx, charger, defender, "defender_before", attacked: true);
            var defenderWounds = WoundsCaused(ctx, defender, charger, "charger_before", attacked: struck);
            var chargerDead = Lookup.LivingModels(ctx.State, charger).Count == 0; var defenderDead = Lookup.LivingModels(ctx.State, defender).Count == 0;
            var loser = defenderDead ? 2 : chargerDead ? 1 : chargerWounds > defenderWounds ? 2 : defenderWounds > chargerWounds ? 1 : 0;
            var values = new Dictionary<string, int> { ["charger_wounds"] = chargerWounds, ["defender_wounds"] = defenderWounds, ["loser"] = loser, ["compared"] = 1 };
            var testing = loser == 1 ? charger : loser == 2 ? defender : (Guid?)null;
            return new Complete([], values, Repeat: testing is not null && Lookup.LivingModels(ctx.State, testing.Value).Count > 0);
        }
    }

    /// <summary>p.10: a destroyed or routed unit lets the other move 3"; otherwise the charger moves back 1".</summary>
    sealed class ConsolidateStep : IStep
    {
        public string Name => "consolidate";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            if (ctx.History.LastClosed() is { } closed)
            {
                var moves = (closed.Single as Destinations)?.Moves.Where(m => m.To is OnTable).Select(m => new ModelRelocation(m.ModelId, m.From, new OnTable(Battlefield.Settle(ctx.State, ((OnTable)m.To).Placement)))).ToList() ?? [];
                return new Complete(moves.Count > 0 ? [new ModelsMoved(moves)] : []);
            }
            var (chargerAlive, defenderAlive) = (Lookup.LivingModels(ctx.State, charger), Lookup.LivingModels(ctx.State, defender));
            if (chargerAlive.Count == 0 && defenderAlive.Count == 0) return new Complete([]);
            var (mover, distance) = chargerAlive.Count == 0 ? (defender, GfNames.Consolidate) : defenderAlive.Count == 0 ? (charger, GfNames.Consolidate) : (charger, GfNames.FallBack);
            return new OpenAsks([ctx.Ask("move", $"Move up to {distance}\"", ctx.TeamOf(mover)!.Value, new MoveRegion(Lookup.LivingModels(ctx.State, mover), distance, [], [], null), allowPass: true)]);   // fall back / consolidate: no terrain parameters, no cap
        }
    }
}
```

`UnreachableException` is `System.Diagnostics.UnreachableException`: in `StrikeBackStep` the push lambda is never invoked when a child already exists. `GfRules._sequences` gains `GfMelee.Charge()` and `GfMelee.Melee()`.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfMeleeTests`; expect 4 passed. Commit: `Add charge and melee: Counter, Impact, strike back, who won, consolidation`.**
