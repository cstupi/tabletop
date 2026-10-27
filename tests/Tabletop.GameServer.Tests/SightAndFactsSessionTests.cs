using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>The sight question and model facts over the session (in-game UX spec → Sight question, Model facts).</summary>
public class SightAndFactsSessionTests
{
    static readonly GameServerOptions Options = new() { DbConnection = "", StytchProjectId = "p", StytchJwksUrl = "u" };

    sealed class Engines(IGameRules rules) : IRulesEngineFactory
    {
        public IRulesEngine Create(IGameRules _, GameInfo game) => new RulesEngine(rules, TestProperties.Core, new ScriptedDice(4), new NoScheduler());
    }

    /// <summary>SightLineTests.Table's two units, playing, observers armed with a 12" rifle; the log holds the deployment event.</summary>
    static async Task<(GameSession Session, InMemoryEventLog Log, GameState State, Tabletop.Content.Unit Observers, Tabletop.Content.Unit Targets)> PlayingAsync(double wallX = 0, IGameRules? rules = null)
    {
        rules ??= GrimdarkFutureRules.Instance;
        var (table, observers, targets, _) = SightLineTests.Table(wallX, Gun("Rifle", "12\"", 1));
        var state = new GameState(table.Info, table.Board, table.Applier, rules);
        var log = new InMemoryEventLog();
        await log.AppendAsync(state.Info.Id, state.Applier.Log, CancellationToken.None);
        var services = new SessionServices(null!, log, new Engines(rules), Options, NullLoggerFactory.Instance);
        var session = new GameSession(state.Info.Id, state.Info, rules, null, state, services.Engines.Create(rules, state.Info), services);
        await session.StartAsync();
        return (session, log, state, observers, targets);
    }

    [Fact]
    public async Task A_sight_query_is_answered_to_the_asker_alone_and_logs_nothing()
    {
        var (session, log, _, observers, targets) = await PlayingAsync();
        var (clay, opponent) = (new RecordingClient(Samples.P1), new RecordingClient(P2));
        await session.AttachAsync(clay); await session.AttachAsync(opponent);
        var id = Guid.CreateVersion7();
        await session.ReceiveAsync(clay, new SightQuery(id, observers.Id, targets.Id));
        Assert.Equal(id, Assert.IsType<SightAnswer>(Assert.Single(clay.Received)).QueryId);       // no AtRest
        Assert.Empty(opponent.Received);
        Assert.Single(await log.ReadAsync(session.GameId, 0, CancellationToken.None));
    }

    [Fact]
    public async Task The_answer_is_the_rules_sight_and_who_can_shoot()
    {
        var (session, _, state, observers, targets) = await PlayingAsync(wallX: -3.5);
        var clay = new RecordingClient(Samples.P1);
        await session.AttachAsync(clay);
        await session.ReceiveAsync(clay, new SightQuery(Guid.CreateVersion7(), observers.Id, targets.Id));
        var answer = Assert.IsType<SightAnswer>(Assert.Single(clay.Received));
        var sight = Sight.From(state, observers.Id);
        var expected = observers.Models.SelectMany(f => targets.Models.Select(t => sight.Line(f.Id, t.Id)!)).ToList();
        Assert.Equal(expected.Select(l => (l.FromModelId, l.ToModelId, l.Clear, l.Screened)), answer.Lines.Select(l => (l.FromModelId, l.ToModelId, l.Clear, l.Screened)));
        Assert.Equal([observers.Models[1].Id], answer.CanShoot);
    }

    [Fact]
    public async Task A_unit_unknown_or_off_the_table_gives_empty_lists()
    {
        var (session, _, state, observers, targets) = await PlayingAsync();
        var clay = new RecordingClient(Samples.P1);
        await session.AttachAsync(clay);
        await session.ReceiveAsync(clay, new SightQuery(Guid.CreateVersion7(), observers.Id, Guid.CreateVersion7()));
        var unknown = Assert.IsType<SightAnswer>(Assert.Single(clay.Received));
        Assert.Empty(unknown.Lines); Assert.Empty(unknown.CanShoot);

        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new MoveModels(targets.Models.Select(m =>
            new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location, new OnShelf())).ToList())));
        clay.Received.Clear();
        await session.ReceiveAsync(clay, new SightQuery(Guid.CreateVersion7(), observers.Id, targets.Id));
        var shelved = Assert.IsType<SightAnswer>(Assert.Single(clay.Received));
        Assert.Empty(shelved.Lines); Assert.Empty(shelved.CanShoot);
    }

    [Fact]
    public async Task Before_play_a_sight_query_gives_empty_lists()
    {
        var log = new InMemoryEventLog();
        var info = Samples.Game with { Status = GameStatus.Lobby };
        var services = new SessionServices(null!, log, new InertEngineFactory(), Options, NullLoggerFactory.Instance);
        var session = new GameSession(info.Id, info, GrimdarkFutureRules.Instance, null, null, null, services);
        await session.StartAsync();
        var clay = new RecordingClient(Samples.P1);
        await session.AttachAsync(clay);
        await session.ReceiveAsync(clay, new SightQuery(Guid.CreateVersion7(), Samples.Unit, Samples.Unit));
        var answer = Assert.IsType<SightAnswer>(Assert.Single(clay.Received));
        Assert.Empty(answer.Lines); Assert.Empty(answer.CanShoot);
    }

    [Fact]
    public async Task A_connecting_client_is_told_every_models_max_wounds_before_rest()
    {
        var (session, _, state, observers, targets) = await PlayingAsync();
        var clay = new RecordingClient(Samples.P1);
        await session.ConnectAsync(clay);
        var kinds = clay.Received.Select(m => m.GetType()).ToList();
        Assert.Equal([typeof(Welcome), typeof(BoardMessage), typeof(EventsMessage), typeof(ModelFacts), typeof(AtRest)], kinds);
        var facts = clay.Received.OfType<ModelFacts>().Single().MaxWounds;
        Assert.Equal(state.Projection.Models.Keys.Order(), facts.Keys.Order());
        Assert.All(targets.Models, m => Assert.Equal(3, facts[m.Id]));
        Assert.All(observers.Models, m => Assert.Equal(1, facts[m.Id]));
    }

    [Fact]
    public async Task Facts_are_broadcast_after_a_batch_only_when_they_change()
    {
        var rules = FakeRules.Gf();
        rules.MaxWoundsHook = (s, m) => 1 + s.Projection.Models[m].Wounds;                      // a fact that moves with the log
        var (session, _, _, _, targets) = await PlayingAsync(rules: rules);
        var (clay, opponent) = (new RecordingClient(Samples.P1), new RecordingClient(P2));
        await session.ConnectAsync(clay); await session.ConnectAsync(opponent);
        clay.Received.Clear(); opponent.Received.Clear();

        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 2)));
        Assert.DoesNotContain(clay.Received, m => m is ModelFacts);
        Assert.DoesNotContain(opponent.Received, m => m is ModelFacts);

        clay.Received.Clear(); opponent.Received.Clear();
        var hurt = targets.Models[0].Id;
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetWounds(hurt, 2)));
        foreach (var c in new[] { clay, opponent })
        {
            Assert.Equal([typeof(EventsMessage), typeof(ModelFacts), typeof(AtRest)], c.Received.Select(m => m.GetType()));
            Assert.Equal(3, c.Received.OfType<ModelFacts>().Single().MaxWounds[hurt]);
        }
    }

    [Fact]
    public async Task Facts_carry_DeployInside_and_are_broadcast_when_it_changes()
    {
        var rules = FakeRules.Gf();
        rules.DeployInsideHook = s => s.Projection.Scores.Values.Sum() > 0
            ? new Dictionary<Guid, IReadOnlyList<Guid>> { [Samples.Unit] = [s.Projection.Models.Keys.First()] } : new Dictionary<Guid, IReadOnlyList<Guid>>();
        var (session, _, state, _, _) = await PlayingAsync(rules: rules);
        var clay = new RecordingClient(Samples.P1);
        await session.ConnectAsync(clay);
        Assert.Empty(clay.Received.OfType<ModelFacts>().Single().DeployInside!);
        clay.Received.Clear();
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 2)));
        Assert.Equal([state.Projection.Models.Keys.First()], clay.Received.OfType<ModelFacts>().Single().DeployInside![Samples.Unit]);
        clay.Received.Clear();
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 3)));
        Assert.DoesNotContain(clay.Received, m => m is ModelFacts);
    }

    [Fact]
    public async Task Facts_carry_MaySetAside_and_are_broadcast_when_it_changes()
    {
        var rules = FakeRules.Gf();
        rules.MaySetAsideHook = s => s.Projection.Scores.Values.Sum() > 0 ? [Samples.Unit] : [];
        var (session, _, _, _, _) = await PlayingAsync(rules: rules);
        var clay = new RecordingClient(Samples.P1);
        await session.ConnectAsync(clay);
        Assert.Empty(clay.Received.OfType<ModelFacts>().Single().MaySetAside!);
        clay.Received.Clear();
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 2)));
        Assert.Equal([Samples.Unit], clay.Received.OfType<ModelFacts>().Single().MaySetAside!);
        clay.Received.Clear();
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 3)));
        Assert.DoesNotContain(clay.Received, m => m is ModelFacts);
    }
}
