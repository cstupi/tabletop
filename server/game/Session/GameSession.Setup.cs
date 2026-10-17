using Tabletop.Content;
using Tabletop.GameServer.Setup;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    /// <summary>Apply in arrival order, broadcast the whole draft, then write the row. Nothing waits on the database (setup phase → The draft is the row).</summary>
    private async Task HandleSetupEditAsync(ISessionClient from, Guid editId, SetupEdit edit)
    {
        if (Info.Status != GameStatus.Setup || Draft is null) { await RefuseEdit(from, editId, RefusalReason.WrongStatus, $"the game is {Info.Status}"); return; }
        if (await ForeignAssetAsync(edit) is { } foreign) { await RefuseEdit(from, editId, RefusalReason.NotPermitted, foreign); return; }
        var next = DraftEditor.Apply(Draft, edit, out var missing, _services.Options.Draft);
        if (next is null) { await RefuseEdit(from, editId, RefusalReason.Unknown, missing!); return; }
        Draft = next;
        await BroadcastAsync(new BoardMessage(new Board(next, [])));
        try { await _services.Rows.WriteDraftAsync(GameId, next, CancellationToken.None); }
        catch (Exception ex) { _log.LogError(ex, "Game {GameId}: the draft row write failed", GameId); await FaultAsync("the draft could not be saved"); }
    }

    /// <summary>An asset a setup edit references must exist and belong to a participant of this game: the table shows everyone's meshes,
    /// and an id from someone else's library would hand its blob to strangers (content-path review). Null when every reference is good.</summary>
    private async Task<string?> ForeignAssetAsync(SetupEdit edit)
    {
        IEnumerable<VisualRef> refs = edit switch
        {
            SetTexture t => [t.Texture],
            AddTerrain a => [a.Placement.Mesh],
            SwapTerrainMesh s => [s.Mesh],
            AddSetupMarker m => [m.Marker.Visual],
            ReplaceDraft r => [r.Draft.Texture, .. r.Draft.Terrain.Select(t => t.Mesh), .. r.Draft.Markers.Select(m => m.Visual)],
            _ => [],
        };
        var ids = refs.Select(v => v.AssetId).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return null;
        var owners = await _services.Rows.AssetOwnersAsync(ids, CancellationToken.None);
        var accounts = Info.Participants.Select(p => p.AccountId).ToHashSet();
        return ids.FirstOrDefault(id => !owners.TryGetValue(id, out var owner) || !accounts.Contains(owner)) is var bad && bad != Guid.Empty
            ? $"asset {bad} is not in a participant's library" : null;
    }
}
