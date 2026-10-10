using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class Batch1UnbatchedTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static LiveSet Attack(string rule, AttackKind? kind, string? point = null)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P(rule)], Gun("Gun", "24\"", 1));
        var b = Unit("B", "4+", "4+", [], 1, [P(rule)]);
        var s = State([a], [b]);
        return LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a.Id), kind is null ? null : Party.Whole(s, b.Id), kind, null, 12, [], point));
    }

    static int Hit(LiveSet l) => l.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
    static int OnSix(LiveSet l, TargetQuantity q) => l.Effects(Side.Attacker, EffectKind.AddHit, new DieContext("hit", 6, 6, true))
        .Where(x => (x.Effect.TargetQuantity ?? TargetQuantity.Hits) == q).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    [Fact]
    public void Bad_shot_bloodborn_and_brutal_fighter_act_on_their_attacks()
    {
        Assert.Equal((-1, 0), (Hit(Attack("Bad Shot", AttackKind.Shooting)), Hit(Attack("Bad Shot", AttackKind.Melee))));
        Assert.Equal((1, 1), (OnSix(Attack("Bloodborn", AttackKind.Shooting), TargetQuantity.Attacks), OnSix(Attack("Bloodborn", AttackKind.Melee), TargetQuantity.Attacks)));
        Assert.Equal((1, 0), (OnSix(Attack("Brutal Fighter", AttackKind.Melee), TargetQuantity.Hits), OnSix(Attack("Brutal Fighter", AttackKind.Shooting), TargetQuantity.Hits)));
    }

    [Fact]
    public void Knightborn_ignores_wounds_on_six_and_on_four_from_a_spell()
    {
        IReadOnlyList<int?> Thresholds(AttackKind kind) => RollEffects.OwnRolls(Attack("Knightborn", kind), Side.Defender,
            c => c is { Kind: EffectKind.OverrideResult, TargetResult: TargetResult.Wound }).Select(x => x.Roll.Threshold).ToList();
        Assert.Equal(new int?[] { 6 }, Thresholds(AttackKind.Shooting));
        Assert.Equal(new int?[] { 4 }, Thresholds(AttackKind.Spell));
    }

    [Fact]
    public void Unpredictable_rolls_at_the_start_of_any_attack_and_wolfborn_places_within_d3_on_activation()
    {
        var roll = Core.Entry("Unpredictable")!.Effects.Single();
        Assert.Equal(EffectKind.Roll, roll.Kind);
        Assert.Contains(roll.Conditions, c => c.Kind == ConditionKind.AtPoint && c.Values.SequenceEqual([GfNames.AttackStart]));
        Assert.Contains(roll.Conditions, c => c.Kind == ConditionKind.AttackKind && c.Values.Order().SequenceEqual(["melee", "shooting"]));
        var place = Assert.Single(Attack("Wolfborn", null, GfNames.ActivationStart).AtPoint(Side.Attacker, GfNames.ActivationStart));
        Assert.Equal((EffectKind.Place, 1, "d3"), (place.Effect.Kind, place.Effect.Amount ?? 0, place.Effect.AmountDice));
    }

    [Fact]
    public void Unique_is_played_and_does_nothing()
    {
        var unique = Core.Entry("Unique")!;
        Assert.Equal((true, 0, CatalogKind.Model), (unique.Played, unique.Effects.Count, unique.Kind));
    }

    [Fact]
    public void Banner_is_plus_one_to_morale_and_musician_plus_one_inch_on_every_move()
    {
        var banner = Attack("Banner", AttackKind.Shooting);
        Assert.Equal(1, banner.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "morale").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter)));
        var musician = Attack("Musician", null);
        Assert.Equal((7.0, 13.0, 13.0), (StatReader.ActionDistance(musician, Side.Attacker, GfNames.Advance), StatReader.ActionDistance(musician, Side.Attacker, GfNames.Rush),
            StatReader.ActionDistance(musician, Side.Attacker, GfNames.Charge)));
    }
}
