using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>An ISequenceStarter that records what was asked for and emits the sequence_started, checking nothing — the TurnCycle and RollOff unit tests drive the frame's life by hand.</summary>
public sealed class ScriptedStarter : ISequenceStarter
{
    public List<(string Sequence, SequenceContext Context)> Started { get; } = new();

    public IReadOnlyList<EventPayload> Start(GameState state, string sequence, SequenceContext context)
    {
        Started.Add((sequence, context));
        return [new SequenceStarted(Guid.CreateVersion7(), sequence, context)];
    }
}
