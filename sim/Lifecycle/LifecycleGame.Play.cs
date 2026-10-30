using System.Net;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Lifecycle;

public sealed partial class LifecycleGame
{
    /// <summary>
    /// The game as the server sent it once play began, played by SocketDriver and RunLoop with the default policy, the spectator recording.
    /// The game ends by its rules at round 4's end. Returns the driver's final projection.
    /// </summary>
    async Task<Projection> PlayGameAsync(CancellationToken ct)
    {
        _step = "play";
        var info = CreatorSocket.Game;
        var board = CreatorSocket.Board!;
        var recorder = info.Participants.Single(p => p.AccountId == _spectator.AccountId);
        var setAside = new Dictionary<Guid, IReadOnlyList<int>>();
        Guid? ambusher = null;
        if (shape.Ambush)
        {
            var army = board.Armies.Single(a => info.Participants.Single(p => p.Id == a.ParticipantId).AccountId == Creator.AccountId);
            var riders = army.Snapshot.Units.Single(u => u.Name == LifecycleArmy.AmbushUnit);
            setAside[army.ParticipantId] = [riders.Ordinal];
            ambusher = riders.Id;
        }
        var compiled = LifecycleScenario.From(name, Seed, info, board, recorder.Id, SelfPlay, setAside, RoundCap, shape.Rules);
        var accounts = _players.Append(_spectator).ToDictionary(a => a.AccountId);
        var credentials = LifecycleScenario.Credentials(compiled, accounts.ToDictionary(kv => kv.Key, kv => kv.Value.Session), async (account, token) =>
        {
            using var fresh = await ManagementApi.SignInAsync(endpoints.Management, accounts[account].Name, token);
            return fresh.Session;
        });

        await using var driver = await SocketDriver.StartAsync(compiled, new ClientWebSocketConnector(endpoints.GameServer), credentials, ct);
        var loop = new RunLoop(driver, compiled, new ComposedAnswers(AnswerScript.Empty, compiled.Scenario.Policy()), HandPlacement.ChooserFor(Seed));
        var report = await loop.RunAsync(ct);
        Check(report.Outcome is RunOutcome.Ended && report.Failures.Count == 0,
            $"the game played to {report.Outcome.Kind} at round {report.Rounds}:\n{string.Join("\n", report.Failures)}");
        var final = await driver.ProjectionAsync(ct);

        _step = "turn order";
        var log = new LogQueries(loop.Log, final);
        var ordinals = info.Teams.ToDictionary(t => t.Id, t => t.Ordinal);
        foreach (var failure in new[]
        {
            FreeForAllChecks.RollOff(log, shape.Teams), FreeForAllChecks.DeploymentRotation(log), FreeForAllChecks.DeployedInZones(log, ordinals, compiled.Zones),
            FreeForAllChecks.TurnOrder(log), FreeForAllChecks.TurnRotation(log), FreeForAllChecks.EveryTeamActivated(log), FreeForAllChecks.Winners(log),
        })
            Check(failure is null, failure!);

        if (ambusher is { } unit)
        {
            _step = "ambush";
            var queries = log;
            var round = 0;
            var rounds = new Dictionary<long, int>();
            foreach (var e in loop.Log)
            {
                if (e.Payload is RoundStarted r) round = r.Round;
                rounds[e.Seq] = round;
            }
            var arrivals = queries.Deployments.Where(d => d.Unit == unit).ToList();
            Check(arrivals.Count > 0, "the Beast Riders never deployed");
            Check(arrivals.All(d => d.Question == AskQuestions.DeployAmbusher && rounds[d.Seq] >= 2),
                $"the Beast Riders deployed by {string.Join(", ", arrivals.Select(d => $"{d.Question} in round {rounds[d.Seq]}"))}");
            Check(final.Models.Values.Where(m => m.UnitId == unit).All(m => m.Location is not OnShelf), "a Beast Rider is still on the shelf");
        }
        return final;
    }

    /// <summary>The row follows the log to ended, every participant's list says so, and the creator's socket from the lobby saw the end.</summary>
    async Task EndAsync(CancellationToken ct)
    {
        _step = "ended";
        var deadline = DateTimeOffset.UtcNow + EndedWithin;
        GameInfo? game;
        while ((game = await Creator.GameAsync(GameId!.Value, ct))?.Status != GameStatus.Ended && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        Check(game?.Status == GameStatus.Ended, $"the row is {game?.Status} {EndedWithin.TotalSeconds:0}s after the log ended the game");
        await StatusAsync(GameStatus.Ended, Participants(late: true), ct);
        await CreatorSocket.UntilAsync(() => CreatorSocket.Events.Any(e => e.Payload is GameEnded), "game_ended on the creator's lobby socket", ct);
    }

    /// <summary>The client's replay: `/ws?watch=1` with a participant's session gets the whole log of the ended game and may send nothing.</summary>
    async Task ReplayAsync(Projection final, CancellationToken ct)
    {
        _step = "replay";
        await using var watcher = await ClientSocket.OpenAsync(new ClientWebSocketConnector(endpoints.WatchUri), $"{Creator.Name} (watch)", Creator.Session.Jwt, GameId!.Value, ct);
        Check(watcher.Welcome.ParticipantId == Guid.Empty, $"the watcher's Welcome names participant {watcher.Welcome.ParticipantId}");
        Check(watcher.Game.Status == GameStatus.Ended && watcher.Board is not null, $"the watcher sees a {watcher.Game.Status} game, board {(watcher.Board is null ? "missing" : "sent")}");
        var applier = new EventApplier(watcher.Board!, watcher.Game);
        foreach (var e in watcher.Events) applier.Apply(e);
        Check(applier.Projection.Ended is not null, "the watcher's log does not end the game");
        Check(applier.Projection.LastSeq == final.LastSeq && watcher.Rest == final.LastSeq,
            $"the watcher's log reaches seq {applier.Projection.LastSeq} (rest {watcher.Rest}), the recorder's {final.LastSeq}");
        var refused = await watcher.RefusedCommandAsync(new AbandonSequence(Guid.CreateVersion7()), ct);
        Check(refused.Reason == RefusalReason.Watcher, $"the watcher's command was refused {refused.Reason}, not Watcher");
    }

    /// <summary>Someone else's delete is 404; the creator's is 204, after which the game is gone from the API and every list.</summary>
    async Task DeleteAsync(CancellationToken ct)
    {
        _step = "delete";
        foreach (var socket in _sockets.Values) await socket.DisposeAsync();
        _sockets.Clear();
        var notTheCreator = await _spectator.DeleteGameAsync(GameId!.Value, ct);
        Check(notTheCreator == HttpStatusCode.NotFound, $"the spectator's delete answered {(int)notTheCreator}, not 404");
        var deleted = await Creator.DeleteGameAsync(GameId.Value, ct);
        Check(deleted == HttpStatusCode.NoContent, $"the creator's delete answered {(int)deleted}, not 204");
        _deleted = true;
        Check(await Creator.GameAsync(GameId.Value, ct) is null, "GET /v1/games/{id} still finds the deleted game");
        foreach (var account in Participants(late: true))
            Check((await account.JoinedAsync(ct)).All(g => g.Id != GameId), $"{account.Name}'s joined list still holds the deleted game");
    }
}
