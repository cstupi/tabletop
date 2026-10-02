# Task 7: Pass two — what the live properties do to a roll, a stat and a die

**Files:**
- Create: `server/game/Effects/RollEffects.cs`, `server/game/Effects/StatReader.cs`
- Test: `tests/Tabletop.GameServer.Tests/RollEffectsTests.cs`

**Interfaces:**
- Consumes: Tasks 5–6; `StatValue` (Content).
- Produces: `static class StatReader` with `int TargetNumber(LiveSet, Side, StatRef, Guid? modelId = null)` (the stat text read as a target number, then `ReplaceStat` effects in ordinal order with the last winning, then `ModifyStat` amounts summed. A `StatLevel.Unit` ref reads the unit's text. A `StatLevel.Model` ref reads the named model's slot and falls back to the unit's text where the slot declares none, which is p.4's *"the model's Quality value"* for a joined hero (ruling 13). A model-level ref with no model, and every other level, throws `NotSupportedException`. The effect scan matches on the stat's **name** at either target-number level, because `Quality` and `Defense` are one stat with a declaration at each level and the catalogue writes every row against the unit-level ref, so `Reliable` replaces a joined hero's own value too), `double? WeaponRange(LiveSet, Side, Guid weaponId)` (null for a melee weapon; `ModifyStat` on `Range` applied; negative amounts dropped when `RangePenalties` is suppressed), `int Attacks(GameState, Guid weaponId)`, `double ActionDistance(LiveSet, Side, string action)` (the action row's distance plus `ModifyStat` effects whose `TargetAction` is it); `sealed record DieEffects(int ExtraHits, IRL<(string Entry, int? Parameter)> Grants)`; `static class RollEffects` with `RollPlan Plan(LiveSet, Side roller, string roll, int? threshold, IEnumerable<RollModifier> baseModifiers, RollConvention, int? requiredRaw, string hitRollName)` (adds one `RollModifier(entry name, amount)` per `ModifyRoll` effect acting on the roller for this roll; drops `cover`-sourced base modifiers when `Cover` is suppressed and negative modifiers when `ToHitPenalties` is suppressed and the roll is a hit roll — the roll named by the ruleset as its hit roll is passed in as `hitRollName`; collects `RerollRaw` from `ForceReroll` effects on this roll whose only unmet condition is the die), `DieEffects PerDie(LiveSet, Side roller, Die die, string roll)` (`AddHit` amounts and `GrantProperty` entries whose conditions hold for this die), `int HitMultiplier(LiveSet, Side attacker, int defenderModels)` (product of `Multiply hits` amounts, each capped by `CapKind`), `int WoundMultiplier(LiveSet, Side attacker)`, `IRL<(LiveProperty Source, Effect Roll)> OwnRolls(LiveSet, Side, Func<Effect, bool> childFilter)` (kind-5 effects whose child matches: `Regeneration` for a wound, `Fearless` for a morale test, `Impact` for hits).
- The hit roll's name is the ruleset's; `Plan` takes it as a parameter so `ToHitPenalties` can be applied without the effects module knowing OPR's roll names.

Spec: effect grammar → The twelve effect kinds (1–8), "Unmodified" is a first-class concept, Multiplication carries a cap; action sequences → One roll primitive ("Effects apply at the roll or step they attach to, via the two passes"); resolution → Modifiers and abilities are different things; rulebook p.13–15 (`Unstoppable`, `Blast`, `Furious`, `Rending`, `Reliable`, `Bane`).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class RollEffectsTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static readonly RollConvention D6 = new(6, null);
    static readonly StatRef Quality = new(StatLevel.Unit, "Quality"), Defense = new(StatLevel.Unit, "Defense");
    static readonly StatRef ModelQuality = new(StatLevel.Model, "Quality"), ModelDefense = new(StatLevel.Model, "Defense");

    static (LiveSet Live, Tabletop.Content.Unit A, Tabletop.Content.Unit D) Shooting(Tabletop.Content.Unit shooter, Tabletop.Content.Unit target, double range, string action = "Hold")
    {
        var state = State([shooter], [target]);
        var weapons = shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id));
        return (LiveProperties.Settle(new Interaction(state, Core, Party.Of(state, shooter.Id, shooter.Models.Select(m => m.Id), weapons), Party.Whole(state, target.Id), AttackKind.Shooting, action, range, [])), shooter, target);
    }

    [Fact]
    public void Stealth_modifies_the_hit_roll_aircraft_cuts_range_and_unstoppable_ignores_the_penalties()
    {
        var (live, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)), Unit("T", "4+", "4+", [P("Aircraft")], 2, [P("Stealth")]), 12);
        var plan = RollEffects.Plan(live, Side.Attacker, "hit", StatReader.TargetNumber(live, Side.Attacker, Quality), [], D6, null, hitRollName: "hit");
        Assert.Equal(4, plan.Threshold); Assert.Equal(-1, plan.NetModifier);                                      // 3.5.1 Aircraft is -12" to range only, with no to-hit modifier
        Assert.Equal(["Stealth"], plan.Modifiers.Select(m => m.Source));
        Assert.Equal(12.0, StatReader.WeaponRange(live, Side.Attacker, live.Interaction.Attacker.WeaponIds[0]));
        var (unstoppable, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Beam", "24\"", 1, P("Unstoppable"))), Unit("T", "4+", "4+", [P("Aircraft")], 2, [P("Stealth")]), 12);
        Assert.Equal(0, RollEffects.Plan(unstoppable, Side.Attacker, "hit", 4, [], D6, null, "hit").NetModifier);
        Assert.Equal(24.0, StatReader.WeaponRange(unstoppable, Side.Attacker, unstoppable.Interaction.Attacker.WeaponIds[0]));   // -12" range penalty ignored
        var covered = RollEffects.Plan(unstoppable, Side.Defender, "defense", 4, [new RollModifier("cover", 1)], D6, null, "hit");
        Assert.Equal(1, covered.NetModifier);                                                                     // p.15 Unstoppable ignores modifiers to its own weapon, not the target's cover
        var (blast, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Bomb", "24\"", 1, P("Blast", 3))), Unit("T", "4+", "4+", [], 2, []), 12);
        Assert.Empty(RollEffects.Plan(blast, Side.Defender, "defense", 4, [new RollModifier("cover", 1)], D6, null, "hit").Modifiers);   // p.13 Blast ignores cover
    }

    [Fact]
    public void Reliable_replaces_quality_ap_modifies_defense_and_bane_forces_a_reroll_of_sixes()
    {
        var (live, _, _) = Shooting(Unit("S", "5+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Reliable"), P("AP", 2), P("Bane"))), Unit("T", "4+", "3+", [], 2, [P("Regeneration")]), 6);
        Assert.Equal(2, StatReader.TargetNumber(live, Side.Attacker, Quality));
        var defense = RollEffects.Plan(live, Side.Defender, "defense", StatReader.TargetNumber(live, Side.Defender, Defense), [], D6, null, "hit");
        Assert.Equal((3, -2), (defense.Threshold, defense.NetModifier));
        Assert.Equal([6], defense.RerollRaw);
        Assert.Empty(RollEffects.OwnRolls(live, Side.Defender, c => c.Kind == EffectKind.OverrideResult && c.TargetResult == TargetResult.Wound));   // Regeneration suppressed
    }

    [Fact]
    public void A_slot_that_declares_its_own_target_number_is_read_at_model_level_and_reliable_still_replaces_it()
    {
        var squad = Unit("Squad", "5+", "4+", [], 2, [], Gun("Rifle", "24\"", 1));
        var joined = squad with { Models = [squad.Models[0] with { Name = "Captain", Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "2+" }, Properties = [P("Hero")] }, squad.Models[1]] };
        var (live, _, _) = Shooting(joined, Unit("T", "4+", "4+", [], 3, []), 12);
        Assert.Equal(3, StatReader.TargetNumber(live, Side.Attacker, ModelQuality, joined.Models[0].Id));   // p.4: "the model's Quality value"
        Assert.Equal(5, StatReader.TargetNumber(live, Side.Attacker, ModelQuality, joined.Models[1].Id));   // no slot value: the unit's
        Assert.Equal(2, StatReader.TargetNumber(live, Side.Attacker, ModelDefense, joined.Models[0].Id));
        Assert.Equal(5, StatReader.TargetNumber(live, Side.Attacker, Quality));                             // the unit-level ref is unchanged
        Assert.Throws<NotSupportedException>(() => StatReader.TargetNumber(live, Side.Attacker, ModelQuality));

        var elite = joined with { Models = joined.Models.Select(m => m with { Weapons = [Gun("Rifle", "24\"", 1, P("Reliable"))] }).ToList() };
        var (reliable, _, _) = Shooting(elite, Unit("T", "4+", "4+", [], 3, []), 12);
        Assert.Equal(2, StatReader.TargetNumber(reliable, Side.Attacker, ModelQuality, elite.Models[0].Id));   // p.14 replaces the value the model brought, hero or not
        Assert.Equal(2, StatReader.TargetNumber(reliable, Side.Attacker, ModelQuality, elite.Models[1].Id));
    }

    [Fact]
    public void Per_die_effects_add_hits_and_grant_on_unmodified_sixes_and_blast_multiplies_with_a_cap()
    {
        var charger = Unit("C", "4+", "4+", [], 3, [P("Furious")], Melee("Blade", 1, P("Rending")));
        var state = State([charger], [Unit("T", "4+", "4+", [], 2, [])]);
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Of(state, charger.Id, charger.Models.Select(m => m.Id), charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id))), Party.Whole(state, state.Board.Armies[1].Snapshot.Units[0].Id), AttackKind.Melee, "Charge", 1, []));
        var six = RollEffects.PerDie(live, Side.Attacker, new Die(6, 6, true), "hit");
        Assert.Equal(1, six.ExtraHits); Assert.Equal([("AP", (int?)4)], six.Grants);
        var five = RollEffects.PerDie(live, Side.Attacker, new Die(5, 6, true), "hit");
        Assert.Equal((0, 0), (five.ExtraHits, five.Grants.Count));
        var (blast, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Mortar", "30\"", 1, P("Blast", 3))), Unit("T", "4+", "4+", [], 2, []), 12);
        Assert.Equal(2, RollEffects.HitMultiplier(blast, Side.Attacker, defenderModels: 2));
        Assert.Equal(3, RollEffects.HitMultiplier(blast, Side.Attacker, defenderModels: 5));
        Assert.Equal(1, RollEffects.WoundMultiplier(blast, Side.Attacker));
        var (deadly, _, _) = Shooting(Unit("S", "4+", "4+", [], 1, [], Gun("Cannon", "30\"", 1, P("Deadly", 3))), Unit("T", "4+", "4+", [], 2, []), 12);
        Assert.Equal(3, RollEffects.WoundMultiplier(deadly, Side.Attacker));
    }

    [Fact]
    public void Action_distances_carry_fast_and_slow()
    {
        var fast = Unit("F", "4+", "4+", [], 2, [P("Fast")]);
        var state = State([fast], [Unit("T", "4+", "4+", [], 1, [])]);
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, fast.Id), null, null, "Advance", null, []));
        Assert.Equal((8.0, 16.0, 0.0), (StatReader.ActionDistance(live, Side.Attacker, "Advance"), StatReader.ActionDistance(live, Side.Attacker, "Charge"), StatReader.ActionDistance(live, Side.Attacker, "Hold")));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `StatReader.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;

namespace Tabletop.GameServer.Effects;

/// <summary>Stats and distances as the live properties leave them: replace first (ordinal order, last wins), then modify.</summary>
public static class StatReader
{
    /// <summary>
    /// A ruleset may list a target number among a unit's stats yet define the test against the model's own value, so it may
    /// declare the same target number at model level as well, and a slot that carries its own value is a joined model.
    /// Pass the model to read that slot; the unit's text is the fallback for every slot that declares none, so an ordinary
    /// squad reads the same number whichever ref is used. A model-level ref with no model has nothing to resolve against.
    /// </summary>
    public static int TargetNumber(LiveSet live, Side side, StatRef stat, Guid? modelId = null)
    {
        var state = live.Interaction.State;
        var unit = state.Projection.Units[live.Interaction.Of(side).UnitId];
        var text = (stat.Level, modelId) switch
        {
            (StatLevel.Unit, _) => unit.Definition.Stats[stat.Name],
            (StatLevel.Model, { } id) => state.Projection.Models[id].Slot.Stats.TryGetValue(stat.Name, out var own) ? own : unit.Definition.Stats[stat.Name],
            _ => throw new NotSupportedException($"a target number is read at unit level, or at model level for a named model; {stat.Level} {stat.Name} has no model to read"),
        };
        var value = StatValue.Read(StatKind.TargetNumber, text).TargetNumber;
        // A target number is one stat with a declaration at each level, and every catalog row names the unit-level ref, so the scan
        // matches on the name across both. That is what puts a rule that sets the number over a joined model's own value.
        bool Acts(Effect e) => e.TargetStat is { Name: var n, Level: StatLevel.Unit or StatLevel.Model } && n == stat.Name;
        foreach (var (_, e) in live.Effects(side, EffectKind.ReplaceStat).Where(x => Acts(x.Effect))) value = StatValue.Read(StatKind.TargetNumber, e.Replacement!).TargetNumber;
        foreach (var (src, e) in live.Effects(side, EffectKind.ModifyStat).Where(x => Acts(x.Effect))) value += e.EffectiveAmount(src.Parameter);
        return value;
    }

    public static double? WeaponRange(LiveSet live, Side side, Guid weaponId)
    {
        var weapon = live.Interaction.State.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).First(w => w.Id == weaponId);
        if (!weapon.Stats.TryGetValue("Range", out var text)) return null;
        var range = StatValue.Read(StatKind.Distance, text).Inches;
        var rangeRef = new StatRef(StatLevel.Weapon, "Range");
        foreach (var (src, e) in live.Effects(side, EffectKind.ModifyStat).Where(x => x.Effect.TargetStat == rangeRef))
        {
            var amount = e.EffectiveAmount(src.Parameter);
            if (amount < 0 && live.SuppressedModifiers.Contains(TargetModifier.RangePenalties)) continue;
            range += amount;
        }
        return Math.Max(0, range);
    }

    public static int Attacks(GameState state, Guid weaponId) =>
        StatValue.Read(StatKind.Count, state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).First(w => w.Id == weaponId).Stats["Attacks"]).Count;

    public static double ActionDistance(LiveSet live, Side side, string action)
    {
        var distance = live.Interaction.Properties.Action(action).Distance;
        foreach (var (src, e) in live.Effects(side, EffectKind.ModifyStat).Where(x => x.Effect.TargetAction == action)) distance += e.EffectiveAmount(src.Parameter);
        return Math.Max(0, distance);
    }
}
```

The stat names `Range` and `Attacks` appear here because `StatKind.Distance` and `StatKind.Count` need a *which* — they are the weapon stat declared as the distance the weapon reaches and the count of attacks. Task 16's constraint test whitelists exactly these two strings in `Effects/`; a second ruleset with differently named weapon stats moves them onto `IGameRules` (ruling 5).

- [ ] **Step 3: Write `RollEffects.cs`**

```csharp
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

public sealed record DieEffects(int ExtraHits, IReadOnlyList<(string Entry, int? Parameter)> Grants);

public static class RollEffects
{
    /// <summary>The roll as the live set leaves it. Summing is generic; the convention clamps. Suppressed modifier classes are removed before summing.</summary>
    public static RollPlan Plan(LiveSet live, Side roller, string roll, int? threshold, IEnumerable<RollModifier> baseModifiers, RollConvention convention, int? requiredRaw, string hitRollName)
    {
        var modifiers = baseModifiers.ToList();
        foreach (var (src, e) in live.Effects(roller, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == roll))
            modifiers.Add(new RollModifier(src.Entry.Name, e.EffectiveAmount(src.Parameter)));
        if (live.SuppressedModifiers.Contains(TargetModifier.Cover)) modifiers.RemoveAll(m => m.Source == "cover");
        if (roll == hitRollName && live.SuppressedModifiers.Contains(TargetModifier.ToHitPenalties)) modifiers.RemoveAll(m => m.Amount < 0);
        // ForceReroll: its roll_result condition names the face; every other condition must hold now.
        var reroll = new HashSet<int>();
        foreach (var p in live.Live)
            foreach (var e in p.Entry.Effects.Where(e => e.Kind == EffectKind.ForceReroll && e.TargetRoll == roll && p.ActedOn(e) == roller))
            {
                var face = e.Conditions.FirstOrDefault(c => c.Kind == ConditionKind.RollResult && c.Values.Count == 2 && c.Values[0] == roll && c.Values[1] == "raw");
                if (face?.Number is { } n && e.Conditions.Where(c => c != face).All(c => Conditions.Holds(live.Interaction, p.Holder, c, p, null))) reroll.Add(n);
            }
        return new RollPlan(roll, threshold, modifiers, convention, requiredRaw, reroll);
    }

    /// <summary>What one die does beyond its success: extra hits and per-hit grants of a rule.</summary>
    public static DieEffects PerDie(LiveSet live, Side roller, Die die, string roll)
    {
        var ctx = new DieContext(roll, die.Raw, die.Modified);
        var extra = live.Effects(roller, EffectKind.AddHit, ctx).Where(x => x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult)).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        var grants = live.Effects(roller, EffectKind.GrantProperty, ctx).Where(x => x.Effect.Conditions.Any(c => c.Kind == ConditionKind.RollResult))
            .Select(x => (x.Effect.TargetEntry!, x.Effect.Amount)).ToList();
        return new DieEffects(extra, grants);
    }

    public static int HitMultiplier(LiveSet live, Side attacker, int defenderModels) => Multiplier(live, attacker, TargetQuantity.Hits, defenderModels);
    public static int WoundMultiplier(LiveSet live, Side attacker) => Multiplier(live, attacker, TargetQuantity.Wounds, int.MaxValue);

    static int Multiplier(LiveSet live, Side side, TargetQuantity quantity, int defenderModels)
    {
        var product = 1;
        foreach (var (src, e) in live.Effects(side, EffectKind.Multiply).Where(x => x.Effect.TargetQuantity == quantity))
        {
            var x = Math.Max(1, e.EffectiveAmount(src.Parameter));
            if (e.CapKind == CapKind.ModelsInTargetUnit) x = Math.Min(x, Math.Max(1, defenderModels));
            product *= x;
        }
        return product;
    }

    /// <summary>Kind-5 rolls a property owns: the property supplies its own dice and threshold, and its child effect is applied once per success.</summary>
    public static IReadOnlyList<(LiveProperty Source, Effect Roll)> OwnRolls(LiveSet live, Side side, Func<Effect, bool> childFilter) =>
        live.Effects(side, EffectKind.Roll).Where(x => x.Effect.Children.Any(childFilter)).ToList();
}
```

`LiveSet.Effects(side, kind, die)` evaluates conditions with the die when one is given; `PerDie` passes it and keeps only effects that actually have a `RollResult` condition, so an unconditional `AddHit` (a spell's *takes 4 hits*, Task 17) is not counted per die.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests --filter RollEffectsTests`; expect 5 passed. Commit: `Apply the live properties to rolls, stats and dice: pass two`.**
