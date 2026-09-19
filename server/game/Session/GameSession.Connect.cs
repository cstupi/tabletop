using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    private const int EventsPerMessage = 500;

    /// <summary>The connect sequence (client shell → The connection and the projection), in one mailbox turn so no event slips between the log and live delivery.</summary>
    public Task ConnectAsync(ISessionClient client) => RunAsync(async () =>
    {
        await client.SendAsync(new Welcome(client.ParticipantId, Info));
        if (Draft is not null) await client.SendAsync(new BoardMessage(new Board(Draft, [])));
        if (Play is not null)
        {
            await client.SendAsync(new BoardMessage(Play.Board));
            var log = EventFilter.ForViewer(Play.Applier.Log, TeamOf(client.ParticipantId), new HashSet<Guid>());
            for (var i = 0; i < log.Count; i += EventsPerMessage) await client.SendAsync(new EventsMessage(log.Skip(i).Take(EventsPerMessage).ToList()));
            if (log.Count == 0) await client.SendAsync(new EventsMessage([]));
        }
        await AttachCoreAsync(client);
        await client.SendAsync(Rest());
    });
}
