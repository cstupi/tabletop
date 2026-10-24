using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Lifecycle;

public sealed partial class LifecycleGame
{
    /// <summary>The army-book rules and a spell every current set holds (lifecycle suite spec → The set): name and kind.</summary>
    static readonly (string Name, string Kind)[] SetHolds = [("Fanatic", "model"), ("Ambush", "unit"), ("Death Bolt", "spell")];

    /// <summary>The client's create dialog: the first set of the game rules, then the create; the game is in the lobby on that set.</summary>
    async Task CreateAsync(CancellationToken ct)
    {
        _step = "properties";
        var sets = await Creator.PropertiesAsync(ct);
        var first = sets.FirstOrDefault(s => s.GameRules == "grimdark-future");
        Check(first is not null, "GET /v1/properties lists no grimdark-future set");
        Check(first!.ContentHash == CurrentHash, $"GET /v1/properties lists {first.ContentHash} ({first.Name}) first, not the game server's set {CurrentHash}");
        _hash = first.ContentHash;
        var rules = await Creator.RulesAsync(_hash, ct);
        foreach (var (entry, kind) in SetHolds)
            Check(rules.Any(r => r.Name == entry && r.Kind == kind && r.Played), $"set {_hash} holds no played {kind} entry {entry}");

        _step = "create";
        var game = await Creator.CreateGameAsync(new CreateGameRequest(name, _hash, SchedulerShape.AlternatingActivation, shape.Enforcement, false), ct);
        GameId = game.Id;
        Check(game.Status == GameStatus.Lobby && game.PropertiesHash == _hash && game.CreatedBy == Creator.AccountId,
            $"the created game is {game.Status} on {game.PropertiesHash}, created by {game.CreatedBy}");
        Check(game.Participants is [{ TeamId: null } only] && only.AccountId == Creator.AccountId, "the created game's one participant is not the creator, teamless");
        await StatusAsync(GameStatus.Lobby, [Creator], ct);
    }

    /// <summary>Joins over HTTP, sockets, teams, configuration, seats and armies: the lobby panel's buttons in the order a table uses them.</summary>
    async Task LobbyAsync(CancellationToken ct)
    {
        _step = "join";
        var joined = await _spectator.JoinAsync(GameId!.Value, ct);
        Check(joined.Participants.Any(p => p.AccountId == _spectator.AccountId && p.TeamId is null), "the spectator's join holds no teamless row of its own");
        foreach (var account in shape.Seats.Select(s => s.Account).Distinct().Where(a => a != 0))
        {
            var game = await _players[account].JoinAsync(GameId.Value, ct);
            Check(game.Status == GameStatus.Lobby && game.Participants.Any(p => p.AccountId == _players[account].AccountId),
                $"{_players[account].Name}'s join answered {game.Status} without its row");
        }
        await StatusAsync(GameStatus.Lobby, Participants(), ct);

        _step = "connect";
        await ConnectAsync(0, ct);
        foreach (var account in shape.Seats.Select(s => s.Account).Distinct().Where(a => a != 0)) await ConnectAsync(account, ct);

        _step = "teams";
        for (var n = 1; n <= shape.Teams; n++)
        {
            var count = n;
            await CreatorSocket.EditAsync(new CreateTeam(), g => g.Teams.Count == count, ct);
        }
        Check(CreatorSocket.Game.Teams.Select(t => t.Ordinal).SequenceEqual(Enumerable.Range(1, shape.Teams)), "the teams' ordinals are not 1..n");

        _step = "configuration";
        var bad = await CreatorSocket.RefusedEditAsync(Configuration(new Dictionary<string, string> { ["melee_separation"] = "sideways" }), ct);
        Check(bad.Reason == RefusalReason.Unknown, $"an unknown option value was refused {bad.Reason}, not Unknown");
        var options = new Dictionary<string, string> { ["unit_sight_blocking"] = "per_model" };
        var configured = await CreatorSocket.EditAsync(Configuration(options), g => g.OptionalRules.GetValueOrDefault("unit_sight_blocking") == "per_model", ct);
        Check(configured.Name == name && configured.PropertiesHash == _hash && configured.Enforcement == shape.Enforcement,
            $"the configuration reads {configured.Name} on {configured.PropertiesHash} under {configured.Enforcement}");

        _step = "seats";
        foreach (var seat in shape.Seats) await TakeSeatAsync(seat, ct);

        _step = "armies";
        foreach (var seat in shape.Seats) await BringArmyAsync(seat, _sockets[seat.Account], ct);
        await StatusAsync(GameStatus.Lobby, Participants(), ct);
    }

    SetConfiguration Configuration(IReadOnlyDictionary<string, string> options) =>
        new(name, _hash, SchedulerShape.AlternatingActivation, shape.Enforcement, false, options);

    /// <summary>An empty team the account takes itself; a team another account holds the creator places it on (PickTeam with its row).</summary>
    async Task TakeSeatAsync(Seat seat, CancellationToken ct)
    {
        var account = _players[seat.Account];
        var game = CreatorSocket.Game;
        var team = TeamId(game, seat.Team);
        if (game.Participants.Any(p => p.TeamId == team && p.AccountId != account.AccountId))
        {
            var row = game.Participants.Single(p => p.AccountId == account.AccountId && p.TeamId is null);
            await CreatorSocket.EditAsync(new PickTeam(team, row.Id), g => Holds(g, account, team), ct);
        }
        else await _sockets[seat.Account].EditAsync(new PickTeam(team), g => Holds(g, account, team), ct);
    }

    /// <summary>The army imported over HTTP (the dev route's body, no Army Forge call) and picked for the seat's team.</summary>
    async Task BringArmyAsync(Seat seat, ClientSocket socket, CancellationToken ct)
    {
        var account = _players[seat.Account];
        var ambush = shape.Ambush && seat.Account == 0;
        var army = await account.ImportArmyAsync(LifecycleArmy.List(seat.Book, ambush), ct);
        _armies.Add((account, army.Id));
        Check(army.Units.Count == 3 && army.Source == LifecycleArmy.Source, $"the import holds {army.Units.Count} units from {army.Source}");
        if (ambush)
            Check(army.Units.Single(u => u.Name == LifecycleArmy.AmbushUnit).Properties.Any(p => p.Name == "Ambush"), "the imported Beast Riders do not carry Ambush");
        var team = TeamId(CreatorSocket.Game, seat.Team);
        await socket.EditAsync(new PickArmy(army.Id, team),
            g => g.Participants.Any(p => p.AccountId == account.AccountId && p.TeamId == team && p.ArmyId == army.Id), ct);
    }

    /// <summary>StartSetup, terrain and markers from two sockets, then EndSetup: the log opens, or waits for players when a team is empty.</summary>
    async Task SetupAsync(CancellationToken ct)
    {
        _step = "start setup";
        await CreatorSocket.EditAsync(new StartSetup(), g => g.Status == GameStatus.Setup, ct);
        await CreatorSocket.UntilAsync(() => CreatorSocket.Board is not null, "the setup draft", ct);
        await StatusAsync(GameStatus.Setup, Participants(), ct);

        _step = "setup edits";
        var size = CreatorSocket.Board!.Table.Size;
        var forest = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("forest"), new Point3(size.Width / 3, size.Length / 2, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Circle(3), new Point2(0, 0), new Yaw(0), 0, 2,
                TerrainDeclarations.Difficult | TerrainDeclarations.Cover, null))]);
        await CreatorSocket.EditAsync(new AddTerrain(forest), b => b.Table.Terrain.Any(t => t.Id == forest.Id), ct);
        var moved = new Point3(size.Width * 2 / 3, size.Length / 2, 0);
        await CreatorSocket.EditAsync(new MoveTerrain(forest.Id, moved, new Yaw(45)), b => b.Table.Terrain.Any(t => t.Id == forest.Id && t.Position == moved), ct);
        var other = _sockets.Keys.Max() is var last && last != 0 ? _sockets[last] : CreatorSocket;
        var markers = new[] { new Point3(size.Width / 2, size.Length / 2, 0), new Point3(size.Width / 4, size.Length / 2, 0) }
            .Select((at, i) => new Marker(Guid.CreateVersion7(), MarkerKind.Objective, at, new Yaw(0), 2, $"Objective {i + 1}", VisualRef.OfBuiltin("objective"))).ToList();
        await other.EditAsync(new AddSetupMarker(markers[0]), b => b.Table.Markers.Any(m => m.Id == markers[0].Id), ct);
        await CreatorSocket.EditAsync(new AddSetupMarker(markers[1]), b => b.Table.Markers.Any(m => m.Id == markers[1].Id), ct);

        _step = "end setup";
        CreatorSocket.ExpectRest();
        await CreatorSocket.EditAsync(new EndSetup(), g => g.Status == GameStatus.Playing, ct);
        await CreatorSocket.UntilAsync(() => CreatorSocket.Rest >= 0, "the rest after EndSetup", ct);
        var board = CreatorSocket.Board!;
        Check(board.Armies.Count == shape.Seats.Count, $"the board holds {board.Armies.Count} armies, not {shape.Seats.Count}");
        Check(board.Table.Terrain.SingleOrDefault(t => t.Id == forest.Id)?.Position == moved, "the board's forest is not where it was moved");
        Check(markers.All(m => board.Table.Markers.Any(x => x.Id == m.Id)), "the board lacks a marker setup placed");
        if (shape.Late is null) Check(CreatorSocket.Rest > 0, "the log did not open at EndSetup");
        else Check(CreatorSocket.Rest == 0 && CreatorSocket.Events.Count == 0, $"the log opened at seq {CreatorSocket.Rest} with team {shape.Late.Team} empty");
        await StatusAsync(GameStatus.Playing, Participants(), ct);
    }

    /// <summary>Waiting for players: the late account joins over HTTP, connects, takes the empty team and brings an army, and the log opens.</summary>
    async Task LateJoinAsync(Seat late, CancellationToken ct)
    {
        _step = "late join";
        var account = _players[late.Account];
        var game = await account.JoinAsync(GameId!.Value, ct);
        Check(game.Status == GameStatus.Playing, $"the late join answered {game.Status}");
        var socket = await ConnectAsync(late.Account, ct);
        Check(socket.Welcome.Game.Status == GameStatus.Playing && socket.Rest == 0, $"the late joiner connected to a {socket.Welcome.Game.Status} game at seq {socket.Rest}");
        var team = TeamId(CreatorSocket.Game, late.Team);
        await socket.EditAsync(new PickTeam(team), g => Holds(g, account, team), ct);
        socket.ExpectRest();
        CreatorSocket.ExpectRest();
        await BringArmyAsync(late, socket, ct);
        await socket.UntilAsync(() => socket.Rest > 0, "the log to open after the late army", ct);
        await CreatorSocket.UntilAsync(() => CreatorSocket.Rest > 0, "the creator's rest after the late army", ct);
        Check(CreatorSocket.Board!.Armies.Count == shape.Seats.Count + 1, $"the board holds {CreatorSocket.Board.Armies.Count} armies after the late army");
        await StatusAsync(GameStatus.Playing, Participants(late: true), ct);
    }
}
