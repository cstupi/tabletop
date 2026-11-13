using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Army rules read against a unit's state: 3.6.0 p.10 "Shaken units … may strike back counting as Fatigued".</summary>
public class ArmyRuleAuditStateTests
{
    static void Set(GameState s, Guid unit, string condition) =>
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new ConditionSet(new Subject(SubjectKind.Unit, unit), condition, true), null));

    static int DodgePenalty(string condition)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Melee("Claws", 2));
        var b = Unit("B", "4+", "4+", [], 1, [P("Dodge")], Melee("Spear", 1));
        var s = State([a], [b], x1: 0, x2: 1.3).Under(RuleSets.Gf360);
        if (condition != "") Set(s, b.Id, condition);
        var live = LiveProperties.Settle(GfAttack.AttackInteraction(s, TestProperties.Core36, null, a.Id, b.Id, [a.Models[0].Weapons[0].Id], AttackKind.Melee, GfNames.Charge));
        return live.Effects(Side.Attacker, EffectKind.ModifyRoll).Count(x => x.Source.Entry.Name == "Dodge");
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData(GfNames.Fatigued, 0)]
    [InlineData(GfNames.Shaken, 0)]
    public void Dodge_holds_only_while_the_unit_is_neither_fatigued_nor_shaken(string condition, int live) =>
        Assert.Equal(live, DodgePenalty(condition));
}
