using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public partial class ArmyRuleHookTests
{
    static string Face(string rule, Guid unit) => $"face_{unit:N}_{rule}";

    [Theory]
    [InlineData(3, true, false)]
    [InlineData(4, false, true)]
    public void Unpredictable_shooter_rolls_once_and_its_face_picks_ap_or_plus_one_to_hit_for_every_group(int face, bool ap, bool toHit)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Unpredictable Shooter")], Gun("Rifle", "24\"", 1), Gun("Pistol", "12\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: -4, x2: 4), GrimdarkFutureRules.Instance, Core, new ScriptedDice([face, .. Enumerable.Repeat(5, 10)]));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id, ["Pistol"] = b.Id }));
        var start = sim.Steps("attack-start").Single();
        Assert.Equal(("Unpredictable Shooter", face), (start.Rolls.Single().Roll, start.Values![Face("Unpredictable Shooter", a.Id)]));
        Assert.All(sim.Steps("roll-to-hit"), s => Assert.Equal(toHit, s.Rolls.SelectMany(r => r.Modifiers).Any(m => m.Source == "Unpredictable Shooter")));
        Assert.All(sim.Steps("roll-to-block"), s => Assert.Equal(ap, s.Rolls.SelectMany(r => r.Modifiers).Any(m => m.Source == "AP")));
        Assert.Single(sim.Steps("attack-start"));
    }

    [Fact]
    public void Unpredictable_fighter_rolls_once_for_each_side_of_a_melee_that_holds_it()
    {
        var c = Unit("C", "4+", "4+", [], 1, [P("Unpredictable Fighter")], Melee("Claws", 1)); var d = Unit("D", "4+", "4+", [], 1, [P("Unpredictable Fighter")], Melee("Spear", 1));
        var sim = new Sim(State([c], [d], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice([2, 5, .. Enumerable.Repeat(1, 20)]));
        sim.Start(GfNames.Melee, new SequenceContext(c.Id, [d.Id], GfMelee.MeleeWeapons(sim.State, c.Id), GfNames.Charge));
        var start = sim.Steps("melee-start").Single();
        Assert.Equal((2, 5), (start.Values![Face("Unpredictable Fighter", c.Id)], start.Values[Face("Unpredictable Fighter", d.Id)]));
    }

    static EntryDocument MarkSpell(string rule, EffectSubject subject) => new("Test Mark " + rule, CatalogKind.Spell, false, null, 1, null, "Mark.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18),
        [new EffectDocument(subject, 10, EffectKind.GrantProperty, null, null, rule, null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], [])]);

    static void Mark(Sim sim, PropertySet props, string rule, Guid holder, Guid caster) =>
        sim.Append([new GrantAdded(holder, new Tabletop.Protocol.Grant(Guid.CreateVersion7(), props.Entry("Test Mark " + rule)!.Effects[0].Id, "Test Mark " + rule, caster, 1))]);

    [Fact]
    public void An_against_granted_unpredictable_shooter_rolls_when_shooting_the_marked_unit_and_not_another()
    {
        var props = TestProperties.WithSpells(MarkSpell("Unpredictable Shooter", EffectSubject.Opponent));
        foreach (var (shootsMarked, rolls) in new[] { (true, 1), (false, 0) })
        {
            var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var marked = Unit("M", "4+", "4+", [], 1, []); var other = Unit("O", "4+", "4+", [], 1, []);
            var sim = new Sim(State([a], [marked, other], x1: -6, x2: 6), GrimdarkFutureRules.Instance, props, new ScriptedDice([3, .. Enumerable.Repeat(5, 10)]));
            Mark(sim, props, "Unpredictable Shooter", marked.Id, a.Id);
            sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
            sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = shootsMarked ? marked.Id : other.Id }));
            Assert.Equal(rolls, sim.Steps("attack-start").Single().Rolls.Count);
            Assert.Equal(shootsMarked, sim.Steps("roll-to-block").SelectMany(s => s.Rolls).SelectMany(r => r.Modifiers).Any(m => m.Source == "AP"));
        }
    }

    [Fact]
    public void An_against_granted_unpredictable_fighter_rolls_in_melee_with_the_marked_unit_and_not_another()
    {
        var props = TestProperties.WithSpells(MarkSpell("Unpredictable Fighter", EffectSubject.Opponent));
        foreach (var (marksOpponent, rolls) in new[] { (true, 1), (false, 0) })
        {
            var c = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1)); var d = Unit("D", "4+", "4+", [], 1, [], Melee("Spear", 1)); var third = Unit("T", "4+", "4+", [], 1, []);
            var sim = new Sim(State([c], [d, third], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, props, new ScriptedDice([2, .. Enumerable.Repeat(1, 20)]));
            Mark(sim, props, "Unpredictable Fighter", marksOpponent ? d.Id : third.Id, c.Id);
            sim.Start(GfNames.Melee, new SequenceContext(c.Id, [d.Id], GfMelee.MeleeWeapons(sim.State, c.Id), GfNames.Charge));
            var start = sim.Steps("melee-start").Single();
            Assert.Equal(rolls, start.Rolls.Count);
            Assert.Equal(marksOpponent, start.Values!.ContainsKey(Face("Unpredictable Fighter", c.Id)));
        }
    }

    [Fact]
    public void A_once_granted_unpredictable_shooter_applies_its_face_to_the_attack_and_is_spent_by_it()
    {
        var props = TestProperties.WithSpells(MarkSpell("Unpredictable Shooter", EffectSubject.Self));
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 2)); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, props, new ScriptedDice([2, .. Enumerable.Repeat(5, 10)]));
        Mark(sim, props, "Unpredictable Shooter", a.Id, a.Id);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Equal(2, sim.Steps("attack-start").Single().Values![Face("Unpredictable Shooter", a.Id)]);
        Assert.True(sim.Steps("roll-to-block").SelectMany(s => s.Rolls).SelectMany(r => r.Modifiers).Any(m => m.Source == "AP"));   // the granted instance's own pass-one row ran
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void A_recorded_face_is_read_back_from_the_log_when_the_game_resumes_from_it()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Unpredictable Shooter")], Gun("Rifle", "24\"", 2)); var b = Unit("B", "4+", "4+", [], 1, []);
        int[] script = [2, .. Enumerable.Repeat(5, 10)];
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Core, new ScriptedDice(script));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        var log = sim.State.Applier.Log;
        var upToStart = log.First(e => e.Payload is StepCompleted { Step: "attack-start" }).Seq;
        var applier = new EventApplier(sim.State.Board, sim.State.Info);
        applier.ApplyAll(log.Where(e => e.Seq <= upToStart));
        // A game resumed from the log at the face, with dice that would roll a 6 if the face were drawn again: the face is read, not rerolled.
        var resumed = new Sim(new GameState(sim.State.Info, sim.State.Board, applier, GrimdarkFutureRules.Instance), GrimdarkFutureRules.Instance, Core, new ScriptedDice(script));
        resumed.Run();
        Assert.Empty(resumed.Steps("attack-start"));
        Assert.True(resumed.Steps("roll-to-block").SelectMany(s => s.Rolls).SelectMany(r => r.Modifiers).Any(m => m.Source == "AP"));
        Assert.Equal(sim.Steps("roll-to-block").SelectMany(s => s.Rolls).Select(r => r.Dice.Select(d => d.Raw).ToArray()).ToArray(),
            resumed.Steps("roll-to-block").SelectMany(s => s.Rolls).Select(r => r.Dice.Select(d => d.Raw).ToArray()).ToArray());
    }

    [Fact]
    public void With_no_declared_target_nothing_is_rolled_at_attack_start()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Unpredictable Shooter")], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: -4, x2: 4), GrimdarkFutureRules.Instance, Core, new ScriptedDice(3, 5, 5));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid>()));
        Assert.Empty(sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls));
        Assert.DoesNotContain(sim.Steps("attack-start"), s => s.Values?.ContainsKey(Face("Unpredictable Shooter", a.Id)) == true);
    }

    [Fact]
    public void In_melee_each_sides_face_applies_to_its_own_strikes_only()
    {
        var c = Unit("C", "4+", "4+", [], 1, [P("Unpredictable Fighter")], Melee("Claws", 1)); var d = Unit("D", "4+", "4+", [], 1, [P("Unpredictable Fighter")], Melee("Spear", 1));
        var sim = new Sim(State([c], [d], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice([2, 5, .. Enumerable.Repeat(6, 20)]));
        sim.Start(GfNames.Melee, new SequenceContext(c.Id, [d.Id], GfMelee.MeleeWeapons(sim.State, c.Id), GfNames.Charge));
        sim.Answer(Samples.Team2, new Chosen("strike"));
        bool Has(StepCompleted s, string source) => s.Rolls.SelectMany(r => r.Modifiers).Any(m => m.Source == source);
        var hits = sim.Steps("roll-to-hit"); var blocks = sim.Steps("roll-to-block");
        Assert.Equal([false, true], hits.Take(2).Select(s => Has(s, "Unpredictable Fighter")));   // the charger rolled 2 (AP), the defender 5 (+1 to hit)
        Assert.Equal([true, false], blocks.Take(2).Select(s => Has(s, "AP")));
    }

    [Fact]
    public void An_against_granted_unpredictable_is_spent_by_the_shooting_at_the_marked_unit()
    {
        var props = TestProperties.WithSpells(MarkSpell("Unpredictable Shooter", EffectSubject.Opponent));
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var marked = Unit("M", "4+", "4+", [], 1, []);
        var sim = new Sim(State([a], [marked], x1: -6, x2: 6), GrimdarkFutureRules.Instance, props, new ScriptedDice([3, .. Enumerable.Repeat(5, 10)]));
        Mark(sim, props, "Unpredictable Shooter", marked.Id, a.Id);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Assert.Single(sim.Unit(marked.Id).Grants);
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = marked.Id }));
        Assert.Empty(sim.Unit(marked.Id).Grants);
    }

    [Fact]
    public void A_once_granted_unpredictable_fighter_is_spent_by_the_melee()
    {
        var props = TestProperties.WithSpells(MarkSpell("Unpredictable Fighter", EffectSubject.Self));
        var c = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1)); var d = Unit("D", "4+", "4+", [], 1, [], Melee("Spear", 1));
        var sim = new Sim(State([c], [d], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, props, new ScriptedDice([2, .. Enumerable.Repeat(1, 20)]));
        Mark(sim, props, "Unpredictable Fighter", c.Id, c.Id);
        sim.Start(GfNames.Melee, new SequenceContext(c.Id, [d.Id], GfMelee.MeleeWeapons(sim.State, c.Id), GfNames.Charge));
        Assert.Single(sim.Unit(c.Id).Grants);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        Assert.Empty(sim.Unit(c.Id).Grants);
    }
}
