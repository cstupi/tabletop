using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Batch 8 (army-book rules spec → Authored rules 8): the grammar additions and the rules they play.</summary>
public class Batch8GrammarTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static LiveSet Attack(GameState s, Guid attacker, Guid defender, AttackKind kind, string? action = null, double range = 12, Guid? weapon = null) =>
        LiveProperties.Settle(new Interaction(s, Core, weapon is { } w ? Party.Of(s, attacker, Lookup.LivingModels(s, attacker), [w]) : Party.Whole(s, attacker), Party.Whole(s, defender), kind, action, range, []));

    static int Mod(LiveSet l, Side side, string roll) => l.Effects(side, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == roll).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void A_floor_is_modify_stat_only_and_replace_stat_needs_a_value()
    {
        EntryDocument E(EffectDocument f) => new("X", CatalogKind.Model, false, Aggregation.All, null, null, "X.", null, [f]);
        IReadOnlyList<string> V(EntryDocument e) => PropertiesJson.Validate(TestProperties.RulebookDocument with { Entries = [.. TestProperties.RulebookDocument.Entries, e] });
        var roll = new EffectDocument(EffectSubject.Self, 10, EffectKind.ModifyRoll, "hit", null, null, null, null, null, null, 1, false, null, null, null, EffectDuration.Interaction, [], [], Floor: 6);
        Assert.Contains(V(E(roll)), p => p.Contains("floor is modify_stat only"));
        var replace = new EffectDocument(EffectSubject.Self, 10, EffectKind.ReplaceStat, null, new StatRef(StatLevel.Unit, "Defense"), null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, [], []);
        Assert.Contains(V(E(replace)), p => p.Contains("replace_stat needs a replacement or the parameter"));
    }

    [Fact]
    public void Armor_sets_defense_to_its_x()
    {
        var a = Unit("A", "4+", "5+", [], 1, [P("Armor", 3)]); var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a], [b]);
        Assert.Equal(3, StatReader.TargetNumber(Attack(s, b.Id, a.Id, AttackKind.Shooting), Side.Defender, new StatRef(StatLevel.Unit, "Defense")).Value);
    }

    [Fact]
    public void Shrouding_takes_range_and_charge_to_a_minimum_of_6_and_no_lower_than_they_were()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Gun", "24\"", 1), Gun("Pistol", "4\"", 1)); var b = Unit("B", "4+", "4+", [], 1, [P("Darkborn")]);
        var s = State([a], [b]);
        var guns = s.Projection.Models.Values.Where(m => m.UnitId == a.Id).SelectMany(m => m.Slot.Weapons).ToList();
        Assert.Equal(20.0, StatReader.WeaponRange(Attack(s, a.Id, b.Id, AttackKind.Shooting), Side.Attacker, guns[0].Id));
        Assert.Equal(4.0, StatReader.WeaponRange(Attack(s, a.Id, b.Id, AttackKind.Shooting), Side.Attacker, guns[1].Id));
        Assert.Equal(10.0, GfMelee.ChargeDistance(s, Core, a.Id, b.Id));
        var c = Unit("C", "4+", "4+", [], 1, [P("Melee Shrouding")]);
        var s2 = State([a], [c]);
        Assert.Equal(9.0, GfMelee.ChargeDistance(s2, Core, a.Id, c.Id));
    }

    [Fact]
    public void Entrenched_and_mobile_artillery_read_a_move_this_round()
    {
        var a = Unit("A", "4+", "4+", [], 1, []); var b = Unit("B", "4+", "4+", [], 1, [P("Entrenched")]);
        var s = State([a], [b]);
        Assert.Equal(-2, Mod(Attack(s, a.Id, b.Id, AttackKind.Shooting, range: 12), Side.Attacker, "hit"));
        Assert.Equal(0, Mod(Attack(s, a.Id, b.Id, AttackKind.Shooting, range: 6), Side.Attacker, "hit"));
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new ConditionSet(new Subject(SubjectKind.Unit, b.Id), GfNames.MovedThisRound, true), null));
        Assert.Equal(0, Mod(Attack(s, a.Id, b.Id, AttackKind.Shooting, range: 12), Side.Attacker, "hit"));
        Assert.Contains(GrimdarkFutureRules.Instance.OnRoundStarted(s, Core, 2), c => c is ConditionSet { Condition: GfNames.MovedThisRound, Present: false });
        var m = Unit("M", "4+", "4+", [], 1, [P("Mobile Artillery")]);
        var s2 = State([m], [a]);
        Assert.Equal(1, Mod(Attack(s2, m.Id, a.Id, AttackKind.Shooting, GfNames.Hold, 12), Side.Attacker, "hit"));
        Assert.Equal(-2, Mod(Attack(s2, a.Id, m.Id, AttackKind.Shooting, null, 12), Side.Attacker, "hit"));
    }

    [Fact]
    public void Quick_readjustment_takes_away_indirects_penalty_only()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Quick Readjustment")], Gun("Mortar", "36\"", 1, P("Indirect")));
        var plain = Unit("P", "4+", "4+", [], 1, [], Gun("Mortar", "36\"", 1, P("Indirect")));
        var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a, plain], [b]);
        var mortar = s.Projection.Models.Values.First(x => x.UnitId == a.Id).Slot.Weapons[0].Id;
        var plainMortar = s.Projection.Models.Values.First(x => x.UnitId == plain.Id).Slot.Weapons[0].Id;
        var live = Attack(s, a.Id, b.Id, AttackKind.Shooting, GfNames.Advance, weapon: mortar);
        Assert.Equal(0, Mod(live, Side.Attacker, "hit"));
        Assert.True(live.Has(Side.Attacker, "Indirect"));                                              // the rule stays: no sight needed
        Assert.Equal(-1, Mod(Attack(s, plain.Id, b.Id, AttackKind.Shooting, GfNames.Advance, weapon: plainMortar), Side.Attacker, "hit"));
    }

    [Fact]
    public void Heavy_impact_counts_as_impact_x()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Heavy Impact", 3)]); var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a], [b]);
        Assert.Equal(3, Attack(s, a.Id, b.Id, AttackKind.Melee, GfNames.Charge).Parameter(Side.Attacker, "Impact"));
    }

    [Fact]
    public void Grounded_precision_reads_the_attackers_terrain()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Grounded Precision")]); var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a], [b]);
        var near = new Interaction(s, Core, Party.Whole(s, a.Id), Party.Whole(s, b.Id), AttackKind.Shooting, null, 12, [], AttackerTerrain: ["near"]);
        var far = near with { AttackerTerrain = [] };
        Assert.Equal((1, 0), (Mod(LiveProperties.Settle(near), Side.Attacker, "hit"), Mod(LiveProperties.Settle(far), Side.Attacker, "hit")));
    }

    [Fact]
    public void No_retreat_passes_the_failed_test_then_wounds_on_1_to_3_that_no_roll_ignores()
    {
        var a = Unit("A", "4+", "4+", [], 3, [P("No Retreat"), P("Regeneration")]);
        var sim = new Sim(State([a], [Unit("E", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Core, new ScriptedDice([1, 1, 2, 6, 6, 6, 6]));
        sim.Start(GfNames.MoraleTest, new SequenceContext(a.Id, [], [], "wounds"));
        if (sim.AskForOrNull(Samples.Team1) is { Legal: Allocation al }) sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [al.Targets[0]] = 1, [al.Targets[1]] = 1 }));
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(a.Id).Conditions);
        var roll = sim.Emitted.OfType<StepCompleted>().SelectMany(x => x.Rolls).Single(r => r.Roll == "No Retreat");
        Assert.Equal(3, roll.Dice.Count);                                                               // three models of one wound each
        Assert.Equal(1, sim.Alive(a.Id));                                                               // the 1 and the 2 wound; Regeneration rolls nothing
        Assert.DoesNotContain(sim.Emitted.OfType<StepCompleted>().SelectMany(x => x.Rolls), r => r.Roll == "Regeneration");
    }
}
