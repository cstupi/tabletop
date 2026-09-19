using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    /// <summary>Every inbound frame after Connect lands here, on the mailbox. A faulted session is done: every frame kind, including presence and ping, is dropped.</summary>
    public Task ReceiveAsync(ISessionClient from, SocketMessage frame) => RunAsync(() => Faulted ? Task.CompletedTask : frame switch
    {
        CommandMessage c => HandleCommandAsync(from, c),
        PresenceMessage p => HandlePresence(from, p.Presence),
        PingMessage p => BroadcastAsync(new PingMessage(p.Ping with { Sender = from.ParticipantId }), except: from),
        EditMessage e => HandleEditAsync(from, e),
        _ => from.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.ProtocolError, $"{frame.GetType().Name} is not a client frame").AsTask(),
    });

    /// <summary>Every command ends at rest. The frame follows its batch: broadcast when the batch appended something, to the sender alone when it did not.</summary>
    private async Task HandleCommandAsync(ISessionClient from, CommandMessage message)
    {
        var before = Play?.Projection.LastSeq ?? 0;
        if (Play is null || Dispatcher is null || Info.Status is not (GameStatus.Playing or GameStatus.Ended))
            await from.SendAsync(new Protocol.Refused(message.CommandId, RefusalReason.WrongStatus, $"commands are not accepted while the game is {Info.Status}"));
        else
            switch (Dispatcher.Dispatch(Play, message.Command, ActorOf(from)))
            {
                case Dispatch.Refused r: await from.SendAsync(new Protocol.Refused(message.CommandId, r.Reason, r.Detail)); break;
                case Applied a: await AppendAsync(a.Events, from.ParticipantId, a.Warning); await AdvanceAsync(); break;
            }
        var rest = Rest();
        if (rest.Seq > before) await BroadcastAsync(rest); else await from.SendAsync(rest);
    }

    /// <summary>Presence is state: the latest value wins and the tick relays it. The server stamps the participant id; a client cannot speak for another.</summary>
    private Task HandlePresence(ISessionClient from, Presence value)
    {
        _presence.Set(value with { ParticipantId = from.ParticipantId });
        return Task.CompletedTask;
    }

    /// <summary>Called at PresenceRelayHz by SessionHost. Relays each changed value to everyone but its owner. A faulted session has no one to relay to.</summary>
    public Task TickPresenceAsync() => RunAsync(async () =>
    {
        if (Faulted) return;
        foreach (var value in _presence.TakeDirty())
            foreach (var c in _clients.ToList()) if (c.ParticipantId != value.ParticipantId) await c.SendAsync(new PresenceMessage(value));
    });

    // Task 10 (lobby edits) and Task 11 (setup edits) replace this body.
    private Task HandleEditAsync(ISessionClient from, EditMessage message) =>
        from.SendAsync(new Protocol.Refused(message.EditId, RefusalReason.WrongStatus, "edits are not handled yet")).AsTask();
}
