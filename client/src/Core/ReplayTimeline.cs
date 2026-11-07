using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>What a replay steps by: one player input and what the rules did with it, one action, one turn, one round.</summary>
public enum ReplayUnit { Step, Action, Turn, Round }

/// <summary>
/// A replay of a game's log (client design → Replay). Position counts the log's events applied, 0 (the board as setup left it) to
/// Count (the whole log); the projection is the log's first Position events as they happened, undos included. The source is the
/// live log, which a running game's events keep extending. Forward applies the next events; backward rebuilds from the start.
/// </summary>
public sealed class ReplayTimeline(Board board, GameInfo game, IReadOnlyList<GameEvent> source)
{
    EventApplier _at = new(board, game);

    public int Count => source.Count;
    /// <summary>The whole log the replay runs over (the live log).</summary>
    public IReadOnlyList<GameEvent> Source => source;
    public int Position => _at.Log.Count;
    public bool AtEnd => Position >= Count;
    public Projection Projection => _at.Projection;
    /// <summary>The events shown: the log's first Position events. The combat log is built over it.</summary>
    public IReadOnlyList<GameEvent> Shown => _at.Log;

    /// <summary>Moves to <paramref name="position"/>, clamped to the log. Forward: the events applied, in order, each passed to
    /// <paramref name="applied"/> once applied. Backward: null, the projection rebuilt from the board.</summary>
    public IReadOnlyList<GameEvent>? Seek(int position, Action<GameEvent>? applied = null)
    {
        position = Math.Clamp(position, 0, Count);
        if (position < Position)
        {
            _at = new EventApplier(board, game);
            for (var i = 0; i < position; i++) _at.Apply(source[i]);
            return null;
        }
        var events = new List<GameEvent>(position - Position);
        for (var i = Position; i < position; i++) { _at.Apply(source[i]); events.Add(source[i]); applied?.Invoke(source[i]); }
        return events;
    }

    public int Next(ReplayUnit unit) => Stops(source, unit).FirstOrDefault(s => s > Position, Count);
    public int Previous(ReplayUnit unit) => Stops(source, unit).LastOrDefault(s => s < Position, 0);

    /// <summary>"Round 2 · 140 of 2210": the round the position is in (none before the first) and the position.</summary>
    public string Caption => (Projection.Scheduler.Round > 0 ? $"Round {Projection.Scheduler.Round} · " : "Before round 1 · ") + $"{Position} of {Count}";

    /// <summary>
    /// The positions a unit stops at, ascending, from 0 to the log's length. Step: before each player input (an event a participant's
    /// command wrote, after one the engine or another participant wrote), so a step shows an input and everything the rules did with it.
    /// Action: before each action frame starts (a sequence with an action, none open). Turn and Round: just after each turn_started and
    /// round_started, so the header already names it.
    /// </summary>
    public static IReadOnlyList<int> Stops(IReadOnlyList<GameEvent> log, ReplayUnit unit)
    {
        var stops = new List<int> { 0 };
        (Guid Frame, long Seq)? action = null;
        for (var i = 0; i < log.Count; i++)
        {
            var e = log[i];
            int? stop = unit switch
            {
                ReplayUnit.Step => e.Actor is { } actor && (i == 0 || log[i - 1].Actor != actor) ? i : null,
                ReplayUnit.Turn => e.Payload is TurnStarted ? i + 1 : null,
                ReplayUnit.Round => e.Payload is RoundStarted ? i + 1 : null,
                _ => null,
            };
            if (unit == ReplayUnit.Action)
                switch (e.Payload)
                {
                    case SequenceStarted { Context.Action: not null } s when action is null: action = (s.FrameId, e.Seq); stop = i; break;
                    case SequenceFinished f when action?.Frame == f.FrameId: action = null; break;
                    case SequenceAbandoned a when action?.Frame == a.FrameId: action = null; break;
                    case Undone u when action?.Seq > u.ToSeq: action = null; break;
                }
            if (stop is { } s2 && s2 > stops[^1]) stops.Add(s2);
        }
        if (stops[^1] < log.Count) stops.Add(log.Count);
        return stops;
    }
}

/// <summary>Replay playback: one step every StepSeconds at 1×, faster or slower by Speed.</summary>
public sealed class ReplayClock
{
    public static readonly IReadOnlyList<double> Speeds = [0.5, 1, 2, 4, 8];
    public const double StepSeconds = 1.5;
    double _since;

    public bool Playing { get; private set; }
    public double Speed { get; set; } = 1;

    public void Play() { Playing = true; _since = 0; }
    public void Pause() => Playing = false;

    /// <summary>Advances the clock by <paramref name="delta"/> seconds; true when the next step is due.</summary>
    public bool Tick(double delta)
    {
        if (!Playing) return false;
        _since += delta;
        if (_since < StepSeconds / Speed) return false;
        _since = 0;
        return true;
    }
}
