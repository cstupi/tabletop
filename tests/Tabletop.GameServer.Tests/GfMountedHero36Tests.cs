using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// A hero on a mount as the army import builds it from Army Forge's Voidys list: a Reaper Champion (Defense 4+, Tough(3)) on a Flying Beast
/// (Armor(3), Fear(2), Tough(12)) is one model with Tough(15) (3.6.0 p.13: "Effects from multiple instances of the same special rule or spell
/// don't stack, unless it is a rule with (X) in its name"), Armor(3) and Fear(2). p.15 Tough(X): "This model must take X wounds before being killed."
/// </summary>
public class GfMountedHero36Tests
{
    static Unit Champion() => Unit("Reaper Champion", "3+", "4+", [], 1, [P("Hero"), P("Tough", 15), P("Armor", 3), P("Fear", 2), P("Flying")],
        Melee("Stomp", 4, P("AP", 1)), Melee("Toxin Claws", 8, P("Bane")));

    static Sim Shot(int hits, int[] defense, out Unit champion)
    {
        var shooter = Unit("Shooter", "2+", "4+", [], 1, [], Gun("Volley", "24\"", hits));
        champion = Champion();
        var sim = Sim.Gf(RuleSets.Gf360, State([shooter], [champion], x1: -6, x2: 6), [.. Enumerable.Repeat(6, hits), .. defense, .. Enumerable.Repeat(6, 8)]);
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Volley"] = champion.Id }));
        return sim;
    }

    [Fact]
    public void Fourteen_wounds_leave_the_mounted_hero_standing_and_its_armor_saves_on_a_3()
    {
        // 15 hits; defense 1 fourteen times, then a 3, which Armor(3) saves and the champion's own 4+ would not.
        var sim = Shot(15, [.. Enumerable.Repeat(1, 14), 3], out var champion);
        var model = sim.Model(champion.Models[0].Id);
        Assert.Equal(14, model.Wounds);
        Assert.IsType<OnTable>(model.Location);
    }

    [Fact]
    public void The_fifteenth_wound_kills_the_mounted_hero()
    {
        var sim = Shot(15, [.. Enumerable.Repeat(1, 15)], out var champion);
        Assert.IsNotType<OnTable>(sim.Model(champion.Models[0].Id).Location);
    }
}
