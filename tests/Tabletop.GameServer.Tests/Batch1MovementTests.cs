using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class Batch1MovementTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static (LiveSet Live, AppliedEntries Applied, Tabletop.Content.Unit Unit, GameState State) Lone(string? point, params string[] rules)
    {
        var a = Unit("A", "4+", "4+", [], 1, [.. rules.Select(r => P(r))], Gun("Gun", "24\"", 1));
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        var applied = new AppliedEntries();
        return (LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a.Id), null, null, null, null, [], point), applied), applied, a, s);
    }

    [Fact]
    public void Agile_and_rapid_charge_lengthen_their_moves()
    {
        var (agile, _, _, _) = Lone(null, "Agile");
        Assert.Equal((7.0, 14.0, 14.0), (StatReader.ActionDistance(agile, Side.Attacker, GfNames.Advance), StatReader.ActionDistance(agile, Side.Attacker, GfNames.Rush), StatReader.ActionDistance(agile, Side.Attacker, GfNames.Charge)));
        var (charge, _, _, _) = Lone(null, "Rapid Charge");
        Assert.Equal((6.0, 16.0), (StatReader.ActionDistance(charge, Side.Attacker, GfNames.Advance), StatReader.ActionDistance(charge, Side.Attacker, GfNames.Charge)));
    }

    [Fact]
    public void Swift_ignores_slow_and_is_listed_where_the_distance_is_read()
    {
        var (live, applied, _, _) = Lone(null, "Slow", "Swift");
        Assert.Equal(6.0, StatReader.ActionDistance(live, Side.Attacker, GfNames.Advance));
        Assert.Contains("Swift", applied.Names!);
    }

    [Fact]
    public void Increased_shooting_range_adds_six_inches_when_shooting()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Increased Shooting Range")], Gun("Gun", "24\"", 1));
        var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a], [b]);
        var gun = s.Projection.Models.Values.Single(m => m.UnitId == a.Id).Slot.Weapons.Single().Id;
        var live = LiveProperties.Settle(new Interaction(s, Core, Party.Whole(s, a.Id), Party.Whole(s, b.Id), AttackKind.Shooting, null, 20, []));
        Assert.Equal(30.0, StatReader.WeaponRange(live, Side.Attacker, gun));
    }

    [Fact]
    public void Guerrilla_moves_three_after_shooting_and_its_boost_six()
    {
        Assert.Equal(new int?[] { 3 }, Lone(GfNames.AfterShooting, "Guerrilla").Live.AtPoint(Side.Attacker, GfNames.AfterShooting).Select(x => x.Effect.Amount));
        Assert.Equal(new int?[] { 6 }, Lone(GfNames.AfterMelee, "Guerrilla", "Guerrilla Boost").Live.AtPoint(Side.Attacker, GfNames.AfterMelee).Select(x => x.Effect.Amount));
    }
}
