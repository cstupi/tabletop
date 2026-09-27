# Task 6: Pass one — which properties are live

**Files:**
- Create: `server/game/Effects/Interaction.cs`, `server/game/Effects/Conditions.cs`, `server/game/Effects/LiveProperties.cs`, `server/game/Effects/Lookup.cs`
- Test: `tests/Tabletop.GameServer.Tests/LivePropertiesTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 3; `GameState`, `Projection`, `UnitInPlay`, `ModelInPlay`, `Grant`, `OnTable`, `OnShelf`, `Inside` (Protocol); `PropertyInstance` (Content). Test infrastructure `Armies` is written here and used by every later test.
- Produces (namespace `Tabletop.GameServer.Effects`): `enum AttackKind { Shooting, Melee }`; `enum Side { Attacker, Defender }`; `sealed record Party(Guid UnitId, IRL<Guid> ModelIds, IRL<Guid> WeaponIds)` with `static Party.Whole(GameState, Guid unitId)` (every model still in play — on the table or inside a container — no weapons) and `static Party.Of(GameState, Guid unitId, IEnumerable<Guid> modelIds, IEnumerable<Guid> weaponIds)`; `sealed record Interaction(GameState State, PropertySet Properties, Party Attacker, Party? Defender, AttackKind? Attack, string? ActionTaken, double? RangeInches, IRL<string> DefenderTerrain)` with `Party Of(Side)`, `Side Other(Side)`, `int Round`; `sealed record LiveProperty(Side Holder, CatalogEntry Entry, int? Parameter, Guid? ModelId, Guid? WeaponId, Guid? GrantId)` with `Side ActedOn(Effect)`; `sealed class LiveSet(IRL<LiveProperty> Live, IReadOnlySet<TargetModifier> SuppressedModifiers, IRL<(LiveProperty Source, Effect Effect)> Applied)` with `IEnumerable<LiveProperty> On(Side)`, `bool Has(Side, string entry)`, `int? Parameter(Side, string entry)` (summed over instances when `TakesParameter`), `IEnumerable<(LiveProperty Source, Effect Effect)> Effects(Side actedOn, EffectKind kind, DieContext? die = null)` (ordinal order; conditions judged now, with the die when one is given), `LiveSet WithGranted(Side holder, string entry, int? parameter)` (a per-die grant folded in later); `static class Conditions { static bool Hold(Interaction, Side holder, Effect, LiveProperty source, DieContext? die) }` where `sealed record DieContext(string Roll, int Raw, int Modified)`; `static class LiveProperties { static LiveSet Settle(Interaction) }`; `static class Lookup` with `IEnumerable<PropertyInstance> UnitProperties(GameState, PropertySet, Guid unitId)` (the unit's own plus grants as instances, a grant's parameter read from its effect's amount), `IEnumerable<(Guid ModelId, PropertyInstance)> ModelProperties(GameState, Guid unitId, IEnumerable<Guid> modelIds)`, `IEnumerable<(Guid WeaponId, Guid ModelId, PropertyInstance)> WeaponProperties(GameState, IEnumerable<Guid> weaponIds)`, `bool UnitHas(GameState, PropertySet, Guid unitId, string entry)` (own properties, aggregated model properties, the grants store, and for a `unit`-kind entry the unit's model slots), `IEnumerable<(PropertyInstance Property, CatalogEntry Entry)> UnitKindOnSlots(GameState, PropertySet, Guid unitId)` (the `unit`-kind entries on the unit's slots, over the definition), `int? UnitParameter(GameState, PropertySet, Guid unitId, string entry)`, `int? ModelParameter(GameState, Guid modelId, string entry)`, `IRL<Guid> LivingModels(GameState, Guid unitId)` (on the table — the models with a position), `IRL<Guid> AliveModels(GameState, Guid unitId)` (on the table **or** inside a container — every model still in play), `Guid? ContainerOf(GameState, Guid unitId)` (the container its models are inside, null when none is), `IRL<Guid> OccupantUnits(GameState, Guid containerModelId)` (the units with a model inside it, in projection order).
- **Two readings of "alive", and which is which.** [Game flow](../specs/2026-08-28-game-flow-design.md) → Inside a container: a model is in exactly one of three places, and *"a transported model is very much in play"* while the shelf is out of play. So `LivingModels` is *has a position* — every geometric question, every sight blocker — and `AliveModels` is *still in play* — how many models a unit has, who may be assigned a wound, whether a unit is destroyed. `Party.Whole` takes the second, so an occupant unit's own properties are still gathered when a wrecked transport wounds it (Task 19).
- Pass one: gather instances for both parties (unit properties; model properties aggregated per the entry's `Aggregation` over the party's models — `none` keeps one instance per model; weapon properties per weapon id; grants as instances whose `GrantId` is set); collapse duplicates by `(holder, entry)` unless `TakesParameter` (page 13: instances of the same rule don't stack unless it has (X)); then apply, in `Ordinal` order across every effect of kinds `GrantProperty`, `SuppressProperty`, `ModifyParameter`, `SuppressModifiers` whose conditions hold and reference no `RollResult` (those are per-die, Task 7): grant adds an instance on the acted-on side, suppress removes every instance of the entry there, modify-parameter adds to every instance's parameter there, suppress-modifiers records the class. The result is what pass two consumes.

Spec: effect grammar → Subject (`self`/`opponent` resolved from where the property is attached), Conditions (flat list, all hold; relational predicates take no subject), Properties act on other properties (two passes; `Bane` deletes `Regeneration`; `Thrust` changes what `AP` contributes), Aggregation is not a predicate; identity and enumerations → The Stealth case; spell effects → The grants store ("read by the two effect passes alongside the unit's own properties, and by `GameRules`' name checks"); rulebook p.13 (instances don't stack unless (X)).

- [ ] **Step 1: Write the army builder the effect and sequence tests share, `Infrastructure/Armies.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>Units with named properties, placed on a 72x48 table. Team1's participant is P1; Team2's is P2.</summary>
public static class Armies
{
    public static readonly Guid P2 = Guid.CreateVersion7();
    public static PropertyInstance P(string name, int? parameter = null) => new(name, parameter);
    public static Weapon Gun(string name, string range, int attacks, params PropertyInstance[] props) =>
        new(Guid.CreateVersion7(), name, new Dictionary<string, string> { ["Range"] = range, ["Attacks"] = attacks.ToString() }, props);
    public static Weapon Melee(string name, int attacks, params PropertyInstance[] props) =>
        new(Guid.CreateVersion7(), name, new Dictionary<string, string> { ["Attacks"] = attacks.ToString() }, props);

    public static Unit Unit(string name, string quality, string defense, PropertyInstance[] unitProps, int models, PropertyInstance[] modelProps, params Weapon[] weapons) =>
        new(Guid.CreateVersion7(), 1, name, new Dictionary<string, string> { ["Quality"] = quality, ["Defense"] = defense }, unitProps,
            Enumerable.Range(1, models).Select(i => new ModelSlot(Guid.CreateVersion7(), i, $"{name} {i}", new Dictionary<string, string>(), modelProps, Base.Circle(1.26), 1.5, null,
                weapons.Select(w => w with { Id = Guid.CreateVersion7() }).ToList())).ToList());

    /// <summary>A playing game: two armies, every model deployed in a row at the given x, one inch apart along y.</summary>
    public static GameState State(IReadOnlyList<Unit> team1, IReadOnlyList<Unit> team2, double x1 = -12, double x2 = 12, Enforcement enforcement = Enforcement.Warn, bool waits = false)
    {
        var info = Tabletop.Protocol.Tests.Samples.Game with { Enforcement = enforcement, WaitsBetweenSteps = waits,
            Participants = [new Participant(Tabletop.Protocol.Tests.Samples.P1, Guid.CreateVersion7(), "Clay", Tabletop.Protocol.Tests.Samples.Team1, null), new Participant(P2, Guid.CreateVersion7(), "Opponent", Tabletop.Protocol.Tests.Samples.Team2, null)] };
        var board = new Board(Tabletop.Content.Tests.TableStateTests.Sample() with { Terrain = [] },
            [new ArmyInPlay(Tabletop.Protocol.Tests.Samples.P1, new ArmySnapshot(Guid.CreateVersion7(), "One", "grimdark-future", team1)), new ArmyInPlay(P2, new ArmySnapshot(Guid.CreateVersion7(), "Two", "grimdark-future", team2))]);
        var applier = new EventApplier(board, info);
        var moves = new List<ModelRelocation>();
        foreach (var (units, x) in new[] { (team1, x1), (team2, x2) })
            foreach (var (u, ui) in units.Select((u, i) => (u, i)))
                foreach (var m in u.Models) moves.Add(new ModelRelocation(m.Id, new OnShelf(), new OnTable(new Placement(new Point3(x, ui * 12 + m.Ordinal * 1.5 - 20, 0), new Yaw(0)))));
        applier.Apply(new GameEvent(1, null, new ModelsMoved(moves), null));
        return new GameState(info, board, applier, GrimdarkFutureRules.Instance);
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class LivePropertiesTests
{
    static readonly PropertySet Core = TestProperties.Core;

    [Fact]
    public void Bane_suppresses_regeneration_and_thrust_changes_what_ap_contributes()
    {
        var lancers = Unit("Lancers", "4+", "4+", [], 3, [], Melee("Thrust", 1, P("AP", 1), P("Thrust"), P("Bane")));
        var trolls = Unit("Trolls", "4+", "4+", [], 2, [P("Regeneration"), P("Tough", 3)]);
        var state = State([lancers], [trolls]);
        var weapons = state.Projection.Units[lancers.Id].Definition.Models.SelectMany(m => m.Weapons.Select(w => w.Id));
        var ix = new Interaction(state, Core, Party.Of(state, lancers.Id, lancers.Models.Select(m => m.Id), weapons), Party.Whole(state, trolls.Id), AttackKind.Melee, "Charge", 1, []);
        var live = LiveProperties.Settle(ix);
        Assert.False(live.Has(Side.Defender, "Regeneration"));
        Assert.True(live.Has(Side.Defender, "Tough"));
        Assert.Equal(2, live.Parameter(Side.Attacker, "AP"));                                       // AP(1) plus Thrust's granted AP(1), summed by Parameter
        Assert.Equal(2, live.Applied.Count(a => a.Effect.Kind is EffectKind.SuppressProperty or EffectKind.GrantProperty));
        var shooting = LiveProperties.Settle(ix with { Attack = AttackKind.Shooting, ActionTaken = "Hold" });
        Assert.Equal(1, shooting.Parameter(Side.Attacker, "AP"));
        Assert.Equal(6, live.Parameter(Side.Defender, "Tough"));                                   // summed over two Tough(3) models
    }

    // 3.5.1 writes Thrust as AP(+1), so it adds AP to a weapon that had none. A grant does that;
    // modify_parameter did not, because it adds to matching instances and there were none.
    [Fact]
    public void Thrust_grants_ap_to_a_weapon_that_has_none()
    {
        var militia = Unit("Militia", "5+", "5+", [], 3, [], Melee("Club", 1, P("Thrust")));
        var trolls = Unit("Trolls", "4+", "4+", [], 2, []);
        var state = State([militia], [trolls]);
        var weapons = state.Projection.Units[militia.Id].Definition.Models.SelectMany(m => m.Weapons.Select(w => w.Id));
        var ix = new Interaction(state, Core, Party.Of(state, militia.Id, militia.Models.Select(m => m.Id), weapons), Party.Whole(state, trolls.Id), AttackKind.Melee, "Charge", 1, []);
        Assert.Equal(1, LiveProperties.Settle(ix).Parameter(Side.Attacker, "AP"));
        Assert.Null(LiveProperties.Settle(ix with { ActionTaken = "Advance" }).Parameter(Side.Attacker, "AP"));
    }

    [Fact]
    public void Model_properties_aggregate_and_instances_of_a_plain_rule_do_not_stack()
    {
        var mixed = Unit("Mixed", "4+", "4+", [], 3, [P("Stealth"), P("Fearless"), P("Tough", 3)]);
        var stealthy = mixed with { Models = mixed.Models.Select((m, i) => i == 0 ? m with { Properties = [P("Fearless"), P("Tough", 3)] } : m).ToList() };
        var state = State([Unit("Shooters", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1))], [stealthy]);
        var ix = new Interaction(state, Core, Party.Whole(state, state.Board.Armies[0].Snapshot.Units[0].Id), Party.Whole(state, stealthy.Id), AttackKind.Shooting, "Hold", 12, []);
        var live = LiveProperties.Settle(ix);
        Assert.False(live.Has(Side.Defender, "Stealth"));                                           // all: one model lacks it
        Assert.True(live.Has(Side.Defender, "Fearless"));                                           // all, and all three carry it (3.5.1: "a unit where all models have this rule")
        Assert.Equal(3, live.On(Side.Defender).Count(p => p.Entry.Name == "Tough"));                // none: per model, all three have it
        Assert.Single(live.On(Side.Defender).Where(p => p.Entry.Name == "Fearless"));               // collapsed: no (X)
    }

    [Fact]
    public void Grants_are_read_like_own_properties_and_an_opponent_grant_acts_on_the_other_side()
    {
        var a = Unit("A", "4+", "4+", [], 2, [], Melee("Claws", 1));
        var b = Unit("B", "4+", "4+", [], 2, []);
        var state = State([a], [b]);
        var apGrant = new Grant(Guid.CreateVersion7(), Core.Entry("Rending")!.Effects[1].Id, "Regeneration", a.Id, 1);   // granted Regeneration on B (entry name is what matters)
        state.Applier.Apply(new GameEvent(2, null, new GrantAdded(b.Id, apGrant), null));
        Assert.True(Lookup.UnitHas(state, Core, b.Id, "Regeneration"));
        Assert.False(Lookup.UnitHas(state, Core, a.Id, "Regeneration"));
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, a.Id), Party.Whole(state, b.Id), AttackKind.Melee, "Charge", 1, []));
        Assert.Equal(apGrant.Id, live.On(Side.Defender).Single(p => p.Entry.Name == "Regeneration").GrantId);
    }

    [Fact]
    public void A_unit_kind_rule_on_one_model_slot_is_the_whole_units_rule_in_both_readers()
    {
        var squad = Unit("Squad", "4+", "4+", [], 3, []);
        var joined = squad with { Models = squad.Models.Select((m, i) => i == 0
            ? m with { Name = "Captain", Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "3+" }, Properties = [P("Ambush"), P("Aircraft"), P("Hero")] }
            : m).ToList() };
        var shooters = Unit("Shooters", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var state = State([shooters], [joined]);
        Assert.True(Lookup.UnitHas(state, Core, joined.Id, "Ambush"));                              // unit kind on one slot: the unit's, so the scheduler's deployment set sees it
        Assert.False(Lookup.UnitHas(state, Core, joined.Id, "Scout"));
        Assert.False(Lookup.UnitHas(state, Core, shooters.Id, "Ambush"));
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, shooters.Id), Party.Whole(state, joined.Id), AttackKind.Shooting, "Hold", 12, []));
        Assert.True(live.Has(Side.Defender, "Ambush"));                                             // the effect pass agrees with the name lookup
        var aircraft = live.On(Side.Defender).Single(p => p.Entry.Name == "Aircraft");
        Assert.Null(aircraft.ModelId);                                                              // holder-less: it is the unit's, not the captain's
        Assert.Equal(-12, live.Effects(Side.Attacker, EffectKind.ModifyStat).Single(x => x.Source.Entry.Name == "Aircraft").Effect.Amount);   // 3.5.1: -12" to range, and no -1 to hit
        Assert.Empty(live.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Source.Entry.Name == "Aircraft"));
        // The captain's own Quality is declared at model level and read: determine-attacks splits the group's attacks by it and roll-to-hit rolls a pool per value (p.4, p.14). Defense stays the unit's until it is alone.
        Assert.NotNull(Core.Stat(StatLevel.Model, "Quality"));
        Assert.Equal("3+", state.Projection.Models[joined.Models[0].Id].Slot.Stats["Quality"]);
        Assert.Equal("4+", state.Projection.Units[joined.Id].Definition.Stats["Quality"]);
    }

    [Fact]
    public void Conditions_read_range_action_attack_kind_and_actor_state()
    {
        var e = Unit("Gun crew", "4+", "4+", [], 2, [P("Artillery"), P("Artillery")]);
        var s = Unit("Shooters", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var state = State([s], [e]);
        Interaction Ix(double range, string action) => new(state, Core, Party.Whole(state, s.Id), Party.Whole(state, e.Id), AttackKind.Shooting, action, range, []);
        // Artillery's second effect is the -2 on the opponent, conditioned on shooting from over 9" away. 3.5.1 dropped Entrenched's "hasn't moved" condition with the rule itself.
        var artillery = Core.Entry("Artillery")!.Effects[1];
        var holder = new LiveProperty(Side.Defender, Core.Entry("Artillery")!, null, e.Models[0].Id, null, null);
        Assert.True(Conditions.Hold(Ix(12, "Hold"), Side.Defender, artillery, holder, null));
        Assert.False(Conditions.Hold(Ix(9, "Hold"), Side.Defender, artillery, holder, null));
        // actor_state is read through Impact's "unless fatigued", the only core rule that carries the predicate.
        var impact = Core.Entry("Impact")!.Effects[0];
        var ih = new LiveProperty(Side.Attacker, Core.Entry("Impact")!, 3, null, null, null);
        Assert.True(Conditions.Hold(Ix(1, "Charge") with { Attack = AttackKind.Melee }, Side.Attacker, impact, ih, null));
        state.Applier.Apply(new GameEvent(2, null, new ConditionSet(new Subject(SubjectKind.Unit, s.Id), "fatigued", true), null));
        Assert.False(Conditions.Hold(Ix(1, "Charge") with { Attack = AttackKind.Melee }, Side.Attacker, impact, ih, null));
        var furious = Core.Entry("Furious")!.Effects[0];
        var fh = new LiveProperty(Side.Attacker, Core.Entry("Furious")!, null, null, null, null);
        Assert.True(Conditions.Hold(Ix(1, "Charge") with { Attack = AttackKind.Melee }, Side.Attacker, furious, fh, new DieContext("hit", 6, 5)));
        Assert.False(Conditions.Hold(Ix(1, "Charge") with { Attack = AttackKind.Melee }, Side.Attacker, furious, fh, new DieContext("hit", 5, 6)));
        Assert.False(Conditions.Hold(Ix(1, "Charge") with { Attack = AttackKind.Melee }, Side.Attacker, furious, fh, null));   // a roll_result predicate cannot hold before the die
    }

    [Fact]
    public void A_model_inside_a_container_is_still_in_play_but_has_no_position()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 2, [P("Regeneration")]);
        var state = State([bus, troops], [Unit("T", "4+", "4+", [], 1, [])]);
        var container = bus.Models[0].Id;
        state.Applier.Apply(new GameEvent(2, null, new ModelsMoved(troops.Models
            .Select(m => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location, new Inside(container))).ToList()), null));
        Assert.Empty(Lookup.LivingModels(state, troops.Id));                                       // no position: no distance, no line of sight, no cover
        Assert.Equal(2, Lookup.AliveModels(state, troops.Id).Count);                               // still in play
        Assert.Equal(container, Lookup.ContainerOf(state, troops.Id));
        Assert.Null(Lookup.ContainerOf(state, bus.Id));
        Assert.Equal([troops.Id], Lookup.OccupantUnits(state, container));
        // Party.Whole is the in-play reading, so an occupant's own properties still settle — which is what lets a wrecked transport wound it.
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, troops.Id), null, null, null, null, []));
        Assert.Equal(2, live.On(Side.Attacker).Count(p => p.Entry.Name == "Regeneration"));
    }
}
```

- [ ] **Step 3: Run; expect compile errors. Write `Interaction.cs` and `Lookup.cs`**

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

public enum AttackKind { Shooting, Melee }
public enum Side { Attacker, Defender }

/// <summary>One side of an interaction: the unit, the models taking part, the weapons in use.</summary>
public sealed record Party(Guid UnitId, IReadOnlyList<Guid> ModelIds, IReadOnlyList<Guid> WeaponIds)
{
    public static Party Whole(GameState state, Guid unitId) => new(unitId, Lookup.AliveModels(state, unitId), []);
    public static Party Of(GameState state, Guid unitId, IEnumerable<Guid> modelIds, IEnumerable<Guid> weaponIds) => new(unitId, modelIds.ToList(), weaponIds.ToList());
}

/// <summary>What the two passes evaluate against. Defender is null for a lone roll (morale, casting, dangerous terrain).</summary>
public sealed record Interaction(GameState State, PropertySet Properties, Party Attacker, Party? Defender, AttackKind? Attack, string? ActionTaken, double? RangeInches, IReadOnlyList<string> DefenderTerrain)
{
    public Party Of(Side side) => side == Side.Attacker ? Attacker : Defender ?? throw new InvalidOperationException("no defender in this interaction");
    public static Side Other(Side side) => side == Side.Attacker ? Side.Defender : Side.Attacker;
    public int Round => State.Projection.Scheduler.Round;
}
```

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

/// <summary>Name checks and instance gathering over a unit's own properties, its models' and weapons', and the grants store.</summary>
public static class Lookup
{
    /// <summary>The unit's models that have a position. Every geometric question asks this one.</summary>
    public static IReadOnlyList<Guid> LivingModels(GameState state, Guid unitId) =>
        state.Projection.Models.Values.Where(m => m.UnitId == unitId && m.Location is OnTable).Select(m => m.Id).ToList();

    /// <summary>The unit's models still in play — on the table or inside a container. Counts, wound allocation and "is it destroyed" ask this one (game flow → Inside a container: the shelf is out of play; a transported model is not).</summary>
    public static IReadOnlyList<Guid> AliveModels(GameState state, Guid unitId) =>
        state.Projection.Models.Values.Where(m => m.UnitId == unitId && m.Location is not OnShelf).Select(m => m.Id).ToList();

    /// <summary>The container this unit is inside, or null. A unit embarks and disembarks whole, so the first occupant model answers for all.</summary>
    public static Guid? ContainerOf(GameState state, Guid unitId) =>
        state.Projection.Models.Values.FirstOrDefault(m => m.UnitId == unitId && m.Location is Inside) is { Location: Inside i } ? i.ContainerId : null;

    /// <summary>The units with at least one model inside the given container model, in projection order.</summary>
    public static IReadOnlyList<Guid> OccupantUnits(GameState state, Guid containerModelId) =>
        state.Projection.Models.Values.Where(m => m.Location is Inside i && i.ContainerId == containerModelId).Select(m => m.UnitId).Distinct().ToList();

    /// <summary>The unit's own properties plus its grants as instances. A grant's parameter is the granting effect's amount ("gets AP(1)" → AP with parameter 1), read from the pinned set by Grant.EffectId.</summary>
    public static IEnumerable<PropertyInstance> UnitProperties(GameState state, PropertySet props, Guid unitId)
    {
        var unit = state.Projection.Units[unitId];
        return unit.Definition.Properties.Concat(unit.Grants.Select(g => new PropertyInstance(g.EntryName, props.AllEffects().FirstOrDefault(e => e.Id == g.EffectId)?.Amount)));
    }

    public static IEnumerable<(Guid ModelId, PropertyInstance Property)> ModelProperties(GameState state, Guid unitId, IEnumerable<Guid> modelIds) =>
        modelIds.SelectMany(id => state.Projection.Models[id].Slot.Properties.Select(p => (id, p)));

    public static IEnumerable<(Guid WeaponId, Guid ModelId, PropertyInstance Property)> WeaponProperties(GameState state, IEnumerable<Guid> weaponIds)
    {
        var wanted = weaponIds.ToHashSet();
        return state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons.Where(w => wanted.Contains(w.Id)).SelectMany(w => w.Properties.Select(p => (w.Id, m.Id, p))));
    }

    /// <summary>Own properties, model properties under the entry's aggregation over living models, and grants. A unit-kind entry on a model slot is
    /// the unit's rule and is read off the definition, which is the reading LiveProperties.Gather takes as well.</summary>
    public static bool UnitHas(GameState state, PropertySet props, Guid unitId, string entry)
    {
        if (UnitProperties(state, props, unitId).Any(p => p.Name == entry)) return true;
        if (props.Entry(entry) is not { } e) return false;
        if (e.Kind == CatalogKind.Unit) return UnitKindOnSlots(state, props, unitId).Any(x => x.Property.Name == entry);
        var living = LivingModels(state, unitId);
        var with = living.Count(id => state.Projection.Models[id].Slot.Properties.Any(p => p.Name == entry));
        return e.Aggregation switch
        {
            Aggregation.All => living.Count > 0 && with == living.Count, Aggregation.Any or Aggregation.None => with > 0,
            Aggregation.Majority => with * 2 > living.Count, _ => false,
        };
    }

    /// <summary>The unit-kind properties written onto the unit's model slots, over the definition rather than the projection: a rule scoped to the unit
    /// does not depend on which of its models are standing, and a deployment rule is read before anything has a position at all.</summary>
    public static IEnumerable<(PropertyInstance Property, CatalogEntry Entry)> UnitKindOnSlots(GameState state, PropertySet props, Guid unitId) =>
        state.Projection.Units[unitId].Definition.Models.SelectMany(m => m.Properties)
            .Select(p => (Property: p, Entry: props.Entry(p.Name)))
            .Where(x => x.Entry is { Kind: CatalogKind.Unit })
            .Select(x => (x.Property, Entry: x.Entry!));

    public static int? UnitParameter(GameState state, PropertySet props, Guid unitId, string entry)
    {
        var own = UnitProperties(state, props, unitId).Where(p => p.Name == entry).Select(p => p.Parameter).ToList();
        if (own.Count > 0) return own.Sum(p => p ?? 0);
        var model = LivingModels(state, unitId).Select(id => ModelParameter(state, id, entry)).Where(p => p is not null).ToList();
        return model.Count == 0 ? null : model.Sum(p => p ?? 0);
    }

    public static int? ModelParameter(GameState state, Guid modelId, string entry) =>
        state.Projection.Models[modelId].Slot.Properties.FirstOrDefault(p => p.Name == entry)?.Parameter;
}
```

- [ ] **Step 4: Write `Conditions.cs`**

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

public sealed record DieContext(string Roll, int Raw, int Modified);

/// <summary>The eight predicates. A subject-bearing predicate tests the side it names relative to the holder; relational ones test the interaction.</summary>
public static class Conditions
{
    public static bool Hold(Interaction ix, Side holder, Effect effect, LiveProperty source, DieContext? die) =>
        effect.Conditions.All(c => Holds(ix, holder, c, source, die));

    public static bool Holds(Interaction ix, Side holder, EffectCondition c, LiveProperty source, DieContext? die)
    {
        var side = c.Subject == EffectSubject.Opponent ? Interaction.Other(holder) : holder;
        switch (c.Kind)
        {
            case ConditionKind.ActionTaken: return ix.ActionTaken is { } a && Membership(c, a);
            case ConditionKind.AttackKind: return ix.Attack is { } k && Membership(c, k == AttackKind.Shooting ? "shooting" : "melee");
            case ConditionKind.Range: return ix.RangeInches is { } r && c.Compare((int)Math.Floor(r));
            case ConditionKind.RollResult:
                if (die is null || c.Values.Count != 2 || die.Roll != c.Values[0]) return false;
                return c.Compare(c.Values[1] == "raw" ? die.Raw : die.Modified);
            case ConditionKind.ActorState:
            {
                var unit = ix.State.Projection.Units[ix.Of(side).UnitId];
                var present = c.Values.Any(v => unit.Conditions.Contains(v) || (source.ModelId is { } m && ix.Of(side).ModelIds.Contains(m) && ix.State.Projection.Models[m].Conditions.Contains(v)));
                return c.Comparison == Comparison.Neq ? !present : present;
            }
            // The terrain list describes the defender, or the lone party when there is no defender (morale, a move, a dangerous terrain test).
            case ConditionKind.PositionTerrain: return Membership(c, ix.DefenderTerrain, ix.Defender is null || side == Side.Defender);
            case ConditionKind.RoundNumber: return c.Compare(ix.Round);
            case ConditionKind.UsageCount: return c.Compare(Usage(ix, source));
            default: return false;
        }
    }

    static bool Membership(EffectCondition c, string actual) => c.Comparison == Comparison.Neq ? !c.Values.Contains(actual) : c.Values.Contains(actual);
    static bool Membership(EffectCondition c, IReadOnlyList<string> actual, bool applies) => applies && (c.Comparison == Comparison.Neq ? !actual.Intersect(c.Values).Any() : actual.Intersect(c.Values).Any());

    /// <summary>How many sequences in the live log have used this weapon: the usage record Limited reads.</summary>
    static int Usage(Interaction ix, LiveProperty source) => source.WeaponId is not { } w ? 0
        : EventApplier.EffectiveLog(ix.State.Applier.Log).Count(e => e.Payload is SequenceStarted s && s.Context.WeaponIds.Contains(w));
}
```

- [ ] **Step 5: Write `LiveProperties.cs`**

```csharp
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Effects;

/// <summary>One instance of a catalog entry in play: who holds it and where it came from.</summary>
public sealed record LiveProperty(Side Holder, CatalogEntry Entry, int? Parameter, Guid? ModelId, Guid? WeaponId, Guid? GrantId)
{
    public Side ActedOn(Effect e) => e.Subject == EffectSubject.Self ? Holder : Interaction.Other(Holder);
}

/// <summary>Pass one's result: the properties that are live, the modifier classes suppressed, and what was applied to get there.</summary>
public sealed class LiveSet(Interaction interaction, IReadOnlyList<LiveProperty> live, IReadOnlySet<TargetModifier> suppressedModifiers, IReadOnlyList<(LiveProperty Source, Effect Effect)> applied)
{
    public Interaction Interaction { get; } = interaction;
    public IReadOnlyList<LiveProperty> Live { get; } = live;
    public IReadOnlySet<TargetModifier> SuppressedModifiers { get; } = suppressedModifiers;
    public IReadOnlyList<(LiveProperty Source, Effect Effect)> Applied { get; } = applied;

    public IEnumerable<LiveProperty> On(Side side) => Live.Where(p => p.Holder == side);
    public bool Has(Side side, string entry) => On(side).Any(p => p.Entry.Name == entry);
    public int? Parameter(Side side, string entry)
    {
        var instances = On(side).Where(p => p.Entry.Name == entry).ToList();
        return instances.Count == 0 ? null : instances.Sum(p => p.Parameter ?? 0);
    }

    /// <summary>Effects of one kind acting on a side, in ordinal order, whose conditions hold now (a per-die condition is judged with the die).</summary>
    public IEnumerable<(LiveProperty Source, Effect Effect)> Effects(Side actedOn, EffectKind kind, DieContext? die = null) =>
        Live.SelectMany(p => p.Entry.Effects.Where(e => e.Kind == kind && p.ActedOn(e) == actedOn && Conditions.Hold(Interaction, p.Holder, e, p, die)).Select(e => (p, e)))
            .OrderBy(x => x.e.Ordinal).Select(x => (x.p, x.e));

    public LiveSet WithGranted(Side holder, string entry, int? parameter) =>
        Interaction.Properties.Entry(entry) is { } ce ? new LiveSet(Interaction, [.. Live, new LiveProperty(holder, ce, parameter, null, null, null)], SuppressedModifiers, Applied) : this;
}

public static class LiveProperties
{
    static readonly EffectKind[] PassOne = [EffectKind.GrantProperty, EffectKind.SuppressProperty, EffectKind.ModifyParameter, EffectKind.SuppressModifiers];

    public static LiveSet Settle(Interaction ix)
    {
        var live = Gather(ix, Side.Attacker).ToList();
        if (ix.Defender is not null) live.AddRange(Gather(ix, Side.Defender));
        var suppressed = new HashSet<TargetModifier>();
        var applied = new List<(LiveProperty, Effect)>();
        // Every pass-one effect whose conditions hold now, across both sides, in ordinal order. Per-die grants (a roll_result condition) wait for pass two.
        var candidates = live.SelectMany(p => p.Entry.Effects.Where(e => PassOne.Contains(e.Kind) && e.Conditions.All(c => c.Kind != ConditionKind.RollResult)).Select(e => (p, e)))
            .OrderBy(x => x.e.Ordinal).ToList();
        foreach (var (source, effect) in candidates)
        {
            if (!live.Contains(source) || !Conditions.Hold(ix, source.Holder, effect, source, null)) continue;   // suppressed earlier this pass, or conditions fail
            var side = source.ActedOn(effect);
            switch (effect.Kind)
            {
                case EffectKind.GrantProperty when ix.Properties.Entry(effect.TargetEntry!) is { } granted:
                    live.Add(new LiveProperty(side, granted, effect.Amount, source.ModelId, source.WeaponId, null)); break;
                case EffectKind.SuppressProperty: live.RemoveAll(p => p.Holder == side && p.Entry.Name == effect.TargetEntry); break;
                case EffectKind.ModifyParameter:
                    for (var i = 0; i < live.Count; i++)
                        if (live[i].Holder == side && live[i].Entry.Name == effect.TargetEntry) live[i] = live[i] with { Parameter = (live[i].Parameter ?? 0) + effect.EffectiveAmount(source.Parameter) };
                    break;
                case EffectKind.SuppressModifiers: suppressed.Add(effect.TargetModifier!.Value); break;
            }
            applied.Add((source, effect));
        }
        return new LiveSet(ix, live, suppressed, applied);
    }

    /// <summary>Unit properties and grants, unit-kind properties on the unit's slots, model properties under aggregation, weapon properties;
    /// duplicates collapsed unless the rule takes (X).</summary>
    static IEnumerable<LiveProperty> Gather(Interaction ix, Side side)
    {
        var party = ix.Of(side);
        var state = ix.State;
        var unit = state.Projection.Units[party.UnitId];
        var result = new List<LiveProperty>();
        foreach (var p in unit.Definition.Properties) if (ix.Properties.Entry(p.Name) is { } e) result.Add(new LiveProperty(side, e, p.Parameter, null, null, null));
        foreach (var g in unit.Grants) if (ix.Properties.Entry(g.EntryName) is { } e)
            result.Add(new LiveProperty(side, e, ix.Properties.AllEffects().FirstOrDefault(x => x.Id == g.EffectId)?.Amount, null, null, g.Id));
        // A unit-kind entry on a model slot is the unit's rule, not a per-model one, and aggregation is not declared for the kind. Import produces the
        // shape two ways — a rule whose count is smaller than the unit, and a joined hero, whose rules all land on its slot whatever their kind — so it
        // is read off the definition, holder-less, and Lookup.UnitHas reads it the same way. Its effects therefore act on the whole unit.
        foreach (var (p, e) in Lookup.UnitKindOnSlots(state, ix.Properties, party.UnitId)) result.Add(new LiveProperty(side, e, p.Parameter, null, null, null));
        var models = party.ModelIds.Count > 0 ? party.ModelIds : Lookup.LivingModels(state, party.UnitId);
        foreach (var group in Lookup.ModelProperties(state, party.UnitId, models).GroupBy(x => x.Property.Name))
        {
            if (ix.Properties.Entry(group.Key) is not { } e || e.Kind == CatalogKind.Unit) continue;   // added above, off the definition
            var count = group.Select(x => x.ModelId).Distinct().Count();
            var live = e.Aggregation switch { Aggregation.All => count == models.Count, Aggregation.Any => count > 0, Aggregation.Majority => count * 2 > models.Count, _ => true };
            if (!live) continue;
            if (e.Aggregation == Aggregation.None) result.AddRange(group.Select(x => new LiveProperty(side, e, x.Property.Parameter, x.ModelId, null, null)));
            else result.Add(new LiveProperty(side, e, group.First().Property.Parameter, null, null, null));
        }
        foreach (var (weaponId, modelId, p) in Lookup.WeaponProperties(state, party.WeaponIds))
            if (ix.Properties.Entry(p.Name) is { } e) result.Add(new LiveProperty(side, e, p.Parameter, modelId, weaponId, null));
        // Page 13: instances of the same rule don't stack unless it has (X). Per-model rules (aggregation none) keep one instance per model;
        // a weapon rule is one rule per weapon *type*, so three Lances' AP(1) are one AP(1) and the group's parameter is the weapon's, not the sum.
        string WeaponName(Guid? id) => id is { } w ? state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).First(x => x.Id == w).Name : "";
        return result.GroupBy(p => (p.Entry.Name, p.Entry.Aggregation == Aggregation.None ? p.ModelId ?? Guid.Empty : Guid.Empty, p.WeaponId is null ? "" : WeaponName(p.WeaponId), p.GrantId ?? Guid.Empty))
            .Select(g => g.First());
    }
}
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.GameServer.Tests --filter LivePropertiesTests`; expect 7 passed. Commit: `Settle which properties are live: pass one of the effect grammar`.**
