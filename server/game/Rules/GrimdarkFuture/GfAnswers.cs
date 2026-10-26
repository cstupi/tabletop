using Tabletop.Geometry;
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

    public static IReadOnlyList<ModelRelocation> Moves(GameState state, AskRecord closed) =>
        closed.Single is Destinations answer && closed.Ask.Legal is MoveRegion region ? Moves(state, region, answer) : [];

    /// <summary>
    /// The one reading of a move answer: what the move steps apply and what IGameRules.AnswerBreach judges, so a check can never pass a move
    /// the application reads differently. The relocations of models the Ask named and the projection holds, the first one per model. From is
    /// the projection's location, never the answer's, so a forged From cannot skip the distance check. Dropped: a placement with a non-finite
    /// coordinate or yaw, or one farther from where the model stands than the table's diagonal, which no move ending on the table can be (p.7: "never
    /// move outside of the battlefield"; it also bounds what Movement.Check sweeps); a move Inside a piece the Ask did not list as enterable; a move out of a container the Ask is not exiting (p.15:
    /// "units can't both embark/disembark as part of the same activation"). A model the reading drops has not moved. A charge's (ContactWith) is
    /// read as the charge ends it: its models within the grace of the target, or the nearest one, drawn into base contact (GfMoves.ChargeLanding).
    /// </summary>
    public static IReadOnlyList<ModelRelocation> Moves(GameState state, MoveRegion region, Destinations answer) =>
        GfMoves.ChargeLanding(state, region, answer, Read(state, region, answer));

    static List<ModelRelocation> Read(GameState state, MoveRegion region, Destinations answer)
    {
        var enterable = region.Containment?.Enterable ?? [];
        return answer.Moves
            .Where(m => region.ModelIds.Contains(m.ModelId) && state.Projection.Models.ContainsKey(m.ModelId))
            .DistinctBy(m => m.ModelId)
            .Select(m => m with { From = state.Projection.Models[m.ModelId].Location })
            .Where(m => m.To switch
            {
                Inside into => enterable.Contains(into.ContainerId),
                OnTable on => Finite(on.Placement) && WithinDiagonal(state, m.From, on.Placement) && (m.From is not Inside from || region.Containment?.ExitingId == from.ContainerId),
                _ => true,
            }).ToList();
    }

    /// <summary>
    /// The first relocation of a listed model the reading does not apply: one Moves drops, or one to somewhere no move step puts a model (the
    /// shelf). Null when every relocation is read. AnswerBreach refuses it under strict rather than let it pass as a silent zero move. A
    /// relocation to where the model already is (an occupant answered Inside the container it is in) is standing still, not a dropped one:
    /// it is how a model with nowhere to stand answers, and it moves nothing (a transport's survivor is then destroyed: GfContainers).
    /// </summary>
    public static ModelRelocation? Unread(GameState state, MoveRegion region, Destinations answer)
    {
        var read = Moves(state, region, answer).ToDictionary(m => m.ModelId);
        return answer.Moves.Where(m => region.ModelIds.Contains(m.ModelId) && state.Projection.Models.ContainsKey(m.ModelId)).DistinctBy(m => m.ModelId)
            .Where(m => m.To != state.Projection.Models[m.ModelId].Location)
            .FirstOrDefault(m => !read.TryGetValue(m.ModelId, out var r) || r.To is not (OnTable or Inside));
    }

    static bool WithinDiagonal(GameState state, Location from, Placement to)
    {
        var size = state.Board.Table.Size;
        return from is not OnTable at || at.Placement.Position.Horizontal.DistanceTo(to.Position.Horizontal) <= Math.Sqrt(size.Width * size.Width + size.Length * size.Length);
    }

    static bool Finite(Placement p) => double.IsFinite(p.Position.X) && double.IsFinite(p.Position.Y) && double.IsFinite(p.Position.Z) && double.IsFinite(p.Yaw.Degrees);
}
