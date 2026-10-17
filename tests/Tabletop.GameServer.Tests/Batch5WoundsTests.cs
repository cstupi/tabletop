using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Batch 5 (army-book rules spec → Authored rules 5): hits and wounds rules deal in melee and on the move, and a once-per-game roll to ignore wounds.</summary>
public class Batch5WoundsTests
{
    /// <summary>A melee between two single-file units 2" apart (bases 0.74" apart), the charger striking first; strike back answered yes.</summary>
    static Sim Fight(Tabletop.Content.Unit charger, Tabletop.Content.Unit defender, params int[] dice)
    {
        var sim = new Sim(State([charger], [defender], x1: -1, x2: 1), GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(dice));
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        for (var i = 0; i < 20 && sim.State.Projection.Stack.Count > 0; i++)
        {
            if (sim.AskForOrNull(Samples.Team2) is { } a2) sim.Answer(Samples.Team2, a2.Question == "strike_back" ? new Chosen("strike") : a2.Legal is Allocation al ? new Allocated(new Dictionary<Guid, int> { [al.Targets[0]] = al.Amount }) : new Passed());
            else if (sim.AskForOrNull(Samples.Team1) is { } a1) sim.Answer(Samples.Team1, a1.Legal is Allocation al1 ? new Allocated(new Dictionary<Guid, int> { [al1.Targets[0]] = al1.Amount }) : new Passed());
            else break;
        }
        return sim;
    }

    static IReadOnlyList<SequenceStarted> Started(Sim sim, string sequence) => sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == sequence).ToList();

    [Fact]
    public void Deathstrike_deals_x_hits_to_the_attacking_unit_when_its_model_is_killed_in_melee()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 6)], Melee("Axe", 2));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Deathstrike", 3)], Melee("Club", 1));
        var sim = Fight(charger, defender, [6, 6, 1, 1, .. Enumerable.Repeat(1, 20)]);
        var hits = Assert.Single(Started(sim, GfNames.RuleHitsSeq));
        Assert.Equal(("Deathstrike|3", defender.Id, charger.Id), (hits.Context.Action, hits.Context.ActorUnitId!.Value, hits.Context.TargetUnitIds[0]));
        var block = sim.Emitted.OfType<StepCompleted>().Last(s => s.Step == "roll-to-block");
        Assert.Equal(3, block.Rolls.Sum(r => r.Dice.Count));
    }

    [Fact]
    public void Retaliate_deals_x_hits_per_wound_its_model_takes_in_melee()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 6)], Melee("Axe", 2));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Retaliate", 1), P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(charger, defender, [6, 6, 1, 1, .. Enumerable.Repeat(1, 20)]);
        Assert.Contains(Started(sim, GfNames.RuleHitsSeq), s => s.Context.Action == "Retaliate|2" && s.Context.TargetUnitIds[0] == charger.Id);
    }

    [Fact]
    public void Ravage_rolls_x_dice_at_its_turn_and_each_6_is_a_wound_with_no_block()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Ravage", 2)], Melee("Axe", 1));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(charger, defender, [6, 3, .. Enumerable.Repeat(1, 20)]);
        var roll = sim.Emitted.OfType<StepCompleted>().First(s => s.Rolls.Any(r => r.Roll == "Ravage")).Rolls.Single();
        Assert.Equal((2, 6), (roll.Dice.Count, roll.Threshold));
        Assert.Equal(1, sim.Model(defender.Models[0].Id).Wounds);
    }

    [Fact]
    public void Self_destruct_kills_its_surviving_model_after_both_sides_attack_and_deals_x_hits()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Self-Destruct", 2)], Melee("Axe", 1));
        var defender = Unit("D", "4+", "4+", [], 1, [P("Tough", 6)], Melee("Club", 1));
        var sim = Fight(charger, defender, Enumerable.Repeat(1, 30).ToArray());
        Assert.IsType<OnShelf>(sim.Model(charger.Models[0].Id).Location);
        Assert.Contains(Started(sim, GfNames.RuleHitsSeq), s => s.Context.Action == "Self-Destruct|2" && s.Context.TargetUnitIds[0] == defender.Id);
    }

    [Fact]
    public void Protection_feat_rolls_for_the_first_attack_that_wounds_the_unit_and_not_again()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Gun", "24\"", 2));
        var b = Unit("B", "4+", "4+", [], 1, [P("Protection Feat"), P("Tough", 9)]);
        var sim = new Sim(State([a], [b]), GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(Enumerable.Repeat(1, 40).ToArray()));
        // Two attacks of two hits each, every block failing: each deal-wounds frame assigns two wounds to B.
        var first = new SequenceContext(a.Id, [b.Id], [], null);
        sim.Start(GfNames.RuleHitsSeq, first with { Action = "Test|2" });
        Assert.Contains(sim.Emitted.OfType<StepCompleted>(), s => s.Step == "assign" && s.Rolls.Any(r => r.Roll == "Protection Feat"));
        var before = sim.Emitted.Count;
        sim.Start(GfNames.RuleHitsSeq, first with { Action = "Test|2" });
        Assert.DoesNotContain(sim.Emitted.Skip(before).OfType<StepCompleted>(), s => s.Step == "assign" && s.Rolls.Any(r => r.Roll == "Protection Feat"));
    }

    [Fact]
    public void Crossing_attack_offers_the_enemy_units_a_move_crossed_and_rolls_x_dice_on_the_one_picked()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Crossing Attack", 3), P("Flying")]);
        var e = Unit("E", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [e], x1: -3, x2: 3), GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(Enumerable.Repeat(6, 20).ToArray()));
        sim.Append([new ActionRecorded(a.Id, GfNames.Advance)]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        var start = ((OnTable)sim.Model(a.Models[0].Id).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(a.Models[0].Id, sim.Model(a.Models[0].Id).Location, new OnTable(start with { Position = start.Position with { X = start.Position.X + 5.5 } }))]));
        while (sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.React or AskQuestions.Confirm or "move" } skip) sim.Answer(Samples.Team1, new Passed());
        var pick = sim.AskFor(Samples.Team1);
        Assert.Equal("crossing_target", pick.Question);
        Assert.Equal([e.Id], Assert.IsType<PieceSelection>(pick.Legal).Eligible);
        sim.Answer(Samples.Team1, new SelectedPieces([e.Id]));
        var roll = sim.Emitted.OfType<StepCompleted>().First(s => s.Rolls.Any(r => r.Roll == "Crossing Attack")).Rolls.Single();
        Assert.Equal(3, roll.Dice.Count);
    }

    [Fact]
    public void A_strafing_weapon_is_in_no_shooting_group()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Gun", "24\"", 1), Gun("Strafer", "24\"", 2, P("Strafing")));
        var state = State([a], [Unit("E", "4+", "4+", [], 1, [])]);
        var weapons = GfShooting.Groups(state, TestProperties.Core, a.Id, null).SelectMany(g => g.WeaponIds).ToList();
        Assert.Single(weapons);
    }
}
