using Tabletop.Content;
using Tabletop.GameServer.Setup;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Content-path review: an army is checked when it is picked and again when it is snapshotted.</summary>
public class ArmyLimitsTests
{
    static Army Army() => TestArmies.Build("A", 2, 3, modelRules: [(1, "Tough", 3)]);
    static Army WithModel(Func<ModelSlot, ModelSlot> change)
    {
        var a = Army();
        var u = a.Units[0];
        return a with { Units = [u with { Models = [change(u.Models[0]), .. u.Models.Skip(1)] }, .. a.Units.Skip(1)] };
    }

    [Fact]
    public void A_test_army_is_playable() => Assert.Null(ArmyLimits.Default.Breach(Army().Units));

    public static TheoryData<string, Army> Unplayable => new()
    {
        { "base too small", WithModel(m => m with { Base = Base.Circle(0.1) }) },
        { "base too large", WithModel(m => m with { Base = new Base(BaseShape.Rect, 40, 2) }) },
        { "unknown base shape", WithModel(m => m with { Base = new Base((BaseShape)9, 1, 1) }) },
        { "height", WithModel(m => m with { Height = 100 }) },
        { "parameter", WithModel(m => m with { Properties = [new PropertyInstance("Tough", 1_000_000)] }) },
        { "negative parameter", WithModel(m => m with { Properties = [new PropertyInstance("Tough", -1)] }) },
        { "quality", Army() with { Units = [Army().Units[0] with { Stats = new Dictionary<string, string> { ["Quality"] = "9+" } }] } },
        { "attacks", WithModel(m => m with { Weapons = [new Weapon(Guid.NewGuid(), "Gun", new Dictionary<string, string> { ["Attacks"] = "5000" }, [])] }) },
        { "models", Army() with { Units = [Army().Units[0] with { Models = Enumerable.Range(0, 51).Select(_ => Army().Units[0].Models[0]).ToList() }] } },
    };

    [Theory, MemberData(nameof(Unplayable))]
    public void An_army_out_of_bounds_is_named(string _, Army army) => Assert.NotNull(ArmyLimits.Default.Breach(army.Units));

    [Fact]
    public async Task Picking_an_unplayable_army_is_refused_with_its_reason()
    {
        var db = new InMemorySeeder();
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Lobby);
        var army = await db.SeedArmyAsync(seeded.CreatorAccountId, WithModel(m => m with { Height = 500 }));
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await client.ExpectAsync<AtRest>();
        await client.SendAsync(new EditMessage(Guid.NewGuid(), new PickArmy(army.Id)));
        var refused = await client.ExpectAsync<Refused>();
        Assert.Equal(RefusalReason.Illegal, refused.Reason);
        Assert.Contains("height", refused.Detail);
    }

    [Fact]
    public void An_army_of_the_games_edition_has_no_edition_breach()
    {
        Assert.Null(ArmyLimits.EditionBreach(ArmyEditions.V35, RuleSets.Gf351));
        Assert.Null(ArmyLimits.EditionBreach(ArmyEditions.V36, RuleSets.Gf360));
    }

    [Fact]
    public void An_edition_breach_names_both_editions()
    {
        Assert.Equal("this army is a 3.5 list; the game plays Grimdark Future 3.6", ArmyLimits.EditionBreach(ArmyEditions.V35, RuleSets.Gf360));
        Assert.Equal("this army is a 3.6 list; the game plays Grimdark Future 3.5", ArmyLimits.EditionBreach(ArmyEditions.V36, RuleSets.Gf351));
    }
}
