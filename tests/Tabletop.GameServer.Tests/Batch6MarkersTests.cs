using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Batch 6 (army-book rules spec → Authored rules 6): round-start rolls and markers on units.</summary>
public class Batch6MarkersTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static void Apply(GameState state, params StateChange[] changes)
    {
        foreach (var c in changes) state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, c, null));
    }

    static LiveSet Attack(GameState s, Guid attacker, Guid defender, AttackKind kind = AttackKind.Shooting) =>
        LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, attacker), Party.Whole(s, defender), kind, null, 12, []));

    static int Mod(LiveSet l, Side side, string roll) => l.Effects(side, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == roll).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void A_growth_places_a_marker_each_round_start_up_to_four_and_acts_for_every_two()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Defensive Growth")]); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b]);
        var defenses = new List<int>();
        for (var round = 1; round <= 5; round++)
        {
            Apply(state, [.. GfMarkers.AtRoundStart(state, Core)]);
            defenses.Add(Mod(Attack(state, b.Id, a.Id), Side.Defender, "defense"));
        }
        Assert.Equal([0, 1, 1, 2, 2], defenses);
        Assert.Equal(4, state.Projection.Units[a.Id].Grants.Count);
        var shaken = State([a with { Id = Guid.CreateVersion7() }], [b]);
        Apply(shaken, new ConditionSet(new Subject(SubjectKind.Unit, shaken.Projection.Units.Keys.First(k => k != b.Id)), GfNames.Shaken, true));
        Assert.Empty(GfMarkers.AtRoundStart(shaken, Core));                                           // "if it's on the table and isn't Shaken"
    }

    [Fact]
    public void Kept_markers_stay_after_they_apply_and_spent_markers_go_together()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Defensive Frenzy")]); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b]);
        Apply(state, [.. GfMarkers.Place(Core.Entry("Defensive Frenzy")!, state.Projection.Units[a.Id], a.Id, 3, GfMarkers.FrenzyCap)]);
        Assert.Equal(2, state.Projection.Units[a.Id].Grants.Count);                                    // "up to a max. of two markers"
        var live = Attack(state, b.Id, a.Id);
        Assert.Equal(2, Mod(live, Side.Defender, "defense"));
        Assert.Empty(RollEffects.Consume(live));                                                      // kept for the game
        Apply(state, [.. GfMarkers.Place(Core.Entry("Precision Spotter")!, state.Projection.Units[b.Id], a.Id, 2)]);
        var shot = Attack(state, a.Id, b.Id);
        Assert.Equal(2, Mod(shot, Side.Attacker, "hit"));
        RollEffects.Plan(shot, Side.Attacker, "hit", 4, [], new RollConvention(6, null), null, "hit", "defense");
        Assert.Equal(2, RollEffects.Consume(shot).Count);                                              // removed together
    }

    [Fact]
    public void A_frenzy_places_a_marker_when_its_unit_fully_destroys_an_enemy_and_vengeance_marks_the_destroyer()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Precision Frenzy")], Gun("Gun", "24\"", 1));
        var b = Unit("B", "4+", "4+", [], 2, [P("Vengeance")]);
        var sim = new Sim(State([a], [b]), GrimdarkFutureRules.Instance, Core, new ScriptedDice(Enumerable.Repeat(1, 20).ToArray()));
        sim.Start(GfNames.RuleHitsSeq, new SequenceContext(a.Id, [b.Id], [], "Test|2"));
        if (sim.AskForOrNull(Samples.Team2) is { Legal: Allocation al }) sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [al.Targets[0]] = 1, [al.Targets[1]] = 1 }));
        Assert.Equal(0, sim.Alive(b.Id));
        var grants = sim.Unit(a.Id).Grants;
        Assert.Single(grants, g => g.EntryName == "Precision Frenzy");
        Assert.Equal(2, grants.Count(g => g.EntryName == "Vengeance" && g.CasterUnitId == b.Id));   // one per model with the rule
    }

    [Fact]
    public void Steadfast_rolls_at_the_round_start_for_a_shaken_unit_and_a_4_ends_its_shaken()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Steadfast")]); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b]);
        Apply(state, new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Shaken, true));
        Assert.Equal([(a.Id, "Steadfast")], GfMarkers.Rallying(state, Core));
        var sim = new Sim(state, GrimdarkFutureRules.Instance, Core, new ScriptedDice([4]));
        sim.Start(GfNames.RallySeq, new SequenceContext(a.Id, [], [], "Steadfast"));
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(a.Id).Conditions);
    }

    [Fact]
    public void A_spotter_rolls_and_places_a_marker_on_a_4_and_a_tag_places_x_markers_once_per_game()
    {
        var spot = Unit("S", "4+", "4+", [], 1, [P("Precision Spotter")]); var e = Unit("E", "4+", "4+", [], 1, []);
        var sim = new Sim(State([spot], [e]), GrimdarkFutureRules.Instance, Core, new ScriptedDice([5, 4, 4]));
        sim.Append([new ActionRecorded(spot.Id, GfNames.Hold)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(spot.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Precision Spotter", spot.Models[0].Id)));
        sim.Answer(Samples.Team1, new SelectedPieces([e.Id]));
        Assert.Single(sim.Unit(e.Id).Grants, g => g.EntryName == "Precision Spotter");
        var tag = Unit("T", "4+", "4+", [], 1, [P("Piercing Tag", 3)]);
        var sim2 = new Sim(State([tag], [e]), GrimdarkFutureRules.Instance, Core, new ScriptedDice([4]));
        sim2.Append([new ActionRecorded(tag.Id, GfNames.Hold)]);
        sim2.Start(GfNames.HoldSeq, new SequenceContext(tag.Id, [], [], GfNames.Hold));
        sim2.Answer(Samples.Team1, new Chosen(GfPick.Key("Piercing Tag", tag.Models[0].Id)));
        sim2.Answer(Samples.Team1, new SelectedPieces([e.Id]));
        Assert.Equal(3, sim2.Unit(e.Id).Grants.Count(g => g.EntryName == "Piercing Tag"));
        Assert.Equal(3, Attack(sim2.State, tag.Id, e.Id).Parameter(Side.Attacker, "AP"));
        Assert.Equal(1, GfPick.GameUses(sim2.State, GfPick.Key("Piercing Tag", tag.Models[0].Id)));
    }

    [Fact]
    public void An_imported_x_places_no_more_than_the_marker_bound_and_deals_no_more_than_the_dealt_bound()
    {
        var tag = Unit("T", "4+", "4+", [], 1, [P("Piercing Tag", 1_000_000)]); var e = Unit("E", "4+", "4+", [], 1, []);
        var sim = new Sim(State([tag], [e]), GrimdarkFutureRules.Instance, Core, new ScriptedDice([4]));
        sim.Append([new ActionRecorded(tag.Id, GfNames.Hold)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(tag.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Piercing Tag", tag.Models[0].Id)));
        sim.Answer(Samples.Team1, new SelectedPieces([e.Id]));
        Assert.Equal(GfMarkers.MaxHeld, sim.Unit(e.Id).Grants.Count);
        Assert.Equal(("Ravage", GfRuleHits.MaxDealt), GfRuleHits.Parse(GfRuleHits.Key("Ravage", int.MaxValue)));
        Assert.Equal(("Ravage", GfRuleHits.MaxDealt), GfRuleHits.Parse("Ravage|999999999"));
    }

    [Fact]
    public void Regenerative_strength_counts_the_wounds_its_model_ignored()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Regenerative Strength"), P("Regeneration"), P("Tough", 6)]);
        var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = new Sim(State([b], [a]), GrimdarkFutureRules.Instance, Core, new ScriptedDice([1, 1, 6, 6, 1, 1, 1]));
        sim.Start(GfNames.RuleHitsSeq, new SequenceContext(b.Id, [a.Id], [], "Test|2"));   // two hits, both blocks fail, both wounds ignored
        Assert.Equal(2, GfMarkers.RegenerativeStrength(sim.State, a.Models[0].Id));
    }
}
