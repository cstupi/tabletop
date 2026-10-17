using Tabletop.Content;
using Tabletop.Geometry;
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

    /// <summary>Written to the game's rows, broadcast as the new GameInfo, never logged. Accepted while lobby or setup; read-only from playing on.</summary>
    private async Task HandleLobbyEditAsync(ISessionClient from, Guid editId, LobbyEdit edit)
    {
        if (Info.Status is not (GameStatus.Lobby or GameStatus.Setup)) { await RefuseEdit(from, editId, RefusalReason.WrongStatus, $"the game is {Info.Status}"); return; }
        var rows = _services.Rows; var ct = CancellationToken.None;
        // The participant row can go (management's leave) while the socket is open: the edit is refused, not thrown.
        if (Info.Participants.FirstOrDefault(p => p.Id == from.ParticipantId) is not { } me) { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "not a participant of this game"); return; }
        var creator = IsCreator(from);
        switch (edit)
        {
            case CreateTeam: await rows.CreateTeamAsync(GameId, ct); break;
            case PickTeam t when t.TeamId is { } id && Info.Teams.All(x => x.Id != id): await RefuseEdit(from, editId, RefusalReason.Unknown, $"no team {id}"); return;
            case PickTeam { ParticipantId: { } whom } when whom != me.Id && !creator: await RefuseEdit(from, editId, RefusalReason.NotPermitted, "only the creator places another participant"); return;
            case PickTeam { ParticipantId: { } whom } when Info.Participants.All(p => p.Id != whom): await RefuseEdit(from, editId, RefusalReason.Unknown, $"no participant {whom}"); return;
            // Teams are shared by design (game flow: two players on a side); joining a side someone already holds takes the creator's approval.
            case PickTeam { TeamId: { } id } t when !creator && Info.Participants.Any(p => p.TeamId == id && p.Id != (t.ParticipantId ?? me.Id)):
                await RefuseEdit(from, editId, RefusalReason.NotPermitted, "another participant holds this team; the creator may place you on it"); return;
            case PickTeam t: await rows.SetParticipantTeamAsync(t.ParticipantId ?? me.Id, t.TeamId, ct); break;
            case PickArmy { ArmyId: { } armyId }:
                var owned = await rows.ReadArmyAsync(armyId, ct);
                if (owned is null) { await RefuseEdit(from, editId, RefusalReason.Unknown, $"no army {armyId}"); return; }
                if (owned.OwnerId != me.AccountId) { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "not your army"); return; }
                if (_services.Options.Army.Breach(owned.Army.Units) is { } unplayable) { await RefuseEdit(from, editId, RefusalReason.Illegal, $"this army cannot be played: {unplayable}"); return; }
                await rows.SetParticipantArmyAsync(me.Id, armyId, ct); break;
            case PickArmy: await rows.SetParticipantArmyAsync(me.Id, null, ct); break;
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
}
