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
        Assert.False(live.Has(Side.Defender, "Stealth"));                                           // all: one model lacks it (3.5.1 p.14: "units where all models"; 3.6.0 p.15: "most models", under which two of three would carry it)
        Assert.True(live.Has(Side.Defender, "Fearless"));                                           // all, and all three carry it (3.5.1: "a unit where all models have this rule"; 3.6.0 p.14: "most models")
        Assert.Equal(3, live.On(Side.Defender).Count(p => p.Entry.Name == "Tough"));                // none: per model, all three have it
        Assert.Single(live.On(Side.Defender).Where(p => p.Entry.Name == "Fearless"));               // collapsed: no (X)
    }

    [Fact]
    public void Grants_are_read_like_own_properties_and_an_opponent_grant_acts_on_the_other_side()
    {
        var a = Unit("A", "4+", "4+", [], 2, [], Melee("Claws", 1));
        var b = Unit("B", "4+", "4+", [], 2, []);
        var state = State([a], [b]);
        var spell = new EntryDocument("Test Mend", CatalogKind.Spell, false, null, 1, null, "Target friendly unit gets Regeneration once.", new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12),
            [new EffectDocument(EffectSubject.Self, 10, EffectKind.GrantProperty, null, null, "Regeneration", null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], [])]);
        var props = TestProperties.WithSpells(spell);
        var apGrant = new Grant(Guid.CreateVersion7(), props.Entry("Test Mend")!.Effects[0].Id, "Test Mend", a.Id, 1);   // the spell's once grant_property Regeneration row, held by B
        state.Applier.Apply(new GameEvent(2, null, new GrantAdded(b.Id, apGrant), null));
        Assert.True(Lookup.UnitHas(state, props, b.Id, "Regeneration"));
        Assert.False(Lookup.UnitHas(state, props, a.Id, "Regeneration"));
        var live = LiveProperties.Settle(new Interaction(state, props, Party.Whole(state, a.Id), Party.Whole(state, b.Id), AttackKind.Melee, "Charge", 1, []));
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
        Assert.Equal(-12, live.Effects(Side.Attacker, EffectKind.ModifyStat).Single(x => x.Source.Entry.Name == "Aircraft").Effect.Amount);   // 3.5.1: -12" to range, and no -1 to hit (3.6.0 p.13 adds -1 to hit rolls)
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
        // Artillery's second effect is the -2 on the opponent, conditioned on shooting from over 9" away (3.6.0 p.13: -1, over 12", only while the unit has not moved this round). 3.5.1 dropped Entrenched's "hasn't moved" condition with the rule itself.
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
        Assert.True(Conditions.Hold(Ix(1, "Charge") with { Attack = AttackKind.Melee }, Side.Attacker, furious, fh, new DieContext("hit", 6, 5, true)));
        Assert.False(Conditions.Hold(Ix(1, "Charge") with { Attack = AttackKind.Melee }, Side.Attacker, furious, fh, new DieContext("hit", 5, 6, true)));
        Assert.False(Conditions.Hold(Ix(1, "Charge") with { Attack = AttackKind.Melee }, Side.Attacker, furious, fh, null));   // a roll_result predicate cannot hold before the die
    }

    [Fact]
    public void A_model_inside_a_container_is_still_in_play_but_has_no_position()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var troops = Unit("Troops", "4+", "4+", [], 2, [P("Regeneration"), P("Tough", 3)]);
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
        // R-F8: Regeneration is authored aggregation:all, so two models carrying it collapse to a single live instance; Tough is
        // aggregation:none, so both models keep their own instance. Both branches of Gather's collapse are exercised here.
        Assert.Equal(1, live.On(Side.Attacker).Count(p => p.Entry.Name == "Regeneration"));
        Assert.Equal(2, live.On(Side.Attacker).Count(p => p.Entry.Name == "Tough"));
    }

    // "those hits count as having AP(-1), to a min. of AP(0)" (3.6.0 Fortified, Fortified Growth, Guardian, Primeborn): a floor on modify_parameter.
    static EntryDocument Dampen(string name, int? floor) => new(name, CatalogKind.Model, false, Aggregation.All, null, null, $"{name}.", null,
        [new EffectDocument(EffectSubject.Opponent, 10, EffectKind.ModifyParameter, null, null, "AP", null, null, null, null, -1, false, null, null, null, EffectDuration.Interaction, [], [], Floor: floor),
         new EffectDocument(EffectSubject.Opponent, 20, EffectKind.ModifyParameter, null, null, "AP", null, null, null, null, -1, false, null, null, null, EffectDuration.Interaction, [], [], Floor: floor)]);

    [Fact]
    public void A_floor_on_modify_parameter_holds_ap_at_zero()
    {
        var props = TestProperties.WithSpells(Dampen("Floored Dampen", 0), Dampen("Open Dampen", null));
        int? Ap(string rule)
        {
            var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("AP", 1)));
            var b = Unit("B", "4+", "4+", [], 1, [P(rule)]);
            var s = State([a], [b]);
            var weapons = s.Projection.Units[a.Id].Definition.Models.SelectMany(m => m.Weapons.Select(w => w.Id));
            return LiveProperties.Settle(new Interaction(s, props, Party.Of(s, a.Id, a.Models.Select(m => m.Id), weapons), Party.Whole(s, b.Id), AttackKind.Shooting, "Hold", 12, [])).Parameter(Side.Attacker, "AP");
        }
        Assert.Equal(0, Ap("Floored Dampen"));
        Assert.Equal(-1, Ap("Open Dampen"));
    }

    /// <summary>Every 3.5.1 entry printing "to a min. of AP(0)" (Fortified, Fortified Growth, Guardian, Primeborn) floors each of its AP rows at zero.</summary>
    [Fact]
    public void The_351_min_ap0_rows_carry_a_floor_of_zero()
    {
        var entries = TestProperties.CoreDocument.Entries.Where(e => e.RuleText?.Contains("to a min. of AP(0)") == true).ToList();
        Assert.Equal(["Fortified", "Fortified Growth", "Guardian", "Primeborn"], entries.Select(e => e.Name).Order(StringComparer.Ordinal));
        foreach (var entry in entries)
        {
            var rows = entry.Effects.Where(e => e.Kind == EffectKind.ModifyParameter && e.TargetEntry == "AP").ToList();
            Assert.NotEmpty(rows);
            Assert.All(rows, e => Assert.Equal(0, e.Floor));
        }
    }

    /// <summary>
    /// A rule that changes what AP a unit gets "from Guardian" or "from Fortified" (3.6.0 Enhanced Guardian, 3.5.1 Guardian Boost) suppresses
    /// that rule and adds its own AP rows, which keep its "to a min. of AP(0)".
    /// </summary>
    [Fact]
    public void The_rows_that_count_as_from_guardian_or_fortified_carry_a_floor_of_zero()
    {
        foreach (var (document, expected) in new[] { (TestProperties.CoreDocument, "Guardian Boost"), (TestProperties.CoreDocument36, "Enhanced Guardian") })
        {
            var entries = document.Entries.Where(e => e.RuleText is { } t && (t.Contains("from Guardian") || t.Contains("from Fortified"))).ToList();
            Assert.Equal([expected], entries.Select(e => e.Name));
            foreach (var entry in entries)
            {
                var rows = entry.Effects.Where(e => e.Kind == EffectKind.ModifyParameter && e.TargetEntry == "AP").ToList();
                Assert.NotEmpty(rows);
                Assert.All(rows, e => Assert.Equal(0, e.Floor));
            }
        }
    }

    // 3.6.0 Enhanced Guardian: "hits they take count as having AP(-2) from Guardian (instead of only AP(-1))", and Guardian's "to a min. of AP(0)".
    // 3.5.1 Guardian Boost: "enemy hits always count as having AP(-1) from Guardian", the same floor.
    [Theory]
    [InlineData(false, "Enhanced Guardian", 1, 0)]
    [InlineData(false, "Enhanced Guardian", 0, 0)]
    [InlineData(false, "Enhanced Guardian", 3, 1)]
    [InlineData(true, "Guardian Boost", 1, 0)]
    [InlineData(true, "Guardian Boost", 0, 0)]
    [InlineData(true, "Guardian Boost", 2, 1)]
    public void A_rule_counting_as_guardian_holds_ap_at_zero(bool gf351, string rule, int weaponAp, int expected)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("AP", weaponAp)));
        var b = Unit("B", "4+", "4+", [], 1, [P("Guardian"), P(rule)]);
        var s = State([a], [b]);
        var weapons = s.Projection.Units[a.Id].Definition.Models.SelectMany(m => m.Weapons.Select(w => w.Id));
        var props = gf351 ? TestProperties.Core : TestProperties.Core36;
        var live = LiveProperties.Settle(new Interaction(s, props, Party.Of(s, a.Id, a.Models.Select(m => m.Id), weapons), Party.Whole(s, b.Id), AttackKind.Shooting, "Hold", 12, []));
        Assert.Equal(expected, live.Parameter(Side.Attacker, "AP"));
    }

    [Fact]
    public void The_36_min_ap0_rows_carry_a_floor_of_zero()
    {
        foreach (var name in new[] { "Fortified", "Fortified Growth", "Guardian", "Primeborn" })
        {
            var rows = TestProperties.Core36.Entry(name)!.Effects.Where(e => e.Kind == EffectKind.ModifyParameter && e.TargetEntry == "AP").ToList();
            Assert.NotEmpty(rows);
            Assert.All(rows, e => Assert.Equal(0, e.Floor));
        }
    }
}
