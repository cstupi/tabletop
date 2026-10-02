# Task 13: attack-sequence and resolve-hits — the four steps of pages 8 and 9

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfGroups.cs`
- Modify: `server/game/Rules/GrimdarkFuture/GfAttack.cs`, `server/game/Rules/GrimdarkFuture/GfRules.Sequences.cs`
- Test: `tests/Tabletop.GameServer.Tests/GfAttackTests.cs`

**Interfaces:**
- Consumes: Tasks 7, 11, 12; `PieceSelection`, `SelectedPieces` (Protocol).
- Produces: `GfAttack.AttackSequence()` (`attack-sequence`: `determine-attacks`, `pick-target-model`, `roll-to-hit`, `resolve`), `GfAttack.ResolveHits()` (`resolve-hits`: `roll-to-block`, `deal`); `static bool IsTakedownGroup(GameState, IRL<Guid> weapons)` (a weapon in the group carries `Takedown`, p.14 — the one predicate, which `GfGroups.Of` reads for `WeaponGroup.Takedown`); `static AttackKind KindOf(GameState, IRL<Guid> weapons)` (a group with a `Range` stat shoots, otherwise melee — content values → a melee weapon has no range); `static IRL<Guid> EligibleModels(GameState, LiveSet, Guid attacker, Guid target, IRL<Guid> weapons, AttackKind)` (shooting: line of sight to a target model and a weapon in range of it, p.8; melee: within 2" horizontally and 4" vertically, p.9; `Battlefield.InsideReach` first, so containment short-circuits both — p.15: no sight into or out of a container, but an occupant may target a fellow occupant and the container itself, with nothing to measure); `static int HitsFrom(FrameView parent)` (successes of **every** `hit` roll on the parent's steps plus its `extra_hits`, times its `hit_multiplier`, which is what makes one roll per Quality value a total the same way one roll did; `parent.Value("hits")` when the parent rolled no hit roll — `Impact`); `static IRL<(int Quality, int Attacks)> AttacksAgainst(StepContext, Guid attacker, Guid target, IRL<Guid> weapons, AttackKind, string? action)` (p.8's Determine Attacks, callable before the group's own frame exists, split by the Quality value each attack is tested against — what `fire-groups` settles each group's counts with, and `strike` each melee group's, under the action the pushed child will carry); `static bool InCoverAgainst(StepContext, Guid attacker, Guid target, IRL<Guid> weapons)` (p.11's majority, settled at the same moment and for the same reason, with Indirect's sight half dropped when a weapon of the group carries it); `static IRL<(int Quality, int Attacks)> ByQuality(IRD<string,int> values, string prefix)` (the `attacks_q<n>` and `attacks_<i>_q<n>` keys read back off a frame).
- Context of an `attack-sequence` frame: `ActorUnitId` = the attacker, `TargetUnitIds = [target]`, `WeaponIds` = one weapon group (one weapon type), `Action` = the action the attacker took this activation (null for a strike-back). `determine-attacks` records `Values`: `attacks` (the total), one `attacks_q<n>` per Quality value the attacking models bring (p.4: *"the model's Quality value"*), `eligible_models` (live), `defender_models` (the defending unit's living models as `fire-groups` settled them) and `in_cover` (p.11's majority as `fire-groups` settled it). `roll-to-hit` rolls one pooled `hit` roll per `attacks_q<n>`, ascending, and records `Values`: `extra_hits` (`Furious`/`Relentless` per unmodified 6, summed over every pool), `hit_multiplier` (`Blast(X)` capped at `defender_models` — p.13's models in the target, settled with the attack counts), and `granted_<entry>_<amount>` counts for per-die grants (`Rending`'s 6s, summed over every pool), so `roll-to-block` rolls one defense roll per grant group with that property folded into the live set. Cover: `GfRolls.InCover(target, eligible attacker models, ignoresSightObstructions)` — the whole eligible set, not one vantage model, because p.11 reads sight as the attacking unit's, and `ignoresSightObstructions` is whether the attacker has `Indirect` (p.14). `roll-to-block` reads the frozen `in_cover` rather than asking again.
- **A `Takedown` weapon names the model its wounds go to**, in `pick-target-model`, a `PieceSelection(Lookup.AliveModels(target), 1, 1)` addressed to the **attacker's** team (p.14: *"This model may pick any model in the target unit as its individual target"*). It sits **after `determine-attacks` and before `roll-to-hit`**: p.8 declares targets before any roll, so the pick cannot be informed by the hits, and a group whose models are all out of range has already finished. **Once per takedown model**, which is what `GfShooting.Groups` splitting a takedown weapon type into one group per model buys: each such group holds one model's weapon, so its `pick-target-model` is that model's own choice and its `roll-to-hit` pools only its own attacks; 3.5.1 says *"This model"*, so the pick is per model and not per weapon type. The step completes with no Ask for anything else: a group no weapon of which carries `Takedown`, and a target with one living model, whose only model the ordinary allocation reaches anyway. A melee Takedown group asks too: p.14's pick does not depend on the attack kind. Every other `attack-sequence` therefore records one empty `pick-target-model` step, as `deal-wounds` records an empty `disgorge`. `deal-wounds` reads the answer through `GfAttack.TakedownModel` (Task 12) and opens no allocation for it.
- **Every group's attack counts are settled once, before any group fires** (ruling 3). `fire-groups` records `attacks_<i>_q<n>` for every assigned group on the `shooting` frame before it pushes the first one, one key per Quality value that group's models bring, and `determine-attacks` reads its own group's keys back off the pushing frame, so a group that fires after another group's casualties rolls as if nobody had died. p.8's *"all weapons are fired simultaneously"* wins over the order the groups are pushed in. `determine-attacks` counts for itself only when the pushing step recorded nothing. Melee's striking steps record too (Task 16): p.9 sums *"how many attacks the unit has in total for this melee"* before the first strike, so each group's counts are frozen per group before its side strikes, and a strike that kills models cannot shorten a later group. `Counter` strikes first and the defender strikes back with what survives, each as a side's own striking step. `eligible_models` stays the live figure, so the log distinguishes a group that fired at a target none of its models could still reach. **The defending unit's model count is frozen with the counts**: `fire-groups` records `defender_models_<i>` beside the attack counts, `determine-attacks` carries it onto its own frame as `defender_models`, and two rules read it there. p.13 caps `Blast` at *"as many hits as models in the target unit"*, and p.14 lets a joined hero block on its own `Defense` only *"until all other models have been killed"*. Both are therefore counted when the action starts rather than when the group fires, so a second group neither caps lower nor finds the hero alone that the first group left. **p.11's cover majority is frozen with them**: `fire-groups` records `in_cover_<i>`, `determine-attacks` carries it down as `in_cover`, and `roll-to-block` reads it there, so killing models cannot push a unit out of cover part-way through one action. `GfAttack.Pushed(frame, prefix)` is the reader, and it falls back to counting living models when the pusher recorded nothing.

Spec: action sequences → Grimdark Future's sequences (attack-sequence, resolve-hits), Shooting and melee are one sequence; rulebook p.8 (the worked example: 3 attacks, Quality 4+, rolls 3/4/5 → 2 hits; Defense 4+, rolls 3/4 → 1 wound), p.9 (melee reach), p.13 `Blast`, `Furious`, p.14 `Rending`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfAttackTests
{
    static SequenceContext Attack(Tabletop.Content.Unit a, Tabletop.Content.Unit t, string action) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), action);

    [Fact]
    public void Page_8_three_warriors_shoot_the_dwarves_two_hits_one_wound_one_casualty()
    {
        var warriors = Unit("Dynasty Warriors", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1));
        var dwarves = Unit("Dwarves", "4+", "4+", [], 4, []);
        var sim = Sim.Gf(State([warriors], [dwarves], x1: -6, x2: 6), 3, 4, 5, 3, 4);
        sim.Start(GfNames.AttackSequence, Attack(warriors, dwarves, GfNames.Hold));
        var determine = sim.Steps("determine-attacks").Single();
        Assert.Equal(3, determine.Values!["attacks"]);
        var hit = sim.Steps("roll-to-hit").Single().Rolls.Single();
        Assert.Equal((GfNames.Hit, 4, 2), (hit.Roll, hit.Threshold, hit.Dice.Count(d => d.Success)));
        var block = sim.Steps("roll-to-block").Single().Rolls.Single();
        Assert.Equal((GfNames.DefenseRoll, 4, 2, 1), (block.Roll, block.Threshold, block.Dice.Count, block.Dice.Count(d => !d.Success)));
        Assert.Equal([GfNames.AttackSequence, GfNames.ResolveHits, GfNames.DealWounds], sim.State.Projection.Stack.Select(f => f.Sequence));
        var ask = sim.AskFor(Samples.Team2);
        Assert.Equal(1, Assert.IsType<Allocation>(ask.Legal).Amount);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [dwarves.Models[0].Id] = 1 }));
        Assert.Equal(3, sim.Alive(dwarves.Id));
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Equal(5, Dice.DiceCursor.DrawnSoFar(sim.State));
    }

    [Fact]
    public void Out_of_range_models_add_no_attacks_and_no_attacks_finishes_early()
    {
        var far = Unit("Far", "4+", "4+", [], 2, [], Gun("Pistol", "6\"", 2));
        var t = Unit("T", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([far], [t], x1: -12, x2: 12), 6);
        sim.Start(GfNames.AttackSequence, Attack(far, t, GfNames.Hold));
        Assert.Equal(0, sim.Steps("determine-attacks").Single().Values!["attacks"]);
        Assert.Empty(sim.Steps("roll-to-hit")); Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void Blast_multiplies_hits_up_to_the_target_size_and_rending_sixes_block_against_ap_four()
    {
        var mortar = Unit("Mortar", "4+", "4+", [], 1, [], Gun("Mortar", "30\"", 2, P("Blast", 3)));
        var pair = Unit("Pair", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([mortar], [pair], x1: -6, x2: 6), 4, 4, 1, 1, 1, 1);
        sim.Start(GfNames.AttackSequence, Attack(mortar, pair, GfNames.Hold));
        Assert.Equal(2, sim.Steps("roll-to-hit").Single().Values!["hit_multiplier"]);                   // 3 capped at 2 models
        Assert.Equal(4, sim.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);                // 2 hits × 2
        Assert.Equal(4, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);

        var render = Unit("Render", "4+", "4+", [], 2, [], Gun("Saw", "12\"", 1, P("Rending")));
        var target = Unit("T", "4+", "2+", [], 3, []);
        var sim2 = Sim.Gf(State([render], [target], x1: -4, x2: 4), 6, 4, 5, 5);
        sim2.Start(GfNames.AttackSequence, Attack(render, target, GfNames.Hold));
        var hit = sim2.Steps("roll-to-hit").Single();
        Assert.Equal(1, hit.Values!["granted_AP_4"]);
        var blocks = sim2.Steps("roll-to-block").Single().Rolls;
        Assert.Equal(2, blocks.Count);
        var rendered = blocks.Single(r => r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -4));
        Assert.Equal((1, false), (rendered.Dice.Count, rendered.Dice[0].Success));                     // 5 − 4 = 1 against 2+: a wound
        Assert.True(blocks.Single(r => r != rendered).Dice[0].Success);                                 // the plain hit: 5 against 2+
    }

    [Fact]
    public void Furious_adds_a_hit_per_unmodified_six_only_when_charging_in_melee()
    {
        var berserkers = Unit("Berserkers", "4+", "4+", [], 2, [P("Furious")], Melee("Axe", 1));
        var t = Unit("T", "4+", "6+", [], 3, []);
        var sim = Sim.Gf(State([berserkers], [t], x1: -1, x2: 1), 6, 3, 1, 1, 1);
        sim.Start(GfNames.AttackSequence, Attack(berserkers, t, GfNames.Charge));
        Assert.Equal(1, sim.Steps("roll-to-hit").Single().Values!["extra_hits"]);
        Assert.Equal(2, sim.Steps("roll-to-block").Single().Rolls.Single().Dice.Count);
        var hold = Sim.Gf(State([berserkers], [t], x1: -1, x2: 1), 6, 3, 1, 1);
        hold.Start(GfNames.AttackSequence, Attack(berserkers, t, GfNames.Hold));
        Assert.Equal(0, hold.Steps("roll-to-hit").Single().Values!.GetValueOrDefault("extra_hits"));
    }

    [Fact]
    public void A_takedown_model_names_its_target_before_the_hit_roll_and_the_wound_lands_on_it()
    {
        var takedown = Unit("Takedown", "5+", "4+", [], 1, [], Gun("Long Rifle", "30\"", 1, P("Takedown")));
        var t = Unit("T", "4+", "6+", [], 2, []) with { Models = [Unit("T", "4+", "6+", [], 1, []).Models[0], Unit("H", "4+", "6+", [], 1, [P("Hero")]).Models[0] with { Ordinal = 2 }] };
        var (plain, hero) = (t.Models[0].Id, t.Models[1].Id);
        var sim = Sim.Gf(State([takedown], [t], x1: -6, x2: 6), 5, 1);
        sim.Start(GfNames.AttackSequence, Attack(takedown, t, GfNames.Hold));
        var pick = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((1, 1, 2), (pick.Min, pick.Max, pick.Eligible.Count));
        Assert.Empty(sim.Steps("roll-to-hit"));                                                        // the pick precedes the hit roll (p.8)
        sim.Answer(Samples.Team1, new SelectedPieces([hero]));
        Assert.Equal(5, sim.Steps("roll-to-hit").Single().Rolls.Single().Threshold);                   // 3.5.1 dropped Takedown's Quality 2+, so the unit's own 5+ stands
        Assert.Null(sim.AskForOrNull(Samples.Team2));                                                  // no allocation, so heroes-last never applies
        Assert.IsType<OnShelf>(sim.Model(hero).Location);
        Assert.Equal(0, sim.Model(plain).Wounds);
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void No_model_is_picked_for_a_plain_group_or_a_single_model_target()
    {
        var riflemen = Unit("Riflemen", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var pair = Unit("Pair", "4+", "2+", [], 2, []);
        var plain = Sim.Gf(State([riflemen], [pair], x1: -6, x2: 6), 5, 6);
        plain.Start(GfNames.AttackSequence, Attack(riflemen, pair, GfNames.Hold));
        Assert.Null(plain.Steps("pick-target-model").Single().Answer);                                 // the step runs and asks nothing
        Assert.Single(plain.Steps("roll-to-hit"));

        var takedown = Unit("Takedown", "5+", "4+", [], 1, [], Gun("Long Rifle", "30\"", 1, P("Takedown")));
        var lone = Unit("Lone", "4+", "2+", [], 1, []);
        var single = Sim.Gf(State([takedown], [lone], x1: -6, x2: 6), 3, 6);
        single.Start(GfNames.AttackSequence, Attack(takedown, lone, GfNames.Hold));
        Assert.Null(single.AskForOrNull(Samples.Team1));                                               // one living model: nothing to choose
        Assert.Equal(5, single.Steps("roll-to-hit").Single().Rolls.Single().Threshold);                // the unit's own Quality, not 2+
    }

    [Fact]
    public void A_joined_hero_rolls_its_own_quality_pool_beside_the_squads()
    {
        var squad = Unit("Squad", "5+", "5+", [], 2, [], Gun("Rifle", "24\"", 1));
        var joined = squad with { Models = [squad.Models[0] with { Name = "Captain", Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "2+" }, Properties = [P("Hero")] }, squad.Models[1]] };
        var t = Unit("T", "4+", "6+", [], 4, []);
        var sim = Sim.Gf(State([joined], [t], x1: -6, x2: 6), 4, 4, 1);
        sim.Start(GfNames.AttackSequence, Attack(joined, t, GfNames.Hold));
        var determine = sim.Steps("determine-attacks").Single();
        Assert.Equal((2, 1, 1, 4), (determine.Values!["attacks"], determine.Values["attacks_q3"], determine.Values["attacks_q5"], determine.Values["defender_models"]));
        var hit = sim.Steps("roll-to-hit").Single();
        Assert.Equal([3, 5], hit.Rolls.Select(r => r.Threshold));                                      // p.4: one pool per model Quality value, best first
        Assert.Equal([true, false], hit.Rolls.Select(r => r.Dice[0].Success));                         // the same 4 hits at 3+ and misses at 5+
        Assert.Equal(1, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[0].Id] = 1 }));
        Assert.Equal(3, sim.Alive(t.Id));
    }

    [Fact]
    public void A_joined_hero_blocks_on_the_units_defense_until_every_other_model_is_dead()
    {
        var squad = Unit("Squad", "5+", "5+", [], 2, [], Gun("Rifle", "24\"", 1));
        var joined = squad with { Models = [squad.Models[0] with { Name = "Captain", Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "2+" }, Properties = [P("Hero")] }, squad.Models[1]] };
        var shooters = Unit("Shooters", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));

        var full = Sim.Gf(State([shooters], [joined], x1: -6, x2: 6), 6, 3);
        full.Start(GfNames.AttackSequence, Attack(shooters, joined, GfNames.Hold));
        var blocked = full.Steps("roll-to-block").Single().Rolls.Single();
        Assert.Equal((5, false), (blocked.Threshold, blocked.Dice[0].Success));                        // p.14: the unit's Defense while anything else lives
        Assert.Null(full.AskForOrNull(Samples.Team2));                                                  // heroes last (p.14): one eligible model, so no Ask
        Assert.Equal(1, full.Alive(joined.Id));
        Assert.IsType<OnShelf>(full.Model(joined.Models[1].Id).Location);

        var alone = Sim.Gf(State([shooters], [joined], x1: -6, x2: 6), 6, 3);
        alone.Append([new ModelsMoved([new ModelRelocation(joined.Models[1].Id, alone.Model(joined.Models[1].Id).Location, new OnShelf())])]);
        alone.Start(GfNames.AttackSequence, Attack(shooters, joined, GfNames.Hold));
        var own = alone.Steps("roll-to-block").Single().Rolls.Single();
        Assert.Equal((2, true), (own.Threshold, own.Dice[0].Success));                                 // the same 3, blocked on the hero's own 2+
        Assert.Null(alone.AskForOrNull(Samples.Team2));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Create `GfGroups.cs` and add to `GfAttack.cs`**

`GfGroups.cs`, the one grouping shooting (Task 14) and melee (Task 16) share:

```csharp
using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed record WeaponGroup(string Name, IReadOnlyList<Guid> WeaponIds, bool Takedown);

/// <summary>One group per weapon type, shared by shooting (p.8) and melee (p.9), so a weapon's AP and Deadly reach only its own hits.</summary>
public static class GfGroups
{
    /// <summary>
    /// Groups by weapon name. A Takedown weapon type splits into one group per weapon, because p.14 gives the pick to "This model" and
    /// the group's name (the key a target declaration uses) carries the ordinal so two groups never share one. Order: p.14 says
    /// "Takedown attacks must be resolved before other weapons" and p.13 says "Hits from Deadly must be resolved first"; both say
    /// first, so Takedown leads on p.14's "before other weapons", then Deadly, then the rest, each by name.
    /// </summary>
    public static IReadOnlyList<WeaponGroup> Of(GameState state, IEnumerable<Weapon> weapons)
    {
        var groups = new List<(WeaponGroup Group, bool Deadly)>();
        foreach (var g in weapons.GroupBy(w => w.Name))
        {
            var ids = g.Select(w => w.Id).ToList();
            var deadly = g.Any(w => w.Properties.Any(p => p.Name == "Deadly"));
            if (!GfAttack.IsTakedownGroup(state, ids)) { groups.Add((new WeaponGroup(g.Key, ids, false), deadly)); continue; }
            groups.AddRange(ids.Select((id, i) => (new WeaponGroup(ids.Count == 1 ? g.Key : $"{g.Key} ({i + 1})", [id], true), deadly)));
        }
        return groups.OrderByDescending(x => x.Group.Takedown).ThenByDescending(x => x.Deadly).ThenBy(x => x.Group.Name, StringComparer.Ordinal).Select(x => x.Group).ToList();
    }
}
```

Add to `GfAttack.cs`:

```csharp
    public static Sequence AttackSequence() => new(GfNames.AttackSequence, [new DetermineAttacksStep(), new PickTargetModelStep(), new RollToHitStep(), new PushStep("resolve", GfNames.ResolveHits)]);
    public static Sequence ResolveHits() => new(GfNames.ResolveHits, [new RollToBlockStep(), new PushStep("deal", GfNames.DealWounds)]);

    /// <summary>p.14 Takedown is a weapon property, so a group carries it when one of its weapons does. GfGroups reads this for WeaponGroup.Takedown, and splits such a group so each holds one model's weapon.</summary>
    public static bool IsTakedownGroup(GameState state, IReadOnlyList<Guid> weapons) =>
        state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).Where(w => weapons.Contains(w.Id)).Any(w => w.Properties.Any(p => p.Name == GfNames.Takedown));

    public static AttackKind KindOf(GameState state, IReadOnlyList<Guid> weapons) =>
        state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => weapons.Contains(w.Id))?.Stats.ContainsKey(GfNames.Range) == true ? AttackKind.Shooting : AttackKind.Melee;

    /// <summary>
    /// p.8: models with line of sight to a target model and a weapon in range of it; p.9: within 2" horizontally and 4" vertically.
    /// Containment answers before any of that (p.15): a unit inside a container and a unit outside it cannot target each other at
    /// all, and two units in one container — or an occupant and the container model itself — need no measurement, because there is
    /// no distance and no sight line inside a box. Everything armed is eligible then; only the weapon check survives.
    /// The occupant-and-container half of that clause is unreachable in a game: `ValidTargets` offers enemy units
    /// only, a unit's own transport is friendly, and no unit may enter an enemy container, so the branch is written for
    /// faithfulness to p.15 and is exercised only by a menu-written state.
    /// </summary>
    public static IReadOnlyList<Guid> EligibleModels(GameState state, LiveSet live, Guid attacker, Guid target, IReadOnlyList<Guid> weapons, AttackKind kind)
    {
        if (Battlefield.InsideReach(state, attacker, target) is { } reaches)
            return reaches ? Lookup.AliveModels(state, attacker).Where(m => state.Projection.Models[m].Slot.Weapons.Any(w => weapons.Contains(w.Id))).ToList() : [];
        var targets = Lookup.LivingModels(state, target);
        var indirect = live.Has(Side.Attacker, GfNames.Indirect);
        var sight = kind == AttackKind.Shooting && !indirect ? Sight.From(state, attacker) : null;               // built once for every pair below
        return Lookup.LivingModels(state, attacker).Where(m =>
        {
            var mine = state.Projection.Models[m].Slot.Weapons.Where(w => weapons.Contains(w.Id)).ToList();
            if (mine.Count == 0) return false;
            if (kind == AttackKind.Melee) return targets.Any(t => Battlefield.Distance(state, m, t) <= GfNames.MeleeHorizontal && Battlefield.Vertical(state, m, t) <= GfNames.MeleeVertical);
            var range = mine.Max(w => StatReader.WeaponRange(live, Side.Attacker, w.Id) ?? 0);
            return targets.Any(t => Battlefield.Distance(state, m, t) <= range && (sight is null || sight.CanSee(m, t)));
        }).ToList();
    }

    /// <summary>
    /// p.8's Determine Attacks, asked from outside the group's own frame: what this group would roll against this target now,
    /// split by the Quality value each attack is tested against. `fire-groups` calls it once per group before the first shot,
    /// because p.8 fires a unit's weapons simultaneously, and melee's striking steps before the first strike (p.9). The action is
    /// the pushed child's, since a defender strikes back under none.
    /// </summary>
    public static IReadOnlyList<(int Quality, int Attacks)> AttacksAgainst(StepContext ctx, Guid attacker, Guid target, IReadOnlyList<Guid> weapons, AttackKind kind, string? action)
    {
        var live = LiveProperties.Settle(AttackInteraction(ctx, attacker, target, weapons, kind, action));
        return Attacks(ctx, live, EligibleModels(ctx.State, live, attacker, target, weapons, kind), weapons, kind);
    }

    /// <summary>
    /// p.11's cover majority, asked from outside the group's own frame, for the same reason and at the same moment as the attack
    /// counts: p.8 fires a unit's weapons simultaneously, so a group firing after another group's casualties judges *"the majority
    /// of models in a unit"* against the unit as it stood when the action started. Positions do not change
    /// between groups; only the count does.
    /// </summary>
    public static bool InCoverAgainst(StepContext ctx, Guid attacker, Guid target, IReadOnlyList<Guid> weapons)
    {
        var live = LiveProperties.Settle(AttackInteraction(ctx, attacker, target, weapons, AttackKind.Shooting, ctx.Frame.Context.Action));
        return GfRolls.InCover(ctx.State, target, EligibleModels(ctx.State, live, attacker, target, weapons, AttackKind.Shooting), live.Has(Side.Attacker, GfNames.Indirect));
    }

    /// <summary>
    /// p.8 sums the Attack values of the models that can shoot, and p.8's next step takes "as many Quality tests as attacks". p.4
    /// scores each of those against *"the model's Quality value"*, so the sum is per Quality value rather than per unit: a joined
    /// hero contributes its own attacks at its own value and everything else contributes at the unit's. Ordered
    /// by the value, best first, so the pools and therefore the dice cursor do not depend on model order.
    /// </summary>
    static IReadOnlyList<(int Quality, int Attacks)> Attacks(StepContext ctx, LiveSet live, IEnumerable<Guid> models, IReadOnlyList<Guid> weapons, AttackKind kind) =>
        models.Select(m => (Quality: GfRolls.HitQuality(ctx, live, kind, m),
                            Attacks: ctx.Projection.Models[m].Slot.Weapons.Where(w => weapons.Contains(w.Id)).Sum(w => StatReader.Attacks(ctx.State, w.Id))))
              .Where(x => x.Attacks > 0)
              .GroupBy(x => x.Quality).Select(g => (Quality: g.Key, Attacks: g.Sum(x => x.Attacks)))
              .OrderBy(x => x.Quality).ToList();

    /// <summary>
    /// Attack counts keyed by the Quality value they are rolled at, read back off a frame's `Values`: `&lt;prefix&gt;&lt;n&gt;` holds the
    /// attacks tested against `n`+. `determine-attacks` writes `attacks_q&lt;n&gt;` on its own frame and `fire-groups` writes
    /// `attacks_&lt;i&gt;_q&lt;n&gt;` on the pushing one, so one reader serves both.
    /// </summary>
    public static IReadOnlyList<(int Quality, int Attacks)> ByQuality(IReadOnlyDictionary<string, int> values, string prefix) =>
        values.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(kv.Key.AsSpan(prefix.Length), out _))
              .Select(kv => (Quality: int.Parse(kv.Key[prefix.Length..]), Attacks: kv.Value))
              .OrderBy(x => x.Quality).ToList();

    public static int HitsFrom(FrameView parent)
    {
        var hitRolls = parent.Frame.Steps.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.Hit).ToList();
        if (hitRolls.Count == 0) return parent.Value("hits");
        return (hitRolls.Sum(Dice.RollOutcome.Successes) + parent.Value("extra_hits")) * Math.Max(1, parent.Value("hit_multiplier", 1));
    }

    static (Interaction Ix, LiveSet Live, AttackKind Kind) Settle(StepContext ctx)
    {
        var c = ctx.Frame.Context;
        var kind = KindOf(ctx.State, c.WeaponIds);
        var ix = AttackInteraction(ctx, c.ActorUnitId, c.TargetUnitIds[0], c.WeaponIds, kind, c.Action);
        return (ix, LiveProperties.Settle(ix), kind);
    }

    /// <summary>Pushes a child once and completes when it has finished. Values pass through the frame stack, not through this step.</summary>
    public sealed class PushStep(string name, string sequence, Func<StepContext, SequenceContext>? context = null) : IStep
    {
        public string Name => name;
        public StepOutcome Run(StepContext ctx) => ctx.History.Children.Any(ch => ch.Finished || ch.Abandoned) ? new Complete([]) : new Push(sequence, context?.Invoke(ctx) ?? ctx.Frame.Context);
    }

    /// <summary>
    /// p.8's first step. The counts are not this frame's to compute when a pushing step already settled them: `fire-groups` fixes
    /// every group's attacks before the first shot, because p.8 fires a unit's weapons simultaneously, and melee's striking steps
    /// fix them before the first strike, because p.9 sums "how many attacks the unit has in total for this melee"; this reads
    /// `attacks_&lt;i&gt;_q&lt;n&gt;` back off the pushing frame. Only a push that recorded nothing is counted here and now.
    /// `eligible_models` is the live figure either way, so a recorded count standing over an empty eligible set is visible in the log.
    ///
    /// The counts are per Quality value, one key each, because p.4 tests every attack against *"the model's Quality value"* and a
    /// joined hero brings its own. `attacks` stays the total, which is what the log line and the readers that do
    /// not care about the split read. `defender_models` is the defending unit's living models, settled at the same moment and for
    /// the same reason, and carried here so `roll-to-hit` can cap `Blast` with it (p.13) and `roll-to-block` can decide whether
    /// p.14's joined hero is alone. `in_cover` is p.11's majority, settled at that same moment and carried here for
    /// `roll-to-block`. A melee push settles no `in_cover`, which melee does not read; a push that settled nothing falls back to the live reading.
    /// </summary>
    sealed class DetermineAttacksStep : IStep
    {
        public string Name => "determine-attacks";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            var eligible = EligibleModels(ctx.State, live, ix.Attacker.UnitId, ix.Defender!.UnitId, ctx.Frame.Context.WeaponIds, kind);
            // `attacks_<i>` is the presence signal rather than the per-Quality keys, because a group the pusher settled at zero
            // attacks writes no per-Quality key at all and must still not be recounted here.
            var settled = Pushed(ctx.Frame, "attacks");
            var byQuality = settled is null ? Attacks(ctx, live, eligible, ctx.Frame.Context.WeaponIds, kind) : PushedByQuality(ctx.Frame);
            var values = new Dictionary<string, int>
            {
                ["attacks"] = settled ?? byQuality.Sum(x => x.Attacks),
                ["eligible_models"] = eligible.Count,
                ["defender_models"] = Pushed(ctx.Frame, "defender_models") ?? Lookup.LivingModels(ctx.State, ix.Defender.UnitId).Count,
                ["in_cover"] = Pushed(ctx.Frame, "in_cover") ?? (kind == AttackKind.Shooting && GfRolls.InCover(ctx.State, ix.Defender.UnitId, eligible, live.Has(Side.Attacker, GfNames.Indirect)) ? 1 : 0),
            };
            foreach (var (quality, attacks) in byQuality) values[$"attacks_q{quality}"] = attacks;
            return values["attacks"] == 0 ? new Finish([], values) : new Complete([], values);
        }

    }

    /// <summary>
    /// A number the pushing step settled for this child before it pushed anything, keyed by the child's ordinal among that step's
    /// children: `defender_models_&lt;i&gt;`, and `attacks_&lt;i&gt;_q&lt;n&gt;` through `ByQuality`. Read off the pusher's recording run, the
    /// parent's last completion, which is still marked as a repeat while its children run; a frame's merged values would mix in the
    /// keys an earlier striking step of the same melee wrote. Null when the pusher recorded none.
    /// </summary>
    static int? Pushed(FrameView frame, string prefix) =>
        Recording(frame) is { } values && values.TryGetValue($"{prefix}_{frame.Parent!.History.Children.Count - 1}", out var n) ? n : null;

    /// <summary>The same read for the per-Quality keys, which are a set rather than one number. Empty when the pusher recorded none.</summary>
    static IReadOnlyList<(int Quality, int Attacks)> PushedByQuality(FrameView frame) =>
        Recording(frame) is { } values ? ByQuality(values, $"attacks_{frame.Parent!.History.Children.Count - 1}_q") : [];

    static IReadOnlyDictionary<string, int>? Recording(FrameView frame) =>
        frame.Parent is { History.Children.Count: > 0 } parent && parent.Frame.Steps is [.., var last] && last.Values is { } values && values.ContainsKey(StepRepeat.Key) ? values : null;

    /// <summary>
    /// p.14 Takedown: the model names one model in the target unit as the target of its wounds. A target declaration, so it is made
    /// before this group's hit roll (p.8), and after determine-attacks, so a group with nothing in range never asks. One pick per
    /// group and not per shot: GfGroups gives a takedown weapon type one group per model, so the group is one model's dice
    /// at one Quality value and roll-to-hit pools them into one roll. deal-wounds reads the answer
    /// back through TakedownModel and assigns to that model without opening an allocation.
    /// </summary>
    sealed class PickTargetModelStep : IStep
    {
        public string Name => "pick-target-model";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.History.LastClosed() is not null) return new Complete([]);
            var (weapons, target) = (ctx.Frame.Context.WeaponIds, ctx.Frame.Context.TargetUnitIds[0]);
            if (!IsTakedownGroup(ctx.State, weapons)) return new Complete([]);
            var models = Lookup.AliveModels(ctx.State, target);
            if (models.Count < 2) return new Complete([]);   // one model to choose from: the ordinary allocation reaches it anyway
            return new OpenAsks([ctx.Ask("takedown_target", "Pick the model this takedown weapon targets", ctx.ActorTeam!.Value, new PieceSelection(models, 1, 1))]);
        }
    }

    /// <summary>
    /// p.8's second step: *"take as many Quality tests as attacks"*. One pooled roll per Quality value the attacking models brought,
    /// in ascending order, since p.4 scores each test against *"the model's Quality value"* and a joined hero brings its own.
    /// p.4's *"having dice of multiple colors ... and then roll them all at once"* is the tabletop procedure for it. The
    /// counts are `determine-attacks`' `attacks_q&lt;n&gt;`, so nothing is recounted here and the pools are the ones the action started
    /// with. `extra_hits` and every `granted_&lt;entry&gt;_&lt;amount&gt;` sum across all of the pools, because they belong to the group's hits
    /// rather than to one pool, and `GfAttack.HitsFrom` already sums the successes of every `hit` roll on the frame.
    /// </summary>
    sealed class RollToHitStep : IStep
    {
        public string Name => "roll-to-hit";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            // p.13 caps Blast at "as many hits as models in the target unit" without saying when they are counted. p.8 fires all
            // weapons simultaneously, so the cap is fixed at the same moment as the attack counts: this reads the defender's model
            // count that determine-attacks carried down from the pushing frame.
            var cap = ctx.Frame.Value("defender_models", Lookup.LivingModels(ctx.State, ix.Defender!.UnitId).Count);
            var values = new Dictionary<string, int> { ["extra_hits"] = 0, ["hit_multiplier"] = RollEffects.HitMultiplier(live, Side.Attacker, cap) };
            foreach (var (quality, attacks) in ByQuality(ctx.Frame.Values, "attacks_q"))
                foreach (var die in ctx.Roll(GfRolls.HitPlan(ctx, live, kind, quality), attacks).Dice.Where(d => d.Success))
                {
                    var effects = RollEffects.PerDie(live, Side.Attacker, die, GfNames.Hit);
                    values["extra_hits"] += effects.ExtraHits;
                    foreach (var (entry, amount) in effects.Grants) values[$"granted_{entry}_{amount}"] = values.GetValueOrDefault($"granted_{entry}_{amount}") + 1;
                }
            return new Complete([], values);
        }
    }

    /// <summary>`granted_&lt;entry&gt;_&lt;amount&gt;`: the entry may hold underscores, the amount never does, so the split is at the last one.</summary>
    public static (string Entry, int Amount) ParseGrantKey(string key)
    {
        var cut = key.LastIndexOf('_');
        return (key["granted_".Length..cut], int.Parse(key[(cut + 1)..]));
    }

    sealed class RollToBlockStep : IStep
    {
        public string Name => "roll-to-block";
        public StepOutcome Run(StepContext ctx)
        {
            var (ix, live, kind) = Settle(ctx);
            var parent = ctx.Frame.Parent!;
            var hits = HitsFrom(parent);
            if (hits <= 0) return new Finish([], new Dictionary<string, int> { ["wounds"] = 0 });
            var defender = ix.Defender!.UnitId;
            // p.11's majority is frozen with the attack counts (p.8: all weapons fire simultaneously), so a group firing after casualties reads the
            // fact determine-attacks carried down. A parent that recorded none is a spell's hits or Impact's, where live is the answer.
            var inCover = kind == AttackKind.Shooting
                && (parent.Values.TryGetValue("in_cover", out var settled) ? settled == 1
                    : GfRolls.InCover(ctx.State, defender, EligibleModels(ctx.State, live, ix.Attacker.UnitId, defender, ctx.Frame.Context.WeaponIds, kind), live.Has(Side.Attacker, GfNames.Indirect)));
            var multiplier = Math.Max(1, parent.Value("hit_multiplier", 1));
            // p.14's Hero clause needs the defending unit's model count, and determine-attacks carried down the one settled before any
            // group fired. A parent that recorded none is a spell's hits or Impact's, where the live count is the count.
            var defenderModels = parent.Value("defender_models", Lookup.LivingModels(ctx.State, defender).Count);
            var wounds = 0; var plain = hits;
            foreach (var (key, count) in parent.Values.Where(kv => kv.Key.StartsWith("granted_")))
            {
                var (entry, amount) = ParseGrantKey(key);
                var granted = live.WithGranted(Side.Attacker, entry, amount);
                var group = count * multiplier; plain -= group;
                wounds += Dice.RollOutcome.Failures(ctx.Roll(GfRolls.DefensePlan(ctx, granted, inCover, defenderModels), group));
            }
            if (plain > 0) wounds += Dice.RollOutcome.Failures(ctx.Roll(GfRolls.DefensePlan(ctx, live, inCover, defenderModels), plain));
            return new Complete([], new Dictionary<string, int> { ["wounds"] = wounds });
        }
    }
```

`GfRules._sequences` gains `GfAttack.AttackSequence()` and `GfAttack.ResolveHits()`. `deal-wounds` (Task 12) reads the defense rolls back from the parent, so a reroll of a block die between `roll-to-block` and `assign` (wait-between-steps on) changes the wounds asked for.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests --filter GfAttackTests`; expect 8 passed. Commit: `Add the attack sequence and resolve-hits: determine, hit, block, deal`.**
