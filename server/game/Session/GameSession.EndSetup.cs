using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Setup;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    /// <summary>Setup → playing. Off-slab terrain dropped, every picked army snapshotted as it stands, the status flipped, the draft thereby the snapshot, the log opened on it, the engine advanced.</summary>
    private async Task HandleEndSetupAsync(ISessionClient from, Guid editId)
    {
        if (Info.Status != GameStatus.Setup || Draft is null) { await RefuseEdit(from, editId, RefusalReason.WrongStatus, $"the game is {Info.Status}"); return; }
        if (!IsCreator(from)) { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "only the creator ends setup"); return; }
        var rows = _services.Rows; var ct = CancellationToken.None;

        var snapshot = DraftEditor.DropOffSlab(Draft);
        var armies = new Dictionary<Guid, ArmySnapshot?>();
        foreach (var p in Info.Participants)
        {
            if (p.ArmyId is not { } armyId) { armies[p.Id] = null; continue; }
            var owned = await rows.ReadArmyAsync(armyId, ct);
            armies[p.Id] = owned is null ? null : ArmySnapshot.Of(owned.Army, Guid.CreateVersion7());
        }
        await rows.EndSetupAsync(GameId, snapshot, armies, ct);

        Draft = null;
        Info = await rows.ReadGameAsync(GameId, ct) ?? throw new GameNotFoundException(GameId);
        var board = new Board(snapshot, armies.Where(kv => kv.Value is not null).Select(kv => new ArmyInPlay(kv.Key, kv.Value!)).ToList());
        Play = new GameState(Info, board, new EventApplier(board, Info), Rules);
        Engine = _services.Engines.Create(Rules, Info);
        Dispatcher = new CommandDispatcher(Engine);
        await BroadcastAsync(new GameInfoMessage(Info));
        await BroadcastAsync(new BoardMessage(board));
        await BroadcastAsync(new EventsMessage([]));
        await RunToRestAsync([], null, null);
        await BroadcastAsync(Rest());   // the log has opened and the scheduler's first events are out: the transition ends at rest
    }
}
