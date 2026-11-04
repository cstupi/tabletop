using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6 Scourge: "For each unmodified result of 1 to block hits, enemies count as having taken 2 extra wounds when checking who won melee, or if shooting
/// wounds would trigger Morale Tests". A `count_wounds` row on the defense roll: counted for the melee result (3.6.0 p.10) and the shooting morale
/// trigger, never laid on a model or added to the unit's wounds this activation.
/// </summary>
public class GfScourge36Tests
{
    const string Scourge = "Test Scourge";

    static readonly EntryDocument Entry = new(Scourge, CatalogKind.Weapon, false, null, null, null, "test", null,
        [new EffectDocument(EffectSubject.Opponent, 10, EffectKind.CountWounds, null, null, null, null, null, null, null, 2, false, null, null, null,
            EffectDuration.Interaction, [new ConditionDocument(ConditionKind.RollResult, null, Comparison.Eq, 1, [GfNames.DefenseRoll, "raw"])], [])]);

    static PropertySet PropsFor(string rules)
    {
        var doc = rules == RuleSets.Gf360 ? TestProperties.CoreDocument36 : TestProperties.CoreDocument;
        return PropertiesJson.ToSet(doc with { Entries = [.. doc.Entries, Entry] }, Guid.CreateVersion7);
    }

    static Sim SimFor(string rules, GameState state, params int[] dice) => rules == RuleSets.Gf360
        ? new Sim(state.Under(rules), GrimdarkFutureRules.V360, PropsFor(rules), new ScriptedDice(dice))
        : new Sim(state.Under(rules), GrimdarkFutureRules.Instance, PropsFor(rules), new ScriptedDice(dice));

    static List<Guid> Weapons(Tabletop.Content.Unit u) => [.. u.Models.SelectMany(m => m.Weapons.Select(w => w.Id))];

    [Fact]
    public void Two_ones_of_three_blocks_count_four_wounds()
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Lash", "24\"", 3, P(Scourge)));
        var target = Unit("Target", "4+", "4+", [], 5, [P("Regeneration")]);
        // Hits 4,4,4; blocks 1,1,6; Regeneration 5,5 ignores both wounds.
        var sim = SimFor(RuleSets.Gf360, State([shooter], [target], x1: -12, x2: 0), 4, 4, 4, 1, 1, 6, 5, 5);
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [target.Id], Weapons(shooter), GfNames.Hold));
        var block = sim.Steps("roll-to-block").Single();
        Assert.Equal((2, 4), (block.Values!["wounds"], block.Values["counted_wounds"]));
        Assert.Contains(Scourge, block.Applied ?? []);
        Assert.Equal(5, sim.Alive(target.Id));
        Assert.All(target.Models, m => Assert.Equal(0, sim.Model(m.Id).Wounds));
        Assert.Equal(0, sim.Unit(target.Id).WoundsThisActivation);
    }

    /// <summary>The charger (Tough(3)) strikes three hits with Scourge: blocks 1,1,6, both wounds regenerated, 0 real wounds and 4 counted. The defender
    /// strikes back one hit the charger fails to block: 1 wound. 3.6.0 p.10 counts wounds taken: the defender took 4, the charger 1, the defender loses.
    /// 3.5.1 compares wounds caused, and counts nothing: the charger caused 0, the defender 1, the charger loses.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, 0, 1, 1)]
    [InlineData(RuleSets.Gf360, 1, 4, 2)]
    public void Counted_wounds_decide_the_melee_under_3_6(string rules, int chargerValue, int defenderValue, int loser)
    {
        var charger = Unit("Charger", "4+", "4+", [], 1, [P("Tough", 3)], Melee("Lash", 3, P(Scourge)));
        var defender = Unit("Defender", "4+", "4+", [], 1, [P("Regeneration"), P("Tough", 3)], Melee("Fist", 1));
        // Charger hits 4,4,4; blocks 1,1,6; Regeneration 5,5. Strike back: hit 4, block 1. Morale 6.
        var sim = SimFor(rules, State([charger], [defender], x1: -3, x2: 0), 4, 4, 4, 1, 1, 6, 5, 5, 4, 1, 6, 6, 6);
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], Weapons(charger), GfNames.Charge));
        for (var i = 0; i < 4 && sim.AskForOrNull(Samples.Team2) is { } ask; i++)
            sim.Answer(Samples.Team2, ask.Question == "move" ? new Passed() : new Chosen("strike"));
        var compare = sim.Steps("compare").First();
        Assert.Equal((chargerValue, defenderValue, loser), (compare.Values!["charger_wounds"], compare.Values["defender_wounds"], compare.Values["loser"]));
        Assert.Equal(loser == 2 ? defender.Id : charger.Id, sim.Emitted.OfType<SequenceStarted>().Single(s => s.Sequence == GfNames.MoraleTest).Context.ActorUnitId);
        Assert.Equal(rules == RuleSets.Gf360, compare.Applied?.Contains(Scourge) ?? false);
        Assert.Equal(0, sim.Model(defender.Models[0].Id).Wounds);
        Assert.Equal(0, sim.Unit(defender.Id).WoundsThisActivation);
    }

    /// <summary>A Hold that shoots: two 1s on the block, both wounds regenerated. The 5-model unit counts 4 wounds (1 left of 5: half or less) and tests;
    /// the 10-model unit (6 left) does not.</summary>
    [Theory]
    [InlineData(5, true)]
    [InlineData(10, false)]
    public void Counted_shooting_wounds_trigger_the_general_morale_test(int models, bool tests)
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Lash", "24\"", 2, P(Scourge)));
        var target = Unit("Target", "4+", "4+", [], models, [P("Regeneration")]);
        var sim = SimFor(RuleSets.Gf360, State([shooter], [target], x1: -12, x2: 0), 4, 4, 1, 1, 5, 5);
        sim.Start(GfNames.HoldSeq, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        for (var i = 0; i < 6 && sim.AskForOrNull(Samples.Team1) is { } ask; i++)
            sim.Answer(Samples.Team1, ask.Question switch
            {
                "shoot_or_not" => new Chosen("shoot"),
                "declare_targets" => new Assigned(((Assignment)ask.Legal).Items.ToDictionary(o => o.Id, _ => target.Id)),
                _ => new Passed(),
            });
        Assert.Equal(4, sim.Steps("roll-to-block").Single().Values!["counted_wounds"]);
        Assert.Equal(0, sim.Unit(target.Id).WoundsThisActivation);
        Assert.All(target.Models, m => Assert.Equal(0, sim.Model(m.Id).Wounds));
        var starts = GfHooks.EndOfTurnSequences(sim.State, PropsFor(RuleSets.Gf360), shooter.Id);
        Assert.Equal(tests ? [target.Id] : [], starts.Select(s => s.Context.ActorUnitId!.Value));
    }

    [Fact]
    public void Count_wounds_needs_a_roll_result_and_subject_opponent()
    {
        var row = Entry.Effects[0];
        var problems = PropertiesJson.Validate(TestProperties.CoreDocument36 with { Entries = [.. TestProperties.CoreDocument36.Entries,
            Entry with { Name = "No Roll", Effects = [row with { Conditions = [] }] },
            Entry with { Name = "On Self", Effects = [row with { Subject = EffectSubject.Self }] }] });
        Assert.Contains(problems, p => p.StartsWith("No Roll:") && p.Contains("count_wounds"));
        Assert.Contains(problems, p => p.StartsWith("On Self:") && p.Contains("count_wounds"));
        Assert.DoesNotContain(problems, p => p.StartsWith(Scourge));
    }
}
