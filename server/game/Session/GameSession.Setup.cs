using Tabletop.GameServer.Setup;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    /// <summary>Apply in arrival order, broadcast the whole draft, then write the row. Nothing waits on the database (setup phase → The draft is the row).</summary>
    private async Task HandleSetupEditAsync(ISessionClient from, Guid editId, SetupEdit edit)
    {
        if (Info.Status != GameStatus.Setup || Draft is null) { await RefuseEdit(from, editId, RefusalReason.WrongStatus, $"the game is {Info.Status}"); return; }
        var next = DraftEditor.Apply(Draft, edit, out var missing);
        if (next is null) { await RefuseEdit(from, editId, RefusalReason.Unknown, missing!); return; }
        Draft = next;
        await BroadcastAsync(new BoardMessage(new Board(next, [])));
        try { await _services.Rows.WriteDraftAsync(GameId, next, CancellationToken.None); }
        catch (Exception ex) { _log.LogError(ex, "Game {GameId}: the draft row write failed", GameId); await FaultAsync("the draft could not be saved"); }
    }
}
