using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 p.13 Artillery: "When this model uses Hold actions and shoots at enemies over 12” away, it gets +1 to hit rolls. If most models in this
/// unit have this rule, as long as they haven't moved during this round, enemy units shooting at them from over 12” away get -1 to hit rolls."
/// </summary>
public class GfArtillery36Tests
{
    static readonly PropertySet Core = TestProperties.Core36;
    static readonly string[] Rows = ["Hold", "Advance", "Rush", "Charge"];

    static int HitMod(GameState s, Guid attacker, Guid defender, string? action, double range) =>
        LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, attacker), Party.Whole(s, defender), AttackKind.Shooting, action, range, []))
            .Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Artillery_may_advance_under_3_6_and_has_no_note()
    {
        var gun = Unit("Gun", "4+", "4+", [P(GfNames.Artillery)], 1, []);
        var state = State([gun], [Unit("Enemy", "4+", "4+", [], 1, [])], x1: -4, x2: 4).Under(RuleSets.Gf360);   // within charge distance
        var (actions, note) = GrimdarkFutureRules.ActivationChoices(state, Core, state.Projection.Units[gun.Id], Rows);
        Assert.Equal(Rows, actions);
        Assert.Null(note);
    }

    [Fact]
    public void The_plus_one_needs_a_hold_and_over_twelve_inches_under_3_6()
    {
        var gun = Unit("Gun", "4+", "4+", [P(GfNames.Artillery)], 1, []);
        var target = Unit("Target", "4+", "4+", [], 1, []);
        var s = State([gun], [target]).Under(RuleSets.Gf360);
        Assert.Equal(0, HitMod(s, gun.Id, target.Id, GfNames.Advance, 13));
        Assert.Equal(1, HitMod(s, gun.Id, target.Id, GfNames.Hold, 13));
        Assert.Equal(0, HitMod(s, gun.Id, target.Id, GfNames.Hold, 10));
        Assert.Equal(0, HitMod(s, gun.Id, target.Id, GfNames.Hold, 12));                                // "over 12”"
    }

    [Fact]
    public void Enemies_get_minus_one_only_while_the_artillery_has_not_moved_this_round()
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, []);
        var gun = Unit("Gun", "4+", "4+", [P(GfNames.Artillery)], 1, []);
        var s = State([shooter], [gun]).Under(RuleSets.Gf360);
        Assert.Equal(-1, HitMod(s, shooter.Id, gun.Id, GfNames.Hold, 13));
        Assert.Equal(0, HitMod(s, shooter.Id, gun.Id, GfNames.Hold, 12));
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new ConditionSet(new Subject(SubjectKind.Unit, gun.Id), GfNames.MovedThisRound, true), null));
        Assert.Equal(0, HitMod(s, shooter.Id, gun.Id, GfNames.Hold, 13));
    }
}
