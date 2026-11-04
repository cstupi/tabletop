using Tabletop.Content;
using Tabletop.Geometry;
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
/// The positive_defense modifier class (3.6.0 Pierce: "Targets get -1 to Defense rolls ... and ignore positive modifiers to Defense rolls" is written
/// `suppress_modifiers positive_defense` on the opponent): suppressed on the defender, every positive modifier of the defense roll goes, cover's +1 and a
/// rule row's alike, each listed in RollPlan.Ignored by the suppressor; AP stays.
/// </summary>
public class GfPierce36Tests
{
    static EffectDocument Row(EffectSubject subject, EffectKind kind, string? roll = null, int? amount = null, TargetModifier? modifier = null) =>
        new(subject, 10, kind, roll, null, null, null, null, null, modifier, amount, false, null, null, null, EffectDuration.Interaction, [], []);

    const string Piercing = "Test Pierce", Shield = "Test Shield";

    static readonly PropertySet Props = TestProperties.WithSpells(
        new EntryDocument(Piercing, CatalogKind.Weapon, false, null, null, null, "Test Pierce.", null,
            [Row(EffectSubject.Opponent, EffectKind.SuppressModifiers, modifier: TargetModifier.PositiveDefense)]),
        new EntryDocument(Shield, CatalogKind.Unit, false, null, null, null, "Test Shield.", null,
            [Row(EffectSubject.Self, EffectKind.ModifyRoll, GfNames.DefenseRoll, 1)]));

    static IGameRules RulesOf(string id) => id == RuleSets.Gf360 ? GrimdarkFutureRules.V360 : GrimdarkFutureRules.V351;

    /// <summary>One AP(1) shot at a shielded unit in cover; the defense roll as recorded.</summary>
    static RollRecord Block(string rulesId, bool pierce)
    {
        PropertyInstance[] weapon = pierce ? [P("AP", 1), P(Piercing)] : [P("AP", 1)];
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, weapon));
        var target = Unit("T", "4+", "4+", [P(Shield)], 1, []);
        var wood = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wood"), new Point3(5, -17.75, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(3, 3), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, null))]);
        var sim = new Sim(State([shooter], [target], x1: -5, x2: 5, terrain: [wood]).Under(rulesId), RulesOf(rulesId), Props, new ScriptedDice(5, 2));
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = target.Id }));
        Assert.Equal(1, sim.Steps("determine-attacks").Single().Values!["in_cover"]);
        return sim.Steps("roll-to-block").Single().Rolls.Single();
    }

    static IReadOnlyList<(string, int)> Modifiers(RollRecord r) => [.. r.Modifiers.Select(m => (m.Source, m.Amount)).Order()];

    [Fact]
    public void Positive_defense_drops_cover_and_a_rule_rows_plus_one_but_not_ap_and_names_the_suppressor_for_each()
    {
        var block = Block(RuleSets.Gf360, pierce: true);
        Assert.Equal([("AP", -1)], Modifiers(block));
        Assert.Equal([new IgnoredEffect("Test Shield", Piercing, 1), new IgnoredEffect("cover", Piercing, 1)], block.Ignored!.OrderByDescending(i => i.Source));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Without_the_suppressor_cover_and_the_rule_row_apply_beside_ap(string rulesId)
    {
        var block = Block(rulesId, pierce: false);
        Assert.Equal([("AP", -1), ("cover", 1), ("Test Shield", 1)], Modifiers(block));
        Assert.Null(block.Ignored);
    }
}
