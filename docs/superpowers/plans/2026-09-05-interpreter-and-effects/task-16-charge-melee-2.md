# Task 16: Charge and melee (continued from part 1)

- [ ] **Step 2: Run; expect compile errors. Write `GfMelee.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static class GfMelee
{
    public static Sequence Charge() => new(GfNames.ChargeSeq, [new GfMoves.ActivationTerrainStep(), new ChargeTargetStep(), new GfMoves.MoveStep(GfNames.Charge, ignoreSeparation: true), new DefenderClosesStep(), new FightStep()]);
    public static Sequence Melee() => new(GfNames.Melee, [new MeleeStartStep(), new CounterStep(), new ImpactStep(), new ChargerAttacksStep(), new StrikeBackStep(), new CompareStep(), new ConsolidateStep()]);

    public static IReadOnlyList<Guid> MeleeWeapons(GameState state, Guid unit, Func<Weapon, bool>? filter = null) =>
        Lookup.LivingModels(state, unit).SelectMany(m => state.Projection.Models[m].Slot.Weapons).Where(w => !w.Stats.ContainsKey(GfNames.Range) && (filter?.Invoke(w) ?? true)).Select(w => w.Id).ToList();

    const double Touching = 1e-6;   // "not in base contact": farther apart than float noise
    static bool IsCounter(Weapon w) => w.Properties.Any(p => p.Name == "Counter");
    static bool CounterStruck(StepContext ctx) => ctx.Frame.Frame.Steps.Any(s => s.Step == "counter" && s.Values?.GetValueOrDefault(StruckKey) == 1);
    static Guid Charger(StepContext ctx) => ctx.Frame.Context.ActorUnitId!.Value;
    static Guid Defender(StepContext ctx) => ctx.Frame.Context.TargetUnitIds[0];
    /// <summary>By step name, not by index: charge's first step is the activation's dangerous terrain test (p.11).</summary>
    static Guid? Target(StepContext ctx) => GfAnswers.Pieces(ctx.State, ctx.Frame, "charge-target", "charge_target") is [var target, ..] ? target : null;
    static bool InReach(GameState state, Guid a, Guid b) => Lookup.LivingModels(state, a).Any(m => Lookup.LivingModels(state, b).Any(t => Battlefield.Distance(state, m, t) <= GfNames.MeleeHorizontal && Battlefield.Vertical(state, m, t) <= GfNames.MeleeVertical));
    static StepOutcome AfterChild(StepContext ctx, Func<StepOutcome> push, IReadOnlyList<StateChange>? onDone = null) =>
        ctx.History.Children.Count == 0 ? push() : ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned ? new Complete(onDone ?? []) : new OpenAsks([]);

    /// <summary>The melee weapon groups of the given weapon ids (p.9's pool is one attack per weapon type, as p.8's shooting is), in GfGroups' order. Read from every model's slot, so the list is the same on each run of a step.</summary>
    static IReadOnlyList<WeaponGroup> GroupsOf(GameState state, IReadOnlyList<Guid> ids) =>
        GfGroups.Of(state, state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).Where(w => ids.Contains(w.Id)));

    /// <summary>
    /// p.9: "Sum the Attack value from the weapons of all models that can strike ... how many attacks the unit has in total for this
    /// melee." Two phases, as `fire-groups`: the first run records every group's attacks per Quality value (`attacks_&lt;i&gt;_q&lt;n&gt;`, with
    /// `attacks_&lt;i&gt;` the total) and the defending unit's living models (`defender_models_&lt;i&gt;`, p.14's Hero clause) and repeats; the
    /// repeat pushes the groups, and determine-attacks reads the recorded counts, so an earlier group's casualties do not shorten a
    /// later one. `values` rides both completions.
    /// </summary>
    static StepOutcome Strike(StepContext ctx, string step, IReadOnlyList<WeaponGroup> groups, Guid attacker, Guid target, string? action, IReadOnlyList<StateChange>? onDone = null, IReadOnlyDictionary<string, int>? values = null)
    {
        if (Recorded(ctx, step)) return EachGroup(ctx, groups, attacker, target, action, onDone, values);
        if (groups.Count == 0) return new Complete(onDone ?? [], values);
        var settled = values is null ? new Dictionary<string, int>() : new Dictionary<string, int>(values);
        var defenderModels = Lookup.LivingModels(ctx.State, target).Count;
        foreach (var (g, i) in groups.Select((g, i) => (g, i)))
        {
            var byQuality = GfAttack.AttacksAgainst(ctx, attacker, target, g.WeaponIds, AttackKind.Melee, action);
            settled[$"attacks_{i}"] = byQuality.Sum(x => x.Attacks);
            foreach (var (quality, attacks) in byQuality) settled[$"attacks_{i}_q{quality}"] = attacks;
            settled[$"defender_models_{i}"] = defenderModels;
        }
        return new Complete([], settled, Repeat: true);
    }

    /// <summary>The step's last completion on this frame was its own recording run: the groups are being pushed.</summary>
    static bool Recorded(StepContext ctx, string step) =>
        ctx.Frame.Frame.Steps is [.., var last] && last.Step == step && (last.Values?.ContainsKey(StepRepeat.Key) ?? false);

    /// <summary>
    /// Pushes one attack-sequence per group in turn (the pushed count is the number of children, so the step is stateless and replayable),
    /// then completes with `onDone`. A target that no model survives ends the run: later groups have nothing to strike.
    /// </summary>
    static StepOutcome EachGroup(StepContext ctx, IReadOnlyList<WeaponGroup> groups, Guid attacker, Guid target, string? action, IReadOnlyList<StateChange>? onDone, IReadOnlyDictionary<string, int>? values)
    {
        var pushed = ctx.History.Children.Count;
        if (pushed > 0 && !(ctx.History.Children[^1].Finished || ctx.History.Children[^1].Abandoned)) return new OpenAsks([]);
        if (pushed < groups.Count && (pushed == 0 || Lookup.LivingModels(ctx.State, target).Count > 0))
            return new Push(GfNames.AttackSequence, new SequenceContext(attacker, [target], groups[pushed].WeaponIds, action));
        return new Complete(onDone ?? [], values);
    }
    static IReadOnlyList<StateChange> Fatigue(Guid unit) => [new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Fatigued, true)];

    /// <summary>The settled move answer: every model that went on the table lands on the surface. Shared by the defender's close-in and consolidation.</summary>
    static StepOutcome Settled(StepContext ctx, AskRecord closed)
    {
        var moves = GfAnswers.Moves(ctx.State, closed).Where(m => m.To is OnTable).Select(m => new ModelRelocation(m.ModelId, m.From, new OnTable(Battlefield.Settle(ctx.State, ((OnTable)m.To).Placement)))).ToList();
        return new Complete(moves.Count > 0 ? [new ModelsMoved(moves)] : []);
    }

    /// <summary>Wounds the target took since melee-start, plus Fear(X) over the attacker's models when it attacked (p.13).</summary>
    public static int WoundsCaused(StepContext ctx, Guid by, Guid to, string beforeKey, bool attacked)
    {
        var wounds = ctx.Projection.Units[to].WoundsThisActivation - ctx.Frame.Value(beforeKey);
        var fear = attacked ? Lookup.LivingModels(ctx.State, by).Sum(m => Lookup.ModelParameter(ctx.State, m, "Fear") ?? 0) : 0;
        return wounds + fear;
    }

    /// <summary>
    /// The enemy units this unit could charge: p.7 "Units may only take a Charge action if at least one model is within charge distance of the
    /// target unit (including any additional movement from special rules)". Read by ActionsFor to offer Charge at all, and by charge-target.
    /// </summary>
    public static IReadOnlyList<Guid> ChargeTargets(GameState state, PropertySet props, Guid unit)
    {
        var live = LiveProperties.Settle(new Interaction(state, props, Party.Whole(state, unit), null, AttackKind.Melee, GfNames.Charge, null, []));
        var distance = StatReader.ActionDistance(live, Side.Attacker, GfNames.Charge);
        var impassable = Battlefield.Terrain(state).Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();
        var team = Teams.Of(state, unit);
        // p.15: "Units may use Charge actions to disembark". An occupant has no position, so its reach is the exit radius from
        // the container it is inside — anything it could be placed fully within 6" of is a unit it could then be in contact with.
        var inside = Lookup.ContainerOf(state, unit);
        return state.Projection.Units.Values.Where(u => Teams.Of(state, u.Id) != team).Select(u => u.Id).Where(t => inside is { } container
            ? Lookup.LivingModels(state, t).Any(e => Battlefield.Distance(state, container, e) <= GfNames.ExitWithin)
            : Lookup.LivingModels(state, unit).Any(m => Lookup.LivingModels(state, t).Any(e =>
            {
                if (Battlefield.Placed(state, m) is not { } from || Battlefield.Placed(state, e) is not { } to) return false;
                var check = Movement.Check(from, to.Placement.Position.Horizontal, distance, impassable, [], null, state.Board.Table.Size);
                return Battlefield.Distance(state, m, e) <= distance && !check.CrossesImpassable;
            }))).ToList();
    }

    sealed class ChargeTargetStep : IStep
    {
        public string Name => "charge-target";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.LastClosed() is { } answered) return GfAnswers.Pieces(answered.Single, answered.Ask).Count == 0 ? new Finish([]) : new Complete([]);   // an answer that names no offered unit is no target
            var unit = Charger(ctx);
            var team = ctx.ActorTeam;
            var targets = ChargeTargets(ctx.State, ctx.Properties, unit);
            // p.7: "if being within charge distance becomes impossible (due to killed models, special rules, etc.)" after the action was taken.
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
            if (Target(ctx) is not { } target) return new Finish([]);
            var charger = Charger(ctx);
            if (ctx.History.LastClosed() is { } closed) return Settled(ctx, closed);
            var free = Lookup.LivingModels(ctx.State, target).Where(t => Lookup.LivingModels(ctx.State, charger).All(m => Battlefield.Distance(ctx.State, m, t) > Touching)).ToList();
            if (free.Count == 0 || ctx.TeamOf(target) is not { } team) return new Complete([]);
            return new OpenAsks([ctx.Ask("move", $"Move up to {GfNames.ChargeCloseIn}\" into contact", team, new MoveRegion(free, GfNames.ChargeCloseIn, [], [], null), allowPass: true)]);   // a close-in: no terrain parameters, no cap
        }
    }

    sealed class FightStep : IStep
    {
        public string Name => "fight";
        public StepOutcome Run(StepContext ctx)
        {
            if (Target(ctx) is not { } target) return new Finish([]);
            var charger = Charger(ctx);
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

    const string StruckKey = "struck";

    sealed class CounterStep : IStep
    {
        public string Name => "counter";
        public StepOutcome Run(StepContext ctx)
        {
            var counters = MeleeWeapons(ctx.State, Defender(ctx), IsCounter);
            if (counters.Count == 0) return new Complete([]);
            // p.13: Counter moves the weapon's strike first; the defender's fatigue is set once, at the end of strike-back.
            return Strike(ctx, Name, GroupsOf(ctx.State, counters), Defender(ctx), Charger(ctx), null, values: new Dictionary<string, int> { [StruckKey] = 1 });
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
            Strike(ctx, Name, GroupsOf(ctx.State, ctx.Frame.Context.WeaponIds), Charger(ctx), Defender(ctx), GfNames.Charge, Fatigue(Charger(ctx)));
    }

    /// <summary>
    /// p.9: the defender may strike back (its Ask), and doing so fatigues it; a destroyed defender cannot. A **Shaken** defender may strike
    /// back too, and it is fatigued for doing so: 3.5.1 p.10 reads "must stay idle, but may strike back counting as fatigued"
    /// (GfRolls.Fatigued reads the Shaken condition as fatigue). No Shaken guard belongs here.
    /// </summary>
    sealed class StrikeBackStep : IStep
    {
        public string Name => "strike-back";
        public StepOutcome Run(StepContext ctx)
        {
            var (charger, defender) = (Charger(ctx), Defender(ctx));
            var counterStruck = CounterStruck(ctx);
            var weapons = MeleeWeapons(ctx.State, defender, w => !IsCounter(w));   // p.13: the Counter weapon's strike already happened in `counter`
            // Striking under way comes before the living-model checks: a strike back that destroys the charger still fatigues (p.9).
            if (Recorded(ctx, Name)) return Strike(ctx, Name, GroupsOf(ctx.State, weapons), defender, charger, null, Fatigue(defender));
            if (Lookup.LivingModels(ctx.State, defender).Count == 0) return new Complete([]);
            if (Lookup.LivingModels(ctx.State, charger).Count == 0) return new Complete(counterStruck ? Fatigue(defender) : []);
            if (ctx.History.LastClosed("strike_back") is { } closed)
                return closed.Single is Chosen { OptionId: "strike" } ? Strike(ctx, Name, GroupsOf(ctx.State, weapons), defender, charger, null, Fatigue(defender)) : new Complete(counterStruck ? Fatigue(defender) : []);
            if (weapons.Count == 0) return new Complete(counterStruck ? Fatigue(defender) : []);
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
            var struck = ctx.Frame.Frame.Steps.Any(s => s.Step == "strike-back" && s.Answer is Chosen { OptionId: "strike" }) || CounterStruck(ctx);
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
            if (ctx.History.LastClosed() is { } closed) return Settled(ctx, closed);
            var (chargerAlive, defenderAlive) = (Lookup.LivingModels(ctx.State, charger), Lookup.LivingModels(ctx.State, defender));
            if (chargerAlive.Count == 0 && defenderAlive.Count == 0) return new Complete([]);
            var (mover, distance) = chargerAlive.Count == 0 ? (defender, GfNames.Consolidate) : defenderAlive.Count == 0 ? (charger, GfNames.Consolidate) : (charger, GfNames.FallBack);
            return new OpenAsks([ctx.Ask("move", $"Move up to {distance}\"", ctx.TeamOf(mover)!.Value, new MoveRegion(Lookup.LivingModels(ctx.State, mover), distance, [], [], null), allowPass: true)]);   // fall back / consolidate: no terrain parameters, no cap
        }
    }
}
```

`GfRules._sequences` gains `GfMelee.Charge()` and `GfMelee.Melee()`.

- [ ] **Step 3: Write `tests/Tabletop.GameServer.Tests/GfGroupsTests.cs`**

Shooting and melee share `GfGroups.Of`: one `attack-sequence` per weapon type, so a weapon's `AP` and `Deadly` stay in its own group, in the order Takedown, Deadly, the rest. A `Limited` weapon fired by this shooting does not drop the groups after it, because `fire-groups` asks `GfShooting.Groups` with its own frame; a later shooting excludes it.

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Weapon groups: one attack-sequence per weapon type in melee as in shooting (p.8, p.9), ordered Takedown, then Deadly, then the rest (p.14, p.13).</summary>
public class GfGroupsTests
{
    static SequenceContext MeleeOf(Tabletop.Content.Unit attacker, Tabletop.Content.Unit target) =>
        new(attacker.Id, [target.Id], attacker.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge);

    [Fact]
    public void A_melee_weapons_ap_and_deadly_apply_only_to_its_own_group()
    {
        // p.13 AP applies "when blocking hits from weapons with this special rule"; Deadly multiplies "each wound" of the weapon that has it.
        var attacker = Unit("A", "4+", "4+", [], 1, [], Melee("CCW", 1), Melee("Fist", 1, P("AP", 2), P("Deadly", 3)));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Tough", 6)]);
        // Deadly resolves first: Fist hits 6, blocks 5 against 6+ (AP 2) and fails; CCW hits 6, blocks 4 against 4+ and holds. Then morale dice.
        var sim = Sim.Gf(State([attacker], [defender], x1: -3, x2: 0), 6, 5, 6, 4, 6, 6);
        sim.Start(GfNames.Melee, MeleeOf(attacker, defender));
        var blocks = sim.Steps("roll-to-block").Select(s => s.Rolls.Single()).ToList();
        Assert.Contains(blocks[0].Modifiers, m => m.Source == "AP" && m.Amount == -2);
        Assert.DoesNotContain(blocks[1].Modifiers, m => m.Source == "AP");                          // the plain weapon's hit blocks with no AP
        Assert.Equal(3, sim.Model(defender.Models[0].Id).Wounds);                                   // only the Fist's wound is multiplied
        Assert.Equal(2, sim.Emitted.OfType<SequenceStarted>().Count(e => e.Sequence == GfNames.AttackSequence));
        Assert.Contains(GfNames.Fatigued, sim.Unit(attacker.Id).Conditions);                        // once, after the last group
    }

    [Fact]
    public void Melee_groups_go_takedown_then_deadly_then_the_rest_and_a_takedown_group_picks_its_model()
    {
        var attacker = Unit("A", "4+", "4+", [], 1, [], Melee("Aardvark", 1), Melee("Zed", 1, P("Deadly", 3)), Melee("Yak", 1, P("Takedown")));
        var defender = Unit("D", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([attacker], [defender], x1: -3, x2: 0), 1);
        sim.Start(GfNames.Melee, MeleeOf(attacker, defender));
        var ask = sim.AskFor(Samples.Team1);
        Assert.IsType<PieceSelection>(ask.Legal);
        sim.Answer(Samples.Team1, new SelectedPieces([defender.Models[1].Id]));
        var names = sim.Emitted.OfType<SequenceStarted>().Where(e => e.Sequence == GfNames.AttackSequence)
            .Select(e => attacker.Models[0].Weapons.Single(w => w.Id == e.Context.WeaponIds.Single()).Name).ToList();
        Assert.Equal(["Yak", "Zed", "Aardvark"], names);
    }

    [Fact]
    public void Shooting_groups_go_takedown_then_deadly_then_the_rest()
    {
        var shooter = Unit("S", "4+", "4+", [], 1, [], Gun("Aardvark", "24\"", 1), Gun("Zed", "24\"", 1, P("Deadly", 3)));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([shooter], [t], x1: -5, x2: 5), 1);
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Aardvark"] = t.Id, ["Zed"] = t.Id }));
        var first = sim.Emitted.OfType<SequenceStarted>().First(e => e.Sequence == GfNames.AttackSequence);
        Assert.Equal("Zed", shooter.Models[0].Weapons.Single(w => w.Id == first.Context.WeaponIds.Single()).Name);
    }

    [Fact]
    public void A_limited_group_does_not_drop_the_groups_after_it()
    {
        var shooter = Unit("S", "4+", "4+", [], 1, [], Gun("A Missile", "24\"", 1, P("Limited")), Gun("B Rifle", "24\"", 1));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([shooter], [t], x1: -5, x2: 5), 1);
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["A Missile"] = t.Id, ["B Rifle"] = t.Id }));
        Assert.Equal(2, sim.Emitted.OfType<SequenceStarted>().Count(e => e.Sequence == GfNames.AttackSequence));
        // A second shooting excludes the Missile, which the first one used.
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        Assert.Equal(["B Rifle"], GfShooting.Groups(sim.State, TestProperties.Core, shooter.Id, null).Select(g => g.Name));
    }
    [Fact]
    public void A_unit_whose_only_ranged_weapon_is_a_spent_limited_one_is_not_asked_to_shoot()
    {
        // p.14 Limited: "may only be used once per game". The previous shooting fired it, so a later Hold has nothing to fire and asks nothing.
        var shooter = Unit("S", "4+", "4+", [], 1, [], Gun("Missile", "24\"", 1, P("Limited")));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([shooter], [t], x1: -5, x2: 5), 1);
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Missile"] = t.Id }));
        Assert.Empty(sim.State.Projection.Stack);
        sim.Start(GfNames.HoldSeq, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        Assert.Null(sim.AskForOrNull(Samples.Team1));
        Assert.Empty(sim.State.Projection.Stack);
    }
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter "GfMeleeTests|GfGroupsTests"`; expect 23 passed. Commit: `Add charge and melee: Counter, Impact, strike back, who won, consolidation`.**
