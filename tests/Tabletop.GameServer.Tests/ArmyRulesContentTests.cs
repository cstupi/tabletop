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

public class ArmyRulesContentTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static readonly string[] NamesOnly = ["Quick Shot",
        "Primal", "Primal Boost", "Clan Warrior", "Clan Warrior Boost"];

    [Fact]
    public void The_part_holds_seventy_three_rules_and_composes_valid()
    {
        var part = PropertiesJson.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "server/game/Content/grimdark-future-army-rules.part.json")));
        Assert.Equal(73, part.Entries.Count);
        Assert.Empty(PropertiesJson.Validate(TestProperties.CoreDocument));
        Assert.All(part.Entries, e => Assert.NotNull(Core.Entry(e.Name)));
        Assert.All(NamesOnly, n => Assert.Empty(Core.Entry(n)!.Effects));
        Assert.All(part.Entries, e => Assert.Null(e.Faction));
    }

    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Tabletop.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("no Tabletop.sln above the test binaries");
    }

    static LiveSet Shot(GameState s, Guid a, Guid b, double range) => LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a), Party.Whole(s, b), AttackKind.Shooting, null, range, []));
    static int HitMods(LiveSet live) => live.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Changebound_gives_minus_one_over_nine_inches_and_its_boost_gives_it_always_and_never_twice()
    {
        var shooter = Unit("S", "4+", "4+", [], 1, []);
        var plain = Unit("P", "4+", "4+", [], 1, [P("Changebound")]); var boosted = Unit("B", "4+", "4+", [], 1, [P("Changebound"), P("Changebound Boost")]);
        var s = State([shooter], [plain, boosted]);
        Assert.Equal((0, -1), (HitMods(Shot(s, shooter.Id, plain.Id, 6)), HitMods(Shot(s, shooter.Id, plain.Id, 12))));
        Assert.Equal((-1, -1), (HitMods(Shot(s, shooter.Id, boosted.Id, 6)), HitMods(Shot(s, shooter.Id, boosted.Id, 12))));
    }

    [Fact]
    public void Shred_on_the_shipped_set_adds_a_wound_for_a_defense_one()
    {
        var a = Unit("A", "2+", "4+", [], 1, [], Gun("Rifle", "24\"", 2, P("Shred"))); var b = Unit("B", "4+", "4+", [], 5, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Core, new ScriptedDice(5, 5, 1, 6));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Equal(2, Assert.IsType<Allocation>(sim.AskFor(Samples.Team2).Legal).Amount);
    }

    [Fact]
    public void Plaguebound_boost_ignores_wounds_on_a_five()
    {
        var a = Unit("A", "2+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "6+", [], 1, [P("Plaguebound"), P("Plaguebound Boost"), P("Tough", 3)]);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Core, new ScriptedDice(5, 1, 5));
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Equal(0, sim.Model(b.Models[0].Id).Wounds);                                             // hit, failed block, a 5 ignores the wound
        Assert.Equal(5, sim.Steps("assign").Single().Rolls.Single().Threshold);
    }

    [Fact]
    public void Counter_attack_strikes_first_when_charged_and_unwieldy_strikes_last_when_charging()
    {
        var charger = Unit("C", "4+", "6+", [], 1, [], Melee("Maul", 1, P("Unwieldy")), Melee("Knife", 1));
        var defender = Unit("D", "4+", "6+", [], 1, [P("Counter-Attack")], Melee("Spear", 1));
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice(Enumerable.Repeat(2, 20).ToArray()));
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        var order = sim.Emitted.OfType<SequenceStarted>().Where(x => x.Sequence == GfNames.AttackSequence).Select(x => x.Context.ActorUnitId).ToList();
        Assert.Equal([defender.Id, charger.Id, charger.Id], order);                                     // Spear first; then Knife; Maul last
        var maul = charger.Models[0].Weapons.Single(w => w.Name == "Maul").Id;
        Assert.Equal([maul], sim.Emitted.OfType<SequenceStarted>().Last(x => x.Sequence == GfNames.AttackSequence).Context.WeaponIds);
    }

    [Fact]
    public void Unwieldy_strikes_nothing_when_the_other_weapons_destroyed_the_defender_but_the_charger_is_still_fatigued()
    {
        var charger = Unit("C", "4+", "6+", [], 1, [], Melee("Knife", 1), Melee("Maul", 1, P("Unwieldy")));
        var defender = Unit("D", "4+", "6+", [], 1, [], Melee("Spear", 1));
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Core, new ScriptedDice([6, 1, .. Enumerable.Repeat(2, 20)]));
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        var knife = charger.Models[0].Weapons.Single(w => w.Name == "Knife").Id;
        Assert.Equal([knife], sim.Emitted.OfType<SequenceStarted>().Single(x => x.Sequence == GfNames.AttackSequence).Context.WeaponIds);
        Assert.Contains(GfNames.Fatigued, sim.Unit(charger.Id).Conditions);
    }
}
