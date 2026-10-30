using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class LiveProperties36Tests
{
    // A unit of 5 where 4 models carry the rule and one hero does not: 3.5.1 reads "all models" (no unit-level rule),
    // 3.6.0 p.14/p.15 read "most models" (one unit-level property).
    static (int Under351, int Under360) UnitLevel(string rule)
    {
        var unit = Unit("Squad", "4+", "4+", [], 5, [P(rule)]);
        var mixed = unit with { Models = unit.Models.Select((m, i) => i == 0 ? m with { Properties = [] } : m).ToList() };
        var state = State([Unit("Shooters", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1))], [mixed]);
        int Count(PropertySet set) => LiveProperties.Settle(new Interaction(state, set, Party.Whole(state, state.Board.Armies[0].Snapshot.Units[0].Id), Party.Whole(state, mixed.Id), AttackKind.Shooting, "Hold", 12, []))
            .On(Side.Defender).Count(p => p.Entry.Name == rule);
        return (Count(TestProperties.Core), Count(TestProperties.Core36));
    }

    [Fact]
    public void A_hero_without_regeneration_no_longer_switches_the_units_regeneration_off_under_3_6() => Assert.Equal((0, 1), UnitLevel("Regeneration"));

    [Fact]
    public void A_hero_without_fearless_no_longer_switches_the_units_fearless_off_under_3_6() => Assert.Equal((0, 1), UnitLevel("Fearless"));

    [Fact]
    public void A_hero_without_stealth_no_longer_switches_the_units_stealth_off_under_3_6() => Assert.Equal((0, 1), UnitLevel("Stealth"));
}
