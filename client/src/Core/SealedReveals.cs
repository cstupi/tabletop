using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The decision box's reveals: each sealed Ask that closes with answers, as one chip per team (its band and its choice), read per batch
/// and queued in order, so a render that coalesces several batches still shows every reveal, whatever Ask is open by then.</summary>
public sealed class SealedReveals
{
    readonly HashSet<Guid> _sealed = new();
    readonly List<IReadOnlyList<RevealChip>> _pending = new();

    public void Read(IReadOnlyList<GameEvent> events, Names names)
    {
        foreach (var payload in events.Select(e => e.Payload))
        {
            if (payload is AskOpened o && o.Ask.Sealed) _sealed.Add(o.Ask.Id);
            if (payload is AskClosed c && _sealed.Remove(c.AskId) && c.Answers.Count > 0)
                _pending.Add(c.Answers.Select(a => new RevealChip(names.Teams.FirstOrDefault(t => t.Id == a.TeamId)?.Ordinal ?? 0, names.Team(a.TeamId),
                    a.Answer is Chosen ch ? ch.OptionId : LogText.Answer(a.Answer, names))).ToList());
        }
    }

    /// <summary>The reveals queued since the last call, oldest first; the queue is empty afterwards.</summary>
    public IReadOnlyList<IReadOnlyList<RevealChip>> Drain()
    {
        var taken = _pending.ToList();
        _pending.Clear();
        return taken;
    }
}
