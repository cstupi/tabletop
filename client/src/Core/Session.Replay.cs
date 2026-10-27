using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// Replay (client design → Replay): the game opened read-only on `/ws?watch=1`, which the server holds as a watcher whatever the account,
/// and the table drawn at a position in the log (ReplayTimeline) rather than at its end. The live log keeps arriving and extends the
/// timeline; nothing is ever sent but the connect and the session's reauth.
/// </summary>
public sealed partial class Session
{
    bool _replay;
    int _resumeAt;

    /// <summary>The replay's timeline, from the board on; null outside a replay.</summary>
    public ReplayTimeline? Replay { get; private set; }
    /// <summary>This session opened its game as a replay (ReplayAsync), until it goes home.</summary>
    public bool Replaying => _replay && GameId is not null;

    /// <summary>Home's Replay: opens a game the account is in, ended or running, read-only, at its start.</summary>
    public Task ReplayAsync(Guid gameId) { _replay = true; _resumeAt = 0; return ConnectAsync(gameId, resync: false); }

    /// <summary>Moves the replay to <paramref name="position"/>. Animated and forward, the table plays the events as play does (glides,
    /// floating results, the camera follow, lasers); otherwise it redraws at the new position.</summary>
    public void SeekReplay(int position, bool animate = false)
    {
        if (Replay is not { } replay || position == replay.Position) return;
        var shots = new List<Shot>();
        var forward = replay.Seek(position, e => { if (animate && e.Payload is StepCompleted step) shots.AddRange(ShotLines.Of(replay.Projection, step)); });
        if (animate && forward is { Count: > 0 })
        {
            RaiseEvents(forward);
            if (shots.Count > 0) Fired?.Invoke(shots);
            return;
        }
        RaiseBoard();
        RaiseEvents([]);
    }

    Uri SocketUri() => _replay ? new UriBuilder(Settings.GameServerUrl) { Query = "watch=1" }.Uri : new Uri(Settings.GameServerUrl);

    void StartReplay(Board board, GameInfo game) => Replay = _replay ? new ReplayTimeline(board, game, Applier!.Log) : null;

    /// <summary>The live log grows; the replay stays where it is, except back to where a reconnect left it.</summary>
    void OnReplayEvents(IReadOnlyList<GameEvent> events)
    {
        try { Applier!.ApplyAll(events); }
        catch (ProjectionDesyncException desync) { OnDesync(desync); return; }
        if (Replay is { } replay && replay.Position < _resumeAt) replay.Seek(Math.Min(_resumeAt, replay.Count));
        if (events.Count == 0) CatchingUp = false;
        RaiseEvents([]);
    }
}
