using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    /// <summary>Every inbound frame after Connect lands here, on the mailbox. A faulted session is done: every frame kind, including presence and ping, is dropped.
    /// So is a frame from a connection the session no longer holds (replaced, or over its account's socket cap).</summary>
    public Task ReceiveAsync(ISessionClient from, SocketMessage frame) => RunAsync(() => Faulted || !(_clients.Contains(from) || _watchers.Contains(from)) ? Task.CompletedTask
        : Watches(from) ? HandleWatcherFrameAsync(from, frame) : frame switch
    {
        CommandMessage c => HandleCommandAsync(from, c),
        PresenceMessage p => HandlePresence(from, p.Presence),
        PingMessage p when !PresenceLimits.Holds(p.Ping) => Task.CompletedTask,
        PingMessage p => BroadcastAsync(new PingMessage(p.Ping with { Sender = from.ParticipantId }), except: from),
        EditMessage e => HandleEditAsync(from, e),
        SightQuery q => HandleSightAsync(from, q),
        _ => from.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.ProtocolError, NotAClientFrame).AsTask(),
    });

    private const string NotAClientFrame = "not a client frame";

    /// <summary>An admin watcher, or from playing on a participant with no team (game flow: that is what a spectator is). Before playing, and
    /// while deployment waits for players, a teamless participant is choosing a team and may edit and point.</summary>
    private bool Watches(ISessionClient from) => _watchers.Contains(from) || (Info.Status is GameStatus.Playing or GameStatus.Ended && TeamsOf(from).Count == 0
        && !(Play is not null && WaitingForPlayers.Holds(Info, Play.Board, Play.Projection.LastSeq)));

    /// <summary>A watcher, admin or teamless participant from playing on, changes nothing (admin watch spec; security review H3): a sight query is answered; a command (answers and undo included), an edit,
    /// presence or a ping is refused with reason watcher and nothing else follows, no rest frame included.</summary>
    private Task HandleWatcherFrameAsync(ISessionClient from, SocketMessage frame)
    {
        const string Detail = "a watcher sends nothing that changes the game";
        return frame switch
        {
            SightQuery q => HandleSightAsync(from, q),
            CommandMessage c => from.SendAsync(new Protocol.Refused(c.CommandId, RefusalReason.Watcher, Detail)).AsTask(),
            EditMessage e => from.SendAsync(new Protocol.Refused(e.EditId, RefusalReason.Watcher, Detail)).AsTask(),
            PresenceMessage or PingMessage => from.SendAsync(new Protocol.Refused(Guid.Empty, RefusalReason.Watcher, Detail)).AsTask(),
            _ => from.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.ProtocolError, NotAClientFrame).AsTask(),
        };
    }

    /// <summary>Every command ends at rest. The frame follows its batch: broadcast when the batch appended something, to the sender alone when it did not.</summary>
    private async Task HandleCommandAsync(ISessionClient from, CommandMessage message)
    {
        var before = Play?.Projection.LastSeq ?? 0;
        if (Play is null || Dispatcher is null || Info.Status is not (GameStatus.Playing or GameStatus.Ended))
            await from.SendAsync(new Protocol.Refused(message.CommandId, RefusalReason.WrongStatus, $"commands are not accepted while the game is {Info.Status}"));
        else if (Play.Projection.LastSeq >= _services.Options.MaxEventsPerGame)
            await from.SendAsync(new Protocol.Refused(message.CommandId, RefusalReason.NotPermitted, "this game's log is full"));
        else if (WaitingForPlayers.Holds(Info, Play.Board, Play.Projection.LastSeq))
            await from.SendAsync(new Protocol.Refused(message.CommandId, RefusalReason.WrongStatus,
                $"deployment has not begun: {string.Join("; ", WaitingForPlayers.Teams(Info, Play.Board).Select(w => w.Text))}"));
        else if (ActorFor(from, message.Command) is var actor)
            switch (Dispatcher.Dispatch(Play, message.Command, actor))
            {
                case Dispatch.Refused r: await from.SendAsync(new Protocol.Refused(message.CommandId, r.Reason, r.Detail)); break;
                case Applied a:
                    if (!await RunToRestAsync(a.Events, actor.ParticipantId, a.Warning))
                        await from.SendAsync(new Protocol.Refused(message.CommandId, RefusalReason.Unknown, "the rules engine failed on this command; nothing was applied"));
                    break;
            }
        if (Faulted) return;
        var rest = Rest();
        if (rest.Seq > before) await BroadcastAsync(rest); else await from.SendAsync(rest);
    }

    /// <summary>Presence is state: the latest value wins and the tick relays it. The server stamps the participant id; a client cannot speak for another.</summary>
    private Task HandlePresence(ISessionClient from, Presence value)
    {
        if (!PresenceLimits.Holds(value)) return Task.CompletedTask;   // dropped: the next value supersedes it
        _presence.Set(value with { ParticipantId = from.ParticipantId });
        return Task.CompletedTask;
    }

    /// <summary>Called at PresenceRelayHz by SessionHost. Relays each changed value to everyone but its owner. A faulted session has no one to relay to.</summary>
    public Task TickPresenceAsync() => RunAsync(async () =>
    {
        if (Faulted) return;
        foreach (var value in _presence.TakeDirty())
        {
            foreach (var c in _clients.ToList()) if (c.ParticipantId != value.ParticipantId) await c.SendAsync(new PresenceMessage(value));
            foreach (var w in _watchers.ToList()) await w.SendAsync(new PresenceMessage(value));
        }
    });
}
