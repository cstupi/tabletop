using System.Net;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Lifecycle;

/// <summary>A step's assertion that did not hold.</summary>
public sealed class LifecycleCheckException(string message) : Exception(message);

/// <summary>
/// One shape, from creation to deletion, by the client's requests and frames (lifecycle suite spec). Each step asserts what the API and the
/// sockets say; the first that fails ends the shape with its step named. Whatever happens, the game and the armies it made are deleted.
/// </summary>
public sealed partial class LifecycleGame(LifecycleShape shape, string name, LifecycleEndpoints endpoints)
{
    /// <summary>The policy's and hand placement's seed. The dice are the game's own: management draws its rng_seed at creation.</summary>
    const ulong Seed = 1;
    /// <summary>Above four: the game ends by its rules at round 4's end (strict's game_ended, warn's game_over answered End), not by the cap.</summary>
    const int RoundCap = 5;
    static readonly TimeSpan EndedWithin = TimeSpan.FromSeconds(10);

    readonly List<ManagementApi> _players = [];
    ManagementApi _spectator = null!;
    readonly Dictionary<int, ClientSocket> _sockets = [];
    readonly List<(ManagementApi Owner, Guid Army)> _armies = [];
    string _step = "sign in";
    string _hash = "";
    bool _deleted;

    public Guid? GameId { get; private set; }
    ManagementApi Creator => _players[0];
    ClientSocket CreatorSocket => _sockets[0];

    public async Task<IReadOnlyList<string>> PlayAsync(CancellationToken ct)
    {
        var failures = new List<string>();
        try
        {
            foreach (var player in LifecycleRunner.Players.Take(shape.Accounts.Max() + 1))
                _players.Add(await ManagementApi.SignInAsync(endpoints.Management, player, ct));
            _spectator = await ManagementApi.SignInAsync(endpoints.Management, LifecycleRunner.Spectator, ct);

            await CreateAsync(ct);
            if (shape.LobbyOnly) await LeaveAndDeleteAsync(ct);
            else
            {
                await LobbyAsync(ct);
                await SetupAsync(ct);
                if (shape.Late is { } late) await LateJoinAsync(late, ct);
                var final = await PlayGameAsync(ct);
                await EndAsync(ct);
                await ReplayAsync(final, ct);
                await DeleteAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            failures.Add($"{_step}: {(ex is LifecycleCheckException or RefusedException or ManagementCallException ? ex.Message : ex.ToString())}");
        }
        finally
        {
            failures.AddRange(await CleanUpAsync());
        }
        return failures;
    }

    /// <summary>The game, unless the shape deleted it, then every army it imported; each failure is a line of its own.</summary>
    async Task<IReadOnlyList<string>> CleanUpAsync()
    {
        var failures = new List<string>();
        foreach (var socket in _sockets.Values) await socket.DisposeAsync();
        using var window = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            if (GameId is { } id && !_deleted && await Creator.DeleteGameAsync(id, window.Token) is not HttpStatusCode.NoContent and var status)
                failures.Add($"clean-up: deleting game {id} answered {(int)status}");
            foreach (var (owner, army) in _armies)
                if (await owner.DeleteArmyAsync(army, window.Token) is not HttpStatusCode.NoContent and var armyStatus)
                    failures.Add($"clean-up: deleting army {army} answered {(int)armyStatus}");
        }
        catch (Exception ex) { failures.Add($"clean-up: {ex.Message}"); }
        foreach (var player in _players) player.Dispose();
        _spectator?.Dispose();
        return failures;
    }

    static void Check(bool holds, string what)
    {
        if (!holds) throw new LifecycleCheckException(what);
    }

    /// <summary>Every account listed: `GET /v1/games/{id}` from the creator, and each account's row in its joined list, at `expected`.</summary>
    async Task StatusAsync(GameStatus expected, IEnumerable<ManagementApi> accounts, CancellationToken ct)
    {
        var game = await Creator.GameAsync(GameId!.Value, ct);
        Check(game?.Status == expected, $"GET /v1/games/{GameId} says {game?.Status.ToString() ?? "404"}, not {expected}");
        foreach (var account in accounts.Distinct())
        {
            var row = (await account.JoinedAsync(ct)).FirstOrDefault(g => g.Id == GameId);
            Check(row is not null, $"{account.Name}'s joined list does not hold the game");
            Check(row!.Joined && row.Status == expected, $"{account.Name}'s joined list says {row.Status} (joined {row.Joined}), not {expected}");
        }
    }

    /// <summary>The accounts holding a participant row now: the creator, the spectator and every seated account (the late one once joined).</summary>
    IEnumerable<ManagementApi> Participants(bool late = false) =>
        shape.Seats.Select(s => _players[s.Account]).Prepend(Creator).Append(_spectator)
            .Concat(late && shape.Late is { } l ? [_players[l.Account]] : []).Distinct();

    async Task<ClientSocket> ConnectAsync(int account, CancellationToken ct)
    {
        var api = _players[account];
        var socket = await ClientSocket.OpenAsync(new ClientWebSocketConnector(endpoints.GameServer), api.Name, api.Session.Jwt, GameId!.Value, ct);
        _sockets[account] = socket;
        var welcome = socket.Welcome;
        var row = welcome.Game.Participants.Where(p => p.AccountId == api.AccountId).MinBy(p => p.Id);
        Check(row is not null && welcome.ParticipantId == row.Id, $"{api.Name}'s Welcome names participant {welcome.ParticipantId}, not its row {row?.Id}");
        Check(welcome.Game.Id == GameId && welcome.Game.PropertiesHash == _hash, $"{api.Name}'s Welcome names game {welcome.Game.Id} on {welcome.Game.PropertiesHash}");
        var keys = welcome.Options.Options.Select(o => o.Key).ToList();
        Check(keys.SequenceEqual(GfOptions.Catalog.Options.Select(o => o.Key)), $"{api.Name}'s Welcome offers options [{string.Join(", ", keys)}]");
        return socket;
    }

    static bool Holds(GameInfo game, ManagementApi account, Guid team) => game.Participants.Any(p => p.AccountId == account.AccountId && p.TeamId == team);
    static Guid TeamId(GameInfo game, int ordinal) => game.Teams.Single(t => t.Ordinal == ordinal).Id;

    /// <summary>Under self-play one account holds every seat.</summary>
    bool SelfPlay => shape.Seats.Count > 1 && shape.Seats.Select(s => s.Account).Distinct().Count() == 1;

    /// <summary>The game server's embedded set, the one every new game must get.</summary>
    static string CurrentHash => ScenarioCompiler.CoreProperties.ContentHash;
}
