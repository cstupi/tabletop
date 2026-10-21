using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    private Task HandleEditAsync(ISessionClient from, EditMessage message) => message.Edit switch
    {
        LobbyEdit l => HandleLobbyEditAsync(from, message.EditId, l),
        SetupEdit s => HandleSetupEditAsync(from, message.EditId, s),
        _ => from.SendAsync(new Protocol.Refused(message.EditId, RefusalReason.Unknown, "unknown edit")).AsTask(),
    };

    private Task RefuseEdit(ISessionClient to, Guid editId, RefusalReason reason, string detail) => to.SendAsync(new Protocol.Refused(editId, reason, detail)).AsTask();
    private bool IsCreator(ISessionClient client) => Info.Participants.FirstOrDefault(p => p.Id == client.ParticipantId)?.AccountId == Info.CreatedBy;

    /// <summary>The default draft the creator's StartSetup creates. Size and texture come from configuration (ruling 2); the zones are the
    /// core rules' deployment (p.6: one long table edge each, fully within 12"), ZoneStyles' Frontline, until setup sets another style.</summary>
    internal TableState NewDraft() => new(Guid.CreateVersion7(), null, new TableSize(_services.Options.DefaultTableWidth, _services.Options.DefaultTableLength),
        VisualRef.OfBuiltin(_services.Options.DefaultTextureBuiltin), [], [],
        ZoneStyles.ShapesOf(ZoneStyles.Frontline).Select((shape, i) => new DeploymentZone(Guid.CreateVersion7(), shape, i + 1)).ToList());

    internal async Task ReloadInfoAsync()
    {
        Info = await _services.Rows.ReadGameAsync(GameId, CancellationToken.None) ?? throw new GameNotFoundException(GameId);
        if (Play is not null) Play.Info = Info;
        await BroadcastAsync(new GameInfoMessage(Info));
    }

    /// <summary>
    /// The rows read again, broadcast only when they differ from Info: management's join and leave write participant rows without telling
    /// this session. A game whose row is gone (deleted) changes nothing here.
    /// </summary>
    internal async Task RefreshInfoAsync()
    {
        if (await _services.Rows.ReadGameAsync(GameId, CancellationToken.None) is not { } fresh) return;
        if (WireJson.Serialize(fresh) == WireJson.Serialize(Info)) return;
        Info = fresh;
        if (Play is not null) Play.Info = Info;
        await BroadcastAsync(new GameInfoMessage(Info));
    }

    /// <summary>
    /// Written to the game's rows, broadcast as the new GameInfo, never logged. Accepted while lobby or setup; from playing on, only taking an
    /// empty team and bringing an army while deployment waits for players (HandleWaitingEditAsync); read-only otherwise.
    /// </summary>
    private async Task HandleLobbyEditAsync(ISessionClient from, Guid editId, LobbyEdit edit)
    {
        if (Info.Status == GameStatus.Playing && Play is not null && WaitingForPlayers.Holds(Info, Play.Board, Play.Projection.LastSeq))
        { await HandleWaitingEditAsync(from, editId, edit); return; }
        if (Info.Status is not (GameStatus.Lobby or GameStatus.Setup)) { await RefuseEdit(from, editId, RefusalReason.WrongStatus, $"the game is {Info.Status}"); return; }
        var rows = _services.Rows; var ct = CancellationToken.None;
        // The participant row can go (management's leave) while the socket is open: the edit is refused, not thrown.
        if (Info.Participants.FirstOrDefault(p => p.Id == from.ParticipantId) is not { } me) { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "not a participant of this game"); return; }
        var creator = IsCreator(from);
        var seats = SeatsOf(from);
        switch (edit)
        {
            case CreateTeam: await rows.CreateTeamAsync(GameId, ct); break;
            case PickTeam t when t.TeamId is { } id && Info.Teams.All(x => x.Id != id): await RefuseEdit(from, editId, RefusalReason.Unknown, $"no team {id}"); return;
            case PickTeam { ParticipantId: { } whom } when seats.All(s => s.Id != whom) && !creator: await RefuseEdit(from, editId, RefusalReason.NotPermitted, "only the creator places another participant"); return;
            case PickTeam { ParticipantId: { } whom } when Info.Participants.All(p => p.Id != whom): await RefuseEdit(from, editId, RefusalReason.Unknown, $"no participant {whom}"); return;
            case PickTeam t:
            {
                var placed = t.ParticipantId is { } whom ? Info.Participants.First(p => p.Id == whom) : me;
                // Teams are shared by design (game flow: two players on a side); joining a side another account holds takes the creator's approval.
                if (t.TeamId is { } id && !creator && Info.Participants.Any(p => p.TeamId == id && p.AccountId != placed.AccountId))
                { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "another participant holds this team; the creator may place you on it"); return; }
                if (t.ParticipantId is null) { await TakeTeamAsync(me, t.TeamId); break; }
                if (t.TeamId is { } target && Info.Participants.Any(p => p.AccountId == placed.AccountId && p.TeamId == target && p.Id != placed.Id))
                { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "that participant already holds this team"); return; }
                if (t.TeamId is null) await DropSeatAsync(placed); else await rows.SetParticipantTeamAsync(placed.Id, t.TeamId, ct);
                break;
            }
            case LeaveTeam l when seats.All(s => s.TeamId != l.TeamId): await RefuseEdit(from, editId, RefusalReason.Unknown, $"you do not hold team {l.TeamId}"); return;
            case LeaveTeam l: await DropSeatAsync(seats.First(s => s.TeamId == l.TeamId)); break;
            case PickArmy a when SeatFor(seats, a.TeamId) is null: await RefuseEdit(from, editId, RefusalReason.Unknown, $"you do not hold team {a.TeamId}"); return;
            case PickArmy { ArmyId: { } armyId } a:
                if (await PlayableArmyAsync(from, editId, me, armyId) is null) return;
                await rows.SetParticipantArmyAsync(SeatFor(seats, a.TeamId)!.Id, armyId, ct); break;
            case PickArmy a: await rows.SetParticipantArmyAsync(SeatFor(seats, a.TeamId)!.Id, null, ct); break;
            case SetConfiguration when !creator: await RefuseEdit(from, editId, RefusalReason.NotPermitted, "only the creator configures the game"); return;
            case SetConfiguration c when !await rows.PropertiesExistAsync(c.PropertiesHash, ct): await RefuseEdit(from, editId, RefusalReason.Unknown, $"no properties {c.PropertiesHash}"); return;
            case SetConfiguration c when Rules.OptionProblems(c.OptionalRules) is { Count: > 0 } problems: await RefuseEdit(from, editId, RefusalReason.Unknown, string.Join("; ", problems)); return;
            case SetConfiguration c: await rows.SetConfigurationAsync(GameId, c, ct); break;
            case StartSetup when !IsCreator(from): await RefuseEdit(from, editId, RefusalReason.NotPermitted, "only the creator starts setup"); return;
            case StartSetup when Info.Status != GameStatus.Lobby: await RefuseEdit(from, editId, RefusalReason.WrongStatus, "setup has already started"); return;
            case StartSetup:
                var draft = NewDraft();
                await rows.StartSetupAsync(GameId, draft, ct);
                Draft = draft;
                await ReloadInfoAsync();
                await BroadcastAsync(new BoardMessage(new Board(draft, [])));
                return;
            case EndSetup: await HandleEndSetupAsync(from, editId); return;
        }
        await ReloadInfoAsync();
    }

    /// <summary>The seat an army goes to: the account's row on teamId; with no team named, its first row (the oldest, the connection's).</summary>
    private static Participant? SeatFor(IReadOnlyList<Participant> seats, Guid? teamId) =>
        teamId is { } t ? seats.FirstOrDefault(s => s.TeamId == t) : seats.FirstOrDefault();

    /// <summary>The army, if it is the sender's and playable; otherwise the refusal is sent and null returned.</summary>
    private async Task<OwnedArmyCheck?> PlayableArmyAsync(ISessionClient from, Guid editId, Participant me, Guid armyId)
    {
        var owned = await _services.Rows.ReadArmyAsync(armyId, CancellationToken.None);
        if (owned is null) { await RefuseEdit(from, editId, RefusalReason.Unknown, $"no army {armyId}"); return null; }
        if (owned.OwnerId != me.AccountId) { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "not your army"); return null; }
        if (_services.Options.Army.Breach(owned.Army.Units) is { } unplayable) { await RefuseEdit(from, editId, RefusalReason.Illegal, $"this army cannot be played: {unplayable}"); return null; }
        return new OwnedArmyCheck(owned.Army);
    }
    private sealed record OwnedArmyCheck(Army Army);

    /// <summary>The account takes one more team: its teamless row takes it, else a new row does (self-play); a team it already holds changes nothing.
    /// Null gives up every team: the oldest row stays, teamless, and every other row goes.</summary>
    private async Task TakeTeamAsync(Participant me, Guid? teamId)
    {
        var rows = _services.Rows; var ct = CancellationToken.None;
        var mine = Info.Participants.Where(p => p.AccountId == me.AccountId).ToList();
        if (teamId is null)
        {
            foreach (var other in mine.Skip(1)) await rows.RemoveSeatAsync(other.Id, ct);
            await rows.SetParticipantTeamAsync(mine[0].Id, null, ct);
            return;
        }
        if (mine.Any(p => p.TeamId == teamId)) return;
        if (mine.FirstOrDefault(p => p.TeamId is null) is { } teamless) await rows.SetParticipantTeamAsync(teamless.Id, teamId, ct);
        else await rows.AddSeatAsync(SeatIdAfter(mine.Max(p => p.Id)), GameId, me.AccountId, teamId.Value, ct);
    }

    /// <summary>A new seat's id sorts after every row the account holds, so its first row stays the oldest: two v7 ids minted in one millisecond
    /// order by their random bits.</summary>
    public static Guid SeatIdAfter(Guid latest)
    {
        var id = Guid.CreateVersion7();
        while (id.CompareTo(latest) <= 0) id = Guid.CreateVersion7();
        return id;
    }

    /// <summary>
    /// One seat given up. The account's only row stays, teamless (a watcher's row); any other row is deleted, except the oldest, which the account's
    /// sockets carry: it takes the next row's team and army instead, and that row goes.
    /// </summary>
    private async Task DropSeatAsync(Participant seat)
    {
        var rows = _services.Rows; var ct = CancellationToken.None;
        var mine = Info.Participants.Where(p => p.AccountId == seat.AccountId).ToList();
        if (mine.Count == 1) { await rows.SetParticipantTeamAsync(seat.Id, null, ct); return; }
        if (seat.Id != mine[0].Id) { await rows.RemoveSeatAsync(seat.Id, ct); return; }
        var next = mine[1];
        await rows.RemoveSeatAsync(next.Id, ct);
        await rows.SetParticipantTeamAsync(seat.Id, next.TeamId, ct);
        await rows.SetParticipantArmyAsync(seat.Id, next.ArmyId, ct);
    }

    /// <summary>
    /// Deployment waits for players (game-flow spec → Waiting for players): setup has ended, the log is empty, and some team has no participant or no army.
    /// Two edits stay open. PickTeam onto a team no one holds: any participant takes it for itself (a new row when it already plays a team), or the
    /// creator places a teamless participant on it. PickArmy for a seat whose team has no army: the army is snapshotted and joins the board the log
    /// opens on, and deployment begins once no team waits. Everything else is frozen.
    /// </summary>
    private async Task HandleWaitingEditAsync(ISessionClient from, Guid editId, LobbyEdit edit)
    {
        var rows = _services.Rows; var ct = CancellationToken.None;
        if (Info.Participants.FirstOrDefault(p => p.Id == from.ParticipantId) is not { } me) { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "not a participant of this game"); return; }
        var waiting = WaitingForPlayers.Teams(Info, Play!.Board);
        var seats = SeatsOf(from);
        const string Frozen = "the game is playing: only an empty team and its army may be taken while deployment waits for players";
        switch (edit)
        {
            case PickTeam { TeamId: { } id } t when waiting.FirstOrDefault(w => w.TeamId == id) is { HasPlayer: false }:
                if (t.ParticipantId is null) { await TakeTeamAsync(me, id); break; }
                if (seats.All(s => s.Id != t.ParticipantId) && !IsCreator(from)) { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "only the creator places another participant"); return; }
                if (Info.Participants.FirstOrDefault(p => p.Id == t.ParticipantId) is not { TeamId: null } placed)
                { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "only a participant with no team is placed while deployment waits"); return; }
                await rows.SetParticipantTeamAsync(placed.Id, id, ct);
                break;
            case PickArmy { ArmyId: { } armyId } a when SeatFor(seats.Where(s => s.TeamId is not null).ToList(), a.TeamId) is { } seat
                && waiting.Any(w => w.TeamId == seat.TeamId) && Play.Board.Armies.All(x => x.ParticipantId != seat.Id):
                if (await PlayableArmyAsync(from, editId, me, armyId) is not { } army) return;
                await JoinArmyAsync(seat, armyId, army.Army);
                return;
            default: await RefuseEdit(from, editId, RefusalReason.WrongStatus, Frozen); return;
        }
        await ReloadInfoAsync();
    }

    /// <summary>The army joins the board the empty log opens on, as EndSetup would have snapshotted it; then the engine runs, which starts the
    /// roll-off once no team waits. The armies keep participant-id order, as hydration reads them, so a rehydrated board is the same board.</summary>
    private async Task JoinArmyAsync(Participant seat, Guid armyId, Army army)
    {
        var snapshot = ArmySnapshot.Of(army, Guid.CreateVersion7());
        await _services.Rows.JoinArmyAsync(GameId, seat.Id, armyId, snapshot, CancellationToken.None);
        Info = await _services.Rows.ReadGameAsync(GameId, CancellationToken.None) ?? throw new GameNotFoundException(GameId);
        var board = Play!.Board with { Armies = Play.Board.Armies.Append(new ArmyInPlay(seat.Id, snapshot)).OrderBy(a => a.ParticipantId).ToList() };
        Play = new GameState(Info, board, new EventApplier(board, Info), Rules);
        await BroadcastAsync(new GameInfoMessage(Info));
        await BroadcastAsync(new BoardMessage(board));
        await BroadcastAsync(new EventsMessage([]));
        await RunToRestAsync([], null, null);
        await BroadcastFactsIfChangedAsync();
        await BroadcastAsync(Rest());
    }
}
