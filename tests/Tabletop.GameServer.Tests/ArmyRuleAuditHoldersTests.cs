using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>"All models with this rule": a rule some models of a unit hold (a joined Hero without it, or a Hero with it in a unit without) triggers
/// when any living model holds it, and its effect reaches the holders only.</summary>
public class ArmyRuleAuditHoldersTests
{
    /// <summary>The unit with its last model a joined Hero: its own rules and its own weapon, as Army Forge's import writes a joined Hero's slot.</summary>
    public static Unit Joined(Unit unit, PropertyInstance[] heroRules, Weapon heroWeapon)
    {
        var models = unit.Models.ToList();
        models[^1] = models[^1] with { Properties = [P("Hero"), .. heroRules], Weapons = [heroWeapon with { Id = Guid.CreateVersion7() }] };
        return unit with { Models = models };
    }

    static bool Has(StepCompleted s, string source) => s.Rolls.SelectMany(r => r.Modifiers).Any(m => m.Source == source);
    static StepCompleted HitsOf(Sim sim, int dice) => sim.Steps("roll-to-hit").Single(s => s.Rolls.Sum(r => r.Dice.Count) == dice);

    // ---- 1. Unpredictable Shooter: "roll one die and apply one effect to all models with this rule" ----------------------------------------

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Unpredictable_shooter_rolls_when_a_joined_hero_lacks_it_and_its_face_reaches_the_holders_weapons_only(string rules)
    {
        var a = Joined(Unit("A", "4+", "4+", [], 3, [P("Unpredictable Shooter")], Gun("Rifle", "24\"", 1)), [], Gun("Pistol", "24\"", 3));
        var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(rules, State([a], [b], x1: -4, x2: 4), [5, .. Enumerable.Repeat(5, 20)]);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id, ["Pistol"] = b.Id }));
        Assert.Equal("Unpredictable Shooter", sim.Steps("attack-start").Single().Rolls.Single().Roll);
        Assert.True(Has(HitsOf(sim, 2), "Unpredictable Shooter"));    // the two Rifles' models hold it
        Assert.False(Has(HitsOf(sim, 3), "Unpredictable Shooter"));   // the Hero's Pistol (3 attacks) does not
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Unpredictable_shooter_rolls_for_a_joined_hero_holding_it_in_a_unit_without_it(string rules)
    {
        var a = Joined(Unit("A", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1)), [P("Unpredictable Shooter")], Gun("Pistol", "24\"", 3));
        var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(rules, State([a], [b], x1: -4, x2: 4), [5, .. Enumerable.Repeat(5, 20)]);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id, ["Pistol"] = b.Id }));
        Assert.Equal("Unpredictable Shooter", sim.Steps("attack-start").Single().Rolls.Single().Roll);
        Assert.False(Has(HitsOf(sim, 2), "Unpredictable Shooter"));   // the two Rifles
        Assert.True(Has(HitsOf(sim, 3), "Unpredictable Shooter"));    // the Hero's Pistol
    }

    [Fact]
    public void Unpredictable_fighter_rolls_in_melee_when_a_joined_hero_lacks_it()
    {
        var c = Joined(Unit("C", "4+", "4+", [], 2, [P("Unpredictable Fighter")], Melee("Claws", 1)), [], Melee("Sword", 1));
        var d = Unit("D", "4+", "4+", [], 1, [], Melee("Spear", 1));
        var sim = new Sim(State([c], [d], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, TestProperties.Core, new Dice.ScriptedDice([2, .. Enumerable.Repeat(1, 20)]));
        sim.Start(GfNames.Melee, new SequenceContext(c.Id, [d.Id], GfMelee.MeleeWeapons(sim.State, c.Id), GfNames.Charge));
        Assert.Equal(2, sim.Steps("melee-start").Single().Values![$"face_{c.Id:N}_Unpredictable Fighter"]);
    }

    // ---- 2. "When this unit is activated, pick one effect: … all models with this rule in this unit …" -----------------------------------

    static IReadOnlyList<AskOption> Offered(Sim sim) =>
        sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.React, Legal: Options o } ? o.Choices : [];

    static Sim Activating(string rules, Unit actor)
    {
        var sim = Sim.Gf(rules, State([actor], [Unit("E", "4+", "4+", [], 1, [])], x1: -12, x2: 12), 4, 4, 4, 4, 4, 4);
        sim.Append([new ActionRecorded(actor.Id, GfNames.Hold)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(actor.Id, [], [], GfNames.Hold));
        return sim;
    }

    [Theory]
    [InlineData(RuleSets.Gf351, "Versatile Attack")]
    [InlineData(RuleSets.Gf351, "Versatile Reach")]
    [InlineData(RuleSets.Gf351, "Watchborn")]
    [InlineData(RuleSets.Gf360, "Versatile Expert")]
    [InlineData(RuleSets.Gf360, "Versatile Reach")]
    [InlineData(RuleSets.Gf360, "Versatile Warrior")]
    [InlineData(RuleSets.Gf360, "Watchborn")]
    [InlineData(RuleSets.Gf360, "Speed Feat")]
    public void A_pick_one_effect_rule_is_offered_when_a_joined_hero_lacks_it(string rules, string rule)
    {
        var a = Joined(Unit("A", "4+", "4+", [], 3, [P(rule)], Gun("Rifle", "24\"", 1)), [], Gun("Pistol", "24\"", 1));
        Assert.Contains(Offered(Activating(rules, a)), o => o.Id == $"{rule}|{a.Id:N}");
    }

    [Theory]
    [InlineData(RuleSets.Gf351, "Versatile Attack")]
    [InlineData(RuleSets.Gf360, "Versatile Expert")]
    public void A_pick_one_effect_rule_is_offered_for_a_joined_hero_holding_it(string rules, string rule)
    {
        var a = Joined(Unit("A", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1)), [P(rule)], Gun("Pistol", "24\"", 1));
        Assert.Contains(Offered(Activating(rules, a)), o => o.Id == $"{rule}|{a.Id:N}");
    }

    [Fact]
    public void A_pick_one_effect_rule_worded_for_units_where_all_models_have_it_is_not_offered_to_a_unit_some_models_lack()
    {
        // 3.5.1 Speed Feat: "when this unit moves and all its models have this rule".
        var a = Joined(Unit("A", "4+", "4+", [], 3, [P("Speed Feat")], Gun("Rifle", "24\"", 1)), [], Gun("Pistol", "24\"", 1));
        Assert.DoesNotContain(Offered(Activating(RuleSets.Gf351, a)), o => o.Id.StartsWith("Speed Feat"));
    }

    // ---- 5. Crossing Attack: "when this model moves through enemy units, pick one of them and roll X dice" ----------------------------------

    /// <summary>A two-model Flying unit at x -3 advancing 5.5" along x through an enemy unit at x 3 (its models in the same rows, y -18.5 and -17);
    /// every crossing Ask the move sets off is answered with the first enemy unit offered.</summary>
    static (Sim Sim, List<Ask> Picks) Cross(Unit a, Unit e)
    {
        var sim = new Sim(State([a], [e], x1: -3, x2: 3), GrimdarkFutureRules.Instance, TestProperties.Core, new Dice.ScriptedDice(Enumerable.Repeat(6, 40).ToArray()));
        sim.Append([new ActionRecorded(a.Id, GfNames.Advance)]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        sim.Answer(Samples.Team1, new Destinations(a.Models.Select(m => sim.Model(m.Id).Location is OnTable { Placement: var p } t
            ? new ModelRelocation(m.Id, t, new OnTable(p with { Position = p.Position with { X = p.Position.X + 5.5 } })) : throw new InvalidOperationException()).ToList()));
        var picks = new List<Ask>();
        for (var i = 0; i < 20 && sim.AskForOrNull(Samples.Team1) is { } ask; i++)
        {
            if (ask.Question == "crossing_target") { picks.Add(ask); sim.Answer(Samples.Team1, new SelectedPieces([((PieceSelection)ask.Legal).Eligible[0]])); }
            else if (ask.Legal is Allocation al) sim.Answer(Samples.Team1, new Allocated(new Dictionary<Guid, int> { [al.Targets[0]] = al.Amount }));
            else sim.Answer(Samples.Team1, new Passed());
            if (sim.AskForOrNull(Samples.Team2) is { Legal: Allocation al2 }) sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [al2.Targets[0]] = al2.Amount }));
        }
        return (sim, picks);
    }

    static Unit Carrying(params int?[] x)
    {
        var u = Unit("A", "4+", "4+", [], x.Length, [P("Flying")]);
        return u with { Models = u.Models.Select((m, i) => x[i] is { } n ? m with { Properties = [.. m.Properties, P("Crossing Attack", n)] } : m).ToList() };
    }

    [Fact]
    public void A_model_without_crossing_attack_moving_through_an_enemy_sets_nothing_off()
    {
        // Only the first model (y -18.5) moves through E's one model; the second, which holds Crossing Attack, passes beside it.
        var (_, picks) = Cross(Carrying(null, 3), Unit("E", "4+", "4+", [], 1, [P("Tough", 9)]));
        Assert.Empty(picks);
    }

    [Fact]
    public void Each_model_with_crossing_attack_that_moved_through_enemies_rolls_its_own_x()
    {
        var e = Unit("E", "4+", "4+", [], 2, [P("Tough", 9)]);
        var (sim, picks) = Cross(Carrying(3, 2), e);
        Assert.Equal(2, picks.Count);
        Assert.Equal([3, 2], sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).Where(r => r.Roll == "Crossing Attack").Select(r => r.Dice.Count));
    }
}
