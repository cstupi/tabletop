using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.Content;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Model facts while deployment runs (round 0): every unit already placed carries its threat, a unit still in the roster none.</summary>
public class ThreatsDuringDeploymentTests
{
    static readonly GameServerOptions Options = new() { DbConnection = "", StytchProjectId = "p", StytchJwksUrl = "u" };

    sealed class Engines(QueueDice dice) : IRulesEngineFactory
    {
        public IRulesEngine Create(IGameRules _, GameInfo game) =>
            new RulesEngine(GrimdarkFutureRules.V360, TestProperties.Core36, dice, new AlternatingActivationScheduler(TestProperties.Core36));
    }

    static Army Army(string name) => new(Guid.CreateVersion7(), name, "grimdark-future", "test",
        [Unit($"{name}1", "4+", "4+", [], 2, [], Gun("Rifle", "24\"", 1)), Unit($"{name}2", "4+", "4+", [], 1, [], Gun("Pistol", "12\"", 1))]) { Edition = ArmyEditions.V36 };

    /// <summary>A 3.6.0 game at its first deploy Ask; oneAccount puts both seats on the same account (solo play on both teams).</summary>
    static async Task<(GameSession Session, GameState State, TestGames.Built Game)> DeployingAsync(bool oneAccount)
    {
        var game = TestGames.Build([Army("A"), Army("B")], rules: GrimdarkFutureRules.V360);
        var state = game.State.Under(RuleSets.Gf360);
        if (oneAccount)
        {
            var account = state.Info.Participants[0].AccountId;
            var info = state.Info with { Participants = state.Info.Participants.Select(p => p with { AccountId = account }).ToList() };
            state = new GameState(info, state.Board, new EventApplier(state.Board, info), state.Rules);
        }
        var dice = new QueueDice();
        dice.Dice.Enqueue(6); dice.Dice.Enqueue(1);                                                   // team A wins the roll-off
        var services = new SessionServices(null!, new InMemoryEventLog(), new Engines(dice), Options, NullLoggerFactory.Instance);
        var session = new GameSession(state.Info.Id, state.Info, state.Rules, null, state, services.Engines.Create(state.Rules, state.Info), services);
        await session.StartAsync();
        Assert.Equal(0, session.Play!.Projection.Scheduler.Round);
        return (session, state, game);
    }

    static Ask DeployAsk(GameSession s) => s.Play!.Projection.OpenAsks.Values.Select(a => a.Ask).Single(a => a.Question == AskQuestions.Deploy);

    static async Task DeployAsync(GameSession session, RecordingClient seat, Guid unitId)
    {
        await session.ReceiveAsync(seat, new CommandMessage(Guid.CreateVersion7(), new AnswerAsk(DeployAsk(session).Id, new SelectedPieces([unitId]))));
        await session.ReceiveAsync(seat, new CommandMessage(Guid.CreateVersion7(), TestGames.Move(session.Play!, unitId)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_placed_unit_carries_its_threat_before_round_one(bool oneAccount)
    {
        var (session, state, game) = await DeployingAsync(oneAccount);
        var (a, b) = (new RecordingClient(game.Participants[0]), new RecordingClient(game.Participants[1]));
        await session.ConnectAsync(a); await session.ConnectAsync(b);
        Assert.Empty(a.Received.OfType<ModelFacts>().Single().Threats!);                               // nothing on the table yet

        var (a1, b1) = (TeamUnits.Of(state, game.Teams[0]).First(), TeamUnits.Of(state, game.Teams[1]).First());
        a.Received.Clear(); b.Received.Clear();
        await DeployAsync(session, a, a1.Id);
        foreach (var seat in new[] { a, b })
        {
            var threats = seat.Received.OfType<ModelFacts>().Last().Threats!;
            Assert.Equal([a1.Id], threats.Keys);
            Assert.Equal((12.0, 6.0), (threats[a1.Id].Charge, threats[a1.Id].ShootMove));
            Assert.Equal(a1.Definition.Models.ToDictionary(m => m.Id, _ => 24.0), threats[a1.Id].Ranges);
        }

        a.Received.Clear();
        await DeployAsync(session, b, b1.Id);
        Assert.Equal(0, session.Play!.Projection.Scheduler.Round);
        var both = a.Received.OfType<ModelFacts>().Last().Threats!;
        Assert.Equal(new[] { a1.Id, b1.Id }.Order(), both.Keys.Order());
        Assert.Equal(b1.Definition.Models.ToDictionary(m => m.Id, _ => 24.0), both[b1.Id].Ranges);
    }
}
