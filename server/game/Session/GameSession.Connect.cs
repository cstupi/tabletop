using System.Net.WebSockets;
using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    private const int EventsPerMessage = 500;

    /// <summary>The connect sequence (client shell → The connection and the projection), in one mailbox turn so no event slips between the log and live delivery.</summary>
    public Task ConnectAsync(ISessionClient client) => RunAsync(async () =>
    {
        // Hydration read the participant rows once and management's join inserts them whenever it likes, so a participant this
        // session has never seen re-reads them first: Welcome carries their row (client shell → Welcome names the connecting
        // participant), TeamOf finds their team, and the lobby edits that look themselves up find them too.
        if (Info.Participants.All(p => p.Id != client.ParticipantId)) await ReloadInfoAsync();
        await SendStateAsync(client, client.ParticipantId, TeamsOf(client));
        await AttachCoreAsync(client);
        await client.SendAsync(Rest());
    });

    /// <summary>An admin watcher (admin watch spec): the frames a participant with no team gets, with no participant id, then held
    /// apart from the participants so nothing that reads a client's ParticipantId (lobby, presence, asks) meets it.</summary>
    public Task WatchAsync(ISessionClient client) => RunAsync(async () =>
    {
        if (Faulted) { await client.CloseAsync(WebSocketCloseStatus.InternalServerError, "this game session has faulted"); return; }
        await SendStateAsync(client, Guid.Empty, new HashSet<Guid>());
        _watchers.Add(client);
        foreach (var value in _presence.All) await client.SendAsync(new PresenceMessage(value));
        await client.SendAsync(Rest());
    });

    private async Task SendStateAsync(ISessionClient client, Guid participantId, IReadOnlySet<Guid> teams)
    {
        await client.SendAsync(new Welcome(participantId, Info) { Options = Rules.OptionCatalog });
        if (Draft is not null) await client.SendAsync(new BoardMessage(new Board(Draft, [])));
        if (Play is not null)
        {
            await client.SendAsync(new BoardMessage(Play.Board));
            var log = EventFilter.ForViewer(Play.Applier.Log, teams, new HashSet<Guid>());
            for (var i = 0; i < log.Count; i += EventsPerMessage) await client.SendAsync(new EventsMessage(log.Skip(i).Take(EventsPerMessage).ToList()));
            if (log.Count == 0) await client.SendAsync(new EventsMessage([]));
            await SendFactsAsync(client);
        }
    }
}
