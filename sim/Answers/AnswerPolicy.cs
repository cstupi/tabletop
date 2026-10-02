using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

/// <summary>Thrown when a legal set admits no answer at all — an empty options list on an Ask that forbids passing.</summary>
public sealed class NoLegalAnswerException(Ask ask)
    : Exception($"the legal set of ask '{ask.Question}' ({ask.Legal.GetType().Name}) admits no answer and the ask forbids passing");

/// <param name="PassProbability">How often a pass is taken when one is allowed and something else is too. Zero: the policy always acts.</param>
/// <param name="EmbarkProbability">How often a move that may embark does.</param>
/// <param name="MoveSamples">Candidate destinations tried before the model stands still.</param>
public sealed record PolicyOptions(double PassProbability = 0, double EmbarkProbability = 0.25, int MoveSamples = 24);

/// <summary>
/// An Ask in, an answer out, from the legal set's shape alone. This is the default policy and it stays
/// ignorant of state on purpose: a cleverer policy explores a narrower, more plausible region and finds
/// fewer bugs (spec → Two ways to answer an Ask). It never narrows the set either — every choice is uniform over
/// exactly what the server said was legal. It reads only PolicyView's geometry members, never UnitOf,
/// ModelOf, TeamOf or Properties; a policy that wants those is a second IAnswerSource, named as such.
/// Deterministic for a seed, given the same run: one chooser stream advanced in Ask order.
/// </summary>
public sealed class AnswerPolicy(Chooser chooser, PolicyOptions? options = null)
{
    private readonly PolicyOptions _o = options ?? new PolicyOptions();

    /// <summary>Every LegalSet member the Protocol declares. A shape missing here is a compile-time hole, not a runtime one.</summary>
    public static IReadOnlyList<string> ShapeNames { get; } =
        ["options", "pieces", "allocation", "assignment", "move_region", "token_spend", "confirmation"];

    public AskAnswer Answer(Ask ask, PolicyView view) => ask.Legal switch
    {
        Options o => AnswerOptions(ask, o),
        PieceSelection p => AnswerPieces(ask, p),
        Allocation a => AnswerAllocation(ask, a),
        Assignment a => AnswerAssignment(ask, a),
        MoveRegion m => AnswerMove(ask, m, view),
        TokenSpend t => AnswerSpend(ask, t),
        Confirmation => new Confirmed(),
        _ => throw new NotSupportedException($"legal set {ask.Legal.GetType().Name} has no policy; add it to AnswerPolicy and to ShapeNames"),
    };

    /// <summary>A pass, when the Ask allows one and the dice say so. Null means "answer the shape instead".</summary>
    private AskAnswer? Pass(Ask ask) => ask.AllowPass && chooser.Chance(_o.PassProbability) ? new Passed() : null;

    private AskAnswer AnswerOptions(Ask ask, Options o)
    {
        if (o.Choices.Count == 0) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        return Pass(ask) ?? new Chosen(chooser.Pick(o.Choices).Id);
    }

    /// <summary>Ids out of the list the server handed over, whatever they name: units for a deploy, one model of a unit for a Takedown's pick (p.14).</summary>
    private AskAnswer AnswerPieces(Ask ask, PieceSelection p)
    {
        var max = Math.Min(p.Max, p.Eligible.Count);
        if (max < p.Min) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        return Pass(ask) ?? new SelectedPieces(chooser.Subset(p.Eligible, p.Min + chooser.Next(max - p.Min + 1)));
    }

    private AskAnswer AnswerAllocation(Ask ask, Allocation a)
    {
        if (a.Targets.Count == 0 || a.Amount <= 0) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        if (Pass(ask) is { } passed) return passed;
        var counts = new Dictionary<Guid, int>();
        for (var i = 0; i < a.Amount; i++)
        {
            var target = chooser.Pick(a.Targets);
            counts[target] = counts.GetValueOrDefault(target) + 1;
        }
        return new Allocated(counts);
    }

    /// <summary>
    /// p.13's spend: any number of tokens, per model, up to what that model holds. One draw per entry in the map, each
    /// uniform in [0, that model's maximum], which is what makes a model's own bound the thing under test — a single
    /// draw against the sum could never produce the answer "one from the model holding four and none from the other".
    /// Entries are taken in the order the server sent them. An empty map is nothing to spend.
    /// </summary>
    private AskAnswer AnswerSpend(Ask ask, TokenSpend t)
    {
        if (t.MaxByModel.Count == 0) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        if (Pass(ask) is { } passed) return passed;
        var counts = new Dictionary<Guid, int>();
        foreach (var (modelId, max) in t.MaxByModel)
            counts[modelId] = chooser.Next(Math.Max(max, 0) + 1);
        return new TokensSpent(counts);
    }

    private AskAnswer AnswerAssignment(Ask ask, Assignment a)
    {
        if (a.Items.Count == 0 || a.Targets.Count == 0) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        if (Pass(ask) is { } passed) return passed;
        var allowed = chooser.Subset(a.Targets, Math.Min(Math.Max(a.MaxDistinctTargets, 1), a.Targets.Count));
        return new Assigned(a.Items.ToDictionary(i => i.Id, _ => chooser.Pick(allowed)));
    }

    private AskAnswer AnswerMove(Ask ask, MoveRegion m, PolicyView view)
    {
        if (m.ModelIds.Count == 0) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
        if (Pass(ask) is { } passed) return passed;

        // Embarking and disembarking are the containment field's, and it carries every id and radius they need.
        if (m.Containment is { Enterable.Count: > 0 } c && chooser.Chance(_o.EmbarkProbability))
        {
            var container = chooser.Pick(c.Enterable);
            return new Destinations(m.ModelIds.Select(id => new ModelRelocation(id, view.LocationOf(id), new Inside(container))).ToList());
        }

        var impassable = view.VolumesById(m.ImpassableVolumeIds);
        var difficult = view.VolumesById(m.DifficultVolumeIds);
        var moves = new List<ModelRelocation>();
        var sampled = false;
        foreach (var id in m.ModelIds)
        {
            var from = view.LocationOf(id);
            var start = view.PlacedOf(id);
            var destination = start is { } placed
                ? Sample(placed, m, impassable, difficult, view)
                : SampleExit(m, view);
            if (destination is null)
            {
                moves.Add(new ModelRelocation(id, from, from));   // stand still: always legal
                continue;
            }
            sampled = true;
            var yaw = start?.Placement.Yaw ?? new Yaw(0);
            moves.Add(new ModelRelocation(id, from, new OnTable(new Placement(
                new Point3(destination.Value.X, destination.Value.Y, view.SurfaceAt(destination.Value)), yaw))));
        }
        // Nothing sampled for any model: pass when the Ask allows it, else the stand-still answer above.
        return !sampled && ask.AllowPass ? new Passed() : new Destinations(moves);
    }

    /// <summary>
    /// A disembark: the models are Inside, so there is no start to walk from. A point within the stated exit distance of
    /// the container's placed position; the server checks the placement. Null when the container has no table position.
    /// </summary>
    private Point2? SampleExit(MoveRegion m, PolicyView view)
    {
        if (m.Containment is not { ExitingId: { } exiting, ExitWithin: { } within }) return null;
        if (_o.MoveSamples <= 0 || view.PlacedOf(exiting) is not { } container) return null;
        var origin = container.Placement.Position.Horizontal;
        var bearing = chooser.Double() * Math.Tau;
        var distance = chooser.Double() * within;
        return new Point2(origin.X + Math.Cos(bearing) * distance, origin.Y + Math.Sin(bearing) * distance);
    }

    /// <summary>
    /// A uniform bearing and a uniform distance inside the allowance, retried until Movement.Check likes it. The reach is
    /// the whole allowance: the difficult cap binds only a path that enters difficult ground, and Movement.Check, given
    /// the server-stated cap, rejects such a sample. Null when no sample passes: the caller stands still.
    /// </summary>
    private Point2? Sample(PlacedModel start, MoveRegion m, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, PolicyView view)
    {
        var origin = start.Placement.Position.Horizontal;
        for (var i = 0; i < _o.MoveSamples; i++)
        {
            var bearing = chooser.Double() * Math.Tau;
            var distance = chooser.Double() * m.Allowance;
            var candidate = new Point2(origin.X + Math.Cos(bearing) * distance, origin.Y + Math.Sin(bearing) * distance);
            var check = Movement.Check(start, candidate, m.Allowance, impassable, difficult, m.DifficultCap, view.Table);
            if (check is { WithinAllowance: true, CrossesImpassable: false, LeavesTable: false }) return candidate;
        }
        return null;
    }
}
