using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The decision box's "Revealed: …" lines: each sealed Ask that closes with answers, read per batch and queued in order,
/// so a render that coalesces several batches still shows every reveal, whatever Ask is open by then.</summary>
public sealed class SealedReveals
{
    readonly HashSet<Guid> _sealed = new();
    readonly List<string> _pending = new();

    public void Read(IReadOnlyList<GameEvent> events, Names names)
    {
        foreach (var payload in events.Select(e => e.Payload))
        {
            if (payload is AskOpened o && o.Ask.Sealed) _sealed.Add(o.Ask.Id);
            if (payload is AskClosed c && _sealed.Remove(c.AskId) && c.Answers.Count > 0)
                _pending.Add("Revealed: " + string.Join(", ", c.Answers.Select(a => $"{names.Team(a.TeamId)} {LogText.Answer(a.Answer, names)}")));
        }
    }

    /// <summary>The reveals queued since the last call, oldest first; the queue is empty afterwards.</summary>
    public IReadOnlyList<string> Drain()
    {
        var taken = _pending.ToList();
        _pending.Clear();
        return taken;
    }
}
