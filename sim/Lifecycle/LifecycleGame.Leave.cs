using System.Net;
using Tabletop.Protocol;
using Tabletop.Sim.Drivers;

namespace Tabletop.Sim.Lifecycle;

public sealed partial class LifecycleGame
{
    /// <summary>
    /// A lobby game a player joins, takes a team in and leaves as the client's Leave does (`POST /leave`, then its socket closes): its row
    /// goes, the creator's open lobby and a fresh connect no longer show it, and the game stays in the lobby. Then the delete: the leaver's is 404, the creator's 204.
    /// </summary>
    async Task LeaveAndDeleteAsync(CancellationToken ct)
    {
        var leaver = _players[1];
        _step = "join";
        await leaver.JoinAsync(GameId!.Value, ct);
        await StatusAsync(GameStatus.Lobby, [Creator, leaver], ct);

        _step = "lobby";
        await ConnectAsync(0, ct);
        var socket = await ConnectAsync(1, ct);
        for (var n = 1; n <= shape.Teams; n++)
        {
            var count = n;
            await CreatorSocket.EditAsync(new CreateTeam(), g => g.Teams.Count == count, ct);
        }
        var team = TeamId(CreatorSocket.Game, 2);
        await socket.EditAsync(new PickTeam(team), g => Holds(g, leaver, team), ct);
        await CreatorSocket.UntilAsync(() => Holds(CreatorSocket.Game, leaver, team), "the creator's lobby to show the seat", ct);

        _step = "leave";
        var left = await leaver.LeaveAsync(GameId.Value, ct);
        Check(left == HttpStatusCode.NoContent, $"the leave answered {(int)left}, not 204");
        await socket.DisposeAsync();
        _sockets.Remove(1);
        await CreatorSocket.UntilAsync(() => CreatorSocket.Game.Participants.All(p => p.AccountId != leaver.AccountId), "the creator's lobby to drop the leaver", ct);
        var game = await Creator.GameAsync(GameId.Value, ct);
        Check(game is { Status: GameStatus.Lobby } && game.Participants.All(p => p.AccountId != leaver.AccountId),
            $"after the leave the game is {game?.Status} with {game?.Participants.Count(p => p.AccountId == leaver.AccountId)} rows of the leaver");
        Check((await leaver.JoinedAsync(ct)).All(g => g.Id != GameId), "the leaver's joined list still holds the game");
        await StatusAsync(GameStatus.Lobby, [Creator], ct);
        await using (var again = await ClientSocket.OpenAsync(new ClientWebSocketConnector(endpoints.GameServer), Creator.Name, Creator.Session.Jwt, GameId.Value, ct))
            Check(again.Welcome.Game.Participants.All(p => p.AccountId != leaver.AccountId), "the creator's lobby, connected after the leave, still shows the leaver");

        _step = "delete";
        foreach (var open in _sockets.Values) await open.DisposeAsync();
        _sockets.Clear();
        var byLeaver = await leaver.DeleteGameAsync(GameId.Value, ct);
        Check(byLeaver == HttpStatusCode.NotFound, $"the leaver's delete answered {(int)byLeaver}, not 404");
        var deleted = await Creator.DeleteGameAsync(GameId.Value, ct);
        Check(deleted == HttpStatusCode.NoContent, $"the creator's delete answered {(int)deleted}, not 204");
        _deleted = true;
        Check(await Creator.GameAsync(GameId.Value, ct) is null, "GET /v1/games/{id} still finds the deleted game");
        Check((await Creator.JoinedAsync(ct)).All(g => g.Id != GameId), "the creator's joined list still holds the deleted game");
    }
}
