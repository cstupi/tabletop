using Tabletop.GameServer.Interpreter;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Answers read through the Ask that recorded them. Under warn and silent an answer outside the legal set is accepted, so a step
/// never trusts its type or its ids: what is not in the recorded set is dropped, and an answer of the wrong type is an empty one.
/// A throw inside Advance is replayed by every rehydrate, so nothing here may throw.
/// </summary>
public static class GfAnswers
{
    /// <summary>The pieces an answer named that the Ask offered, each once, at most the Ask's maximum.</summary>
    public static IReadOnlyList<Guid> Pieces(AskAnswer? answer, Ask? ask) =>
        answer is SelectedPieces picked && ask?.Legal is PieceSelection set ? picked.PieceIds.Distinct().Where(set.Eligible.Contains).Take(set.Max).ToList() : [];

    /// <summary>An earlier step's piece answer, read against the Ask that frame recorded for it.</summary>
    public static IReadOnlyList<Guid> Pieces(GameState state, FrameView frame, string step, string question) =>
        Pieces(frame.Frame.Steps.LastOrDefault(s => s.Step == step)?.Answer, FrameReader.Recorded(state, frame.Id, question));

    /// <summary>The option an answer chose, looked up among the Ask's own choices; null for a pass, another type or an id the Ask did not offer.</summary>
    public static AskOption? Option(AskRecord closed) =>
        closed.Single is Chosen chosen && closed.Ask.Legal is Options set ? set.Choices.FirstOrDefault(c => c.Id == chosen.OptionId) : null;

    /// <summary>Wounds per model: only models the Ask offered and that are still in the unit, none below one, and no more in total than the wounds owed.</summary>
    public static Dictionary<Guid, int> Allocated(AskRecord closed, IReadOnlyCollection<Guid> alive, int owed)
    {
        var result = new Dictionary<Guid, int>();
        if (closed.Single is not Allocated answer || closed.Ask.Legal is not Allocation set) return result;
        var left = owed;
        foreach (var (model, count) in answer.Counts)
        {
            if (!set.Targets.Contains(model) || !alive.Contains(model) || count <= 0 || left <= 0) continue;
            var take = Math.Min(count, left);
            result[model] = take; left -= take;
        }
        return result;
    }

    /// <summary>
    /// The relocations of models the move Ask named and the projection holds, one per model. From is the projection's location, never the
    /// answer's, so a forged From cannot skip the distance check; a move Inside a piece the Ask did not list as enterable is dropped.
    /// </summary>
    public static IReadOnlyList<ModelRelocation> Moves(GameState state, AskRecord closed)
    {
        if (closed.Single is not Destinations answer || closed.Ask.Legal is not MoveRegion region) return [];
        var enterable = region.Containment?.Enterable ?? [];
        return answer.Moves
            .Where(m => region.ModelIds.Contains(m.ModelId) && state.Projection.Models.ContainsKey(m.ModelId) && (m.To is not Inside into || enterable.Contains(into.ContainerId)))
            .DistinctBy(m => m.ModelId)
            .Select(m => m with { From = state.Projection.Models[m.ModelId].Location }).ToList();
    }
}
