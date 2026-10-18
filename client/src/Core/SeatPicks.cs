using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// Which of my team's open Asks this client answers (game-flow spec → Participants; rulebook p.50 Team Games: "for each team's turn only one of the
/// players may activate a single unit"). An Ask naming a Seat is that seat's player's; an OwnUnitsOnly Ask (activate, deploy, a reaction) offers each
/// player only their own units. `me` is Welcome's row; every row of its account is mine (self-play). The server refuses the rest.
/// </summary>
public static class SeatPicks
{
    /// <summary>The Ask as this client answers it: null when it is another player's of my team (its Seat, or an OwnUnitsOnly pick offering only
    /// their units), else the Ask with an OwnUnitsOnly legal set narrowed to my units. A null `me` narrows nothing.</summary>
    public static Ask? For(Ask ask, Projection p, GameInfo game, Guid? me)
    {
        var seats = Seats.Mine(game, me).Select(s => s.Id).ToHashSet();
        if (seats.Count == 0) return ask;
        if (ask.Seat is { } seat && !seats.Contains(seat)) return null;
        if (!ask.OwnUnitsOnly) return ask;
        bool Mine(Guid piece) => OwnerOf(p, piece) is not { } owner || seats.Contains(owner);
        switch (ask.Legal)
        {
            case Options o:
                var kept = o.Choices.Where(c => c.Pieces.All(Mine)).ToList();
                if (kept.Count == o.Choices.Count) return ask;
                return kept.Any(c => c.Pieces.Count > 0) || !o.Choices.Any(c => c.Pieces.Count > 0) ? ask with { Legal = new Options(kept) } : null;
            case PieceSelection s:
                var eligible = s.Eligible.Where(Mine).ToList();
                if (eligible.Count == s.Eligible.Count) return ask;
                return eligible.Count > 0 ? ask with { Legal = s with { Eligible = eligible, Max = Math.Min(s.Max, eligible.Count), Min = Math.Min(s.Min, eligible.Count) } } : null;
            default: return ask;
        }
    }

    /// <summary>The players who may answer for an addressed team: the Seat's, or the owners of the units an OwnUnitsOnly Ask offers; empty when
    /// any player of the team may (the round boundary, a roll-off).</summary>
    public static IReadOnlyList<Guid> Answerers(Ask ask, Projection p)
    {
        if (ask.Seat is { } seat) return [seat];
        if (!ask.OwnUnitsOnly) return [];
        IEnumerable<Guid> pieces = ask.Legal switch { Options o => o.Choices.SelectMany(c => c.Pieces), PieceSelection s => s.Eligible, _ => [] };
        return pieces.Select(id => OwnerOf(p, id)).OfType<Guid>().Distinct().Order().ToList();
    }

    /// <summary>The seat whose army a unit or model is; null for anything else (an empty piece, a marker).</summary>
    static Guid? OwnerOf(Projection p, Guid piece) =>
        p.Units.GetValueOrDefault(piece)?.ParticipantId
        ?? (p.Models.GetValueOrDefault(piece) is { } m ? p.Units.GetValueOrDefault(m.UnitId)?.ParticipantId : null);
}
