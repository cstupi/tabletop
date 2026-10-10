using System.Net.WebSockets;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

/// <summary>Set-aside units and per-model entries, on both drivers.</summary>
[Collection("Postgres")]
public class ScenarioExtensionsTests(PostgresFixture pg)
{
    static CancellationToken Ct => CancellationToken.None;

    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    static readonly IReadOnlyDictionary<string, string> HeroStats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "3+" };
    static readonly ScenarioWeapon Pistol = new("Pistol", new Dictionary<string, string> { ["Range"] = "12\"", ["Attacks"] = "1" }, []);

    /// <summary>Red unit 1 is a set-aside Scout, Red unit 2 a set-aside Ambusher; unit 3 deploys as usual. Round 2 brings the Ambusher in. Strict enforcement.</summary>
    static Scenario Reserves(ulong seed, SchedulerShape shape = SchedulerShape.AlternatingActivation) => new ScenarioBuilder("reserves", seed).RoundCap(2)
        .Shape(shape).Enforcing(Enforcement.Strict)
        .Army("Red", 3, 2, unitRules: [new ScenarioRule(1, "Scout"), new ScenarioRule(2, "Ambush")], setAside: [1, 2])
        .Army("Blue", 2, 2)
        .Build();

    static Scenario WithHero() => new ScenarioBuilder("hero", 1).RoundCap(1)
        .Army("Red", 1, 3, models: [new ScenarioModel(1, 2, [new ScenarioRule(0, "Hero")], HeroStats, [Pistol])])
        .Army("Blue", 1, 3)
        .Build();

    static async Task<(RunReport Report, RunLoop Loop)> PlayAsync(IGameDriver driver, CompiledScenario c)
    {
        var loop = new RunLoop(driver, c, new ComposedAnswers(c.Scenario.Script, c.Scenario.Policy()), HandPlacement.ChooserFor(c.Scenario.Seed));
        return (await loop.RunAsync(Ct), loop);
    }

    async Task<T> OverSocketAsync<T>(CompiledScenario c, Func<IGameDriver, Task<T>> play)
    {
        await new PostgresPropertiesStore(pg.Db).SeedAsync(PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == GrimdarkFutureRules.RulesId), Ct);
        var credentials = new SocketCredentials(c.Info.Participants.Select(p => new MintedSession(TestKeys.Mint(p.AccountId), p.AccountId)).ToList());
        var seeder = new SocketRowSeeder(pg.Db, new PostgresGameRows(pg.Db));
        using var factory = new GameServerFactory();
        try
        {
            await seeder.SeedAsync(c, credentials, Ct);
            await using var driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), credentials, Ct);
            return await play(driver);
        }
        finally
        {
            try { await seeder.DeleteAsync(c.Info.Id, Ct); }
            finally { await TestPool.DeleteAccountsAsync(pg.Db, credentials); }
        }
    }

    /// <summary>The unit answered at each deploy-family Ask, by question, in log order.</summary>
    static IReadOnlyList<(string Question, Guid Unit)> Deployed(IReadOnlyList<GameEvent> log)
    {
        var questions = log.Select(e => e.Payload).OfType<AskOpened>().ToDictionary(o => o.Ask.Id, o => o.Ask.Question);
        return log.Select(e => e.Payload).OfType<AskClosed>()
            .SelectMany(c => c.Answers.Select(a => (Question: questions.GetValueOrDefault(c.AskId), a.Answer)))
            .Where(x => x.Question is AskQuestions.Deploy or AskQuestions.DeployScouts or AskQuestions.DeployAmbusher && x.Answer is SelectedPieces)
            .SelectMany(x => ((SelectedPieces)x.Answer).PieceIds.Select(u => (x.Question!, u))).ToList();
    }

    static void AssertReserves(CompiledScenario c, RunReport report, RunLoop loop, Projection final)
    {
        Assert.True(report.Passed, string.Join("\n", report.Failures));
        var red = c.Board.Armies[0].Snapshot.Units;
        var deployed = Deployed(loop.Log);
        Assert.Contains((AskQuestions.DeployScouts, red[0].Id), deployed);
        Assert.Contains((AskQuestions.DeployAmbusher, red[1].Id), deployed);
        Assert.DoesNotContain(deployed, d => d.Question == AskQuestions.Deploy && (d.Unit == red[0].Id || d.Unit == red[1].Id));
        Assert.All(red.Take(2).SelectMany(u => u.Models), m => Assert.IsType<OnTable>(final.Models[m.Id].Location));
    }

    [Theory]
    [InlineData(SchedulerShape.AlternatingActivation)]
    [InlineData(SchedulerShape.PlayerTurn)]
    public async Task A_set_aside_scout_and_ambusher_deploy_through_their_own_asks_in_process(SchedulerShape shape)
    {
        var c = ScenarioCompiler.Compile(Reserves(1, shape));
        await using var driver = await InProcessDriver.StartAsync(c, Ct);
        var (report, loop) = await PlayAsync(driver, c);
        AssertReserves(c, report, loop, await driver.ProjectionAsync(Ct));
    }

    [Fact]
    public async Task A_set_aside_scout_and_ambusher_deploy_through_their_own_asks_over_the_socket()
    {
        var c = ScenarioCompiler.Compile(Reserves(1));
        await OverSocketAsync(c, async driver =>
        {
            var (report, loop) = await PlayAsync(driver, c);
            AssertReserves(c, report, loop, await driver.ProjectionAsync(Ct));
            return 0;
        });
    }

    [Fact]
    public void A_model_gets_its_own_rules_stats_and_weapons_and_the_others_keep_the_armys()
    {
        var c = ScenarioCompiler.Compile(WithHero());
        var models = c.Board.Armies[0].Snapshot.Units[0].Models;
        Assert.Equal(["Hero"], models[1].Properties.Select(p => p.Name));
        Assert.Equal(HeroStats, models[1].Stats);
        Assert.Equal(["Pistol"], models[1].Weapons.Select(w => w.Name));
        Assert.All([models[0], models[2]], m =>
        {
            Assert.Empty(m.Properties);
            Assert.Empty(m.Stats);
            Assert.Equal(["Rifle"], m.Weapons.Select(w => w.Name));
        });
    }

    [Fact]
    public void Model_rules_add_to_the_units_model_rules()
    {
        var s = new ScenarioBuilder("both", 1)
            .Army("Red", 1, 2, modelRules: [new ScenarioRule(1, "Caster", 2)], models: [new ScenarioModel(1, 1, [new ScenarioRule(0, "Hero")])])
            .Army("Blue", 1, 1).Build();
        var models = ScenarioCompiler.Compile(s).Board.Armies[0].Snapshot.Units[0].Models;
        Assert.Equal(["Caster", "Hero"], models[0].Properties.Select(p => p.Name));
        Assert.Equal(["Caster"], models[1].Properties.Select(p => p.Name));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 4)]
    public void A_model_outside_the_army_is_a_programming_error(int unit, int model) =>
        Assert.Throws<InvalidOperationException>(() => new ScenarioBuilder("bad", 1)
            .Army("Red", 1, 3, models: [new ScenarioModel(unit, model)]).Army("Blue", 1, 1).Build());

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Setting_aside_a_unit_the_army_lacks_is_a_programming_error(int unit) =>
        Assert.Contains($"sets aside unit {unit}; its units are 1..1",
            Assert.Throws<InvalidOperationException>(() => new ScenarioBuilder("bad", 1).Army("Red", 1, 1, setAside: [unit]).Army("Blue", 1, 1).Build()).Message);

    static void AssertHero(CompiledScenario c, Projection p)
    {
        var hero = c.Board.Armies[0].Snapshot.Units[0].Models[1];
        Assert.Contains(p.Models[hero.Id].Slot.Properties, x => x.Name == "Hero");
        Assert.Equal("3+", p.Models[hero.Id].Slot.Stats["Defense"]);
        Assert.Equal(["Pistol"], p.Models[hero.Id].Slot.Weapons.Select(w => w.Name));
    }

    [Fact]
    public async Task The_engine_holds_the_models_own_entries_on_both_drivers()
    {
        var c = ScenarioCompiler.Compile(WithHero());
        await using (var driver = await InProcessDriver.StartAsync(c, Ct))
        {
            var (report, _) = await PlayAsync(driver, c);
            Assert.True(report.Passed, string.Join("\n", report.Failures));
            AssertHero(c, await driver.ProjectionAsync(Ct));
        }
        var s = ScenarioCompiler.Compile(WithHero());
        await OverSocketAsync(s, async driver =>
        {
            var (report, _) = await PlayAsync(driver, s);
            Assert.True(report.Passed, string.Join("\n", report.Failures));
            AssertHero(s, await driver.ProjectionAsync(Ct));
            return 0;
        });
    }
}
