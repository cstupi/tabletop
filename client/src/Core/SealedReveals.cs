using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The decision box's reveals: each sealed Ask that closes with answers, as one chip per team (its band and its choice), read per batch
/// and queued in order, so a render that coalesces several batches still shows every reveal. A reveal is a note about the Ask that closed:
/// it shows only while no card of mine is up, and the next card clears it (a token spend never rides onto the Advance after the cast).</summary>
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

    IReadOnlyList<IReadOnlyList<RevealChip>>? _shown;

    /// <summary>What a render draws: with a card of mine up, nothing, and every reveal queued so far is dropped; else the reveals queued
    /// since the last render, oldest first, or the ones already shown when none came.</summary>
    public IReadOnlyList<IReadOnlyList<RevealChip>>? Shown(bool card)
    {
        var fresh = _pending.ToList();
        _pending.Clear();
        if (card) return _shown = null;
        if (fresh.Count > 0) _shown = fresh;
        return _shown;
    }
}
