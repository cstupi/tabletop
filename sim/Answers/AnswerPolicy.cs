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
/// Where a model would rather be, or null for nowhere in particular. A goal ranks destinations the policy has already found legal; it
/// never adds one and never removes one, so the answer stays inside the legal set. It may read any state PolicyView carries.
/// </summary>
public delegate Point2? MoveGoal(Guid modelId, PolicyView view);

/// <summary>
/// An Ask in, an answer out, from the legal set's shape alone. This is the default policy and it stays
/// ignorant of state on purpose: a cleverer policy explores a narrower, more plausible region and finds
/// fewer bugs (spec → Two ways to answer an Ask). It never narrows the set either — every choice is uniform over
/// exactly what the server said was legal. It reads only PolicyView's geometry members and a model's unit (ModelOf, for the
/// region's Separation, which spares the moving unit's own models), never UnitOf, TeamOf or Properties; a policy that wants those
/// is a second IAnswerSource, named as such.
/// Deterministic for a seed, given the same run: one chooser stream advanced in Ask order.
///
/// The one opt-in exception is a <see cref="MoveGoal"/> (a scenario's <c>Goal</c>, e.g. <see cref="ObjectiveGoal.Nearest"/>): with one,
/// a model that has a goal draws all <see cref="PolicyOptions.MoveSamples"/> candidates and takes the legal one nearest its goal. The
/// goal is the state-reading part; the sampling and the legality check are the uniform policy's own. Without a goal, or for a model the
/// goal returns null for, the move is exactly the uniform one.
/// </summary>
public sealed class AnswerPolicy(Chooser chooser, PolicyOptions? options = null, MoveGoal? goal = null)
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
        var pool = a.MaxTargets is { } most && most < a.Targets.Count ? chooser.Subset(a.Targets, Math.Max(1, most)) : a.Targets;
        for (var i = 0; i < a.Amount; i++)
        {
            var target = chooser.Pick(pool);
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
        // A set of `size` targets in which every item finds one of its own (Assignment.TargetsOf), then each item aims within it.
        // When no set covers every item, a set covering the most; an item with none of its targets in it holds fire (p.8: "may" shoot).
        var size = Math.Min(Math.Max(a.MaxDistinctTargets, 1), a.Targets.Count);
        int Covered(IReadOnlyList<Guid> set) => a.Items.Count(i => a.TargetsOf(i.Id).Any(set.Contains));
        var allowed = chooser.Subset(a.Targets, size);
        if (Covered(allowed) < a.Items.Count)
        {
            var sets = Combinations(a.Targets, size).ToList();
            var most = sets.Max(Covered);
            if (most == 0) return ask.AllowPass ? new Passed() : throw new NoLegalAnswerException(ask);
            allowed = chooser.Pick(sets.Where(s => Covered(s) == most).ToList());
        }
        return new Assigned(a.Items.Where(i => a.TargetsOf(i.Id).Any(allowed.Contains))
            .ToDictionary(i => i.Id, i => chooser.Pick(allowed.Where(a.TargetsOf(i.Id).Contains).ToList())));
    }

    private static IEnumerable<IReadOnlyList<T>> Combinations<T>(IReadOnlyList<T> items, int size, int from = 0)
    {
        if (size == 0) { yield return []; yield break; }
        for (var i = from; i <= items.Count - size; i++)
            foreach (var rest in Combinations(items, size - 1, i + 1)) yield return [items[i], .. rest];
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
        // An Ask may list no impassable volume to sweep and still not let a model end in one, so every sample lands clear of them all.
        var landing = view.Volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();
        // Models are sampled in ModelIds order, each Clear of the landings already taken and of the models standing (p.7). A model not yet
        // sampled holds its own placement in `taken`, so no landing is put on a base that may yet stand still.
        var taken = m.ModelIds.Select(id => (id, At: view.PlacedOf(id))).Where(x => x.At is not null).ToDictionary(x => x.id, x => x.At!.Value);
        var destinations = new Dictionary<Guid, Point2?>();
        void Record(Guid id, Point2? at, PlacedModel? start)
        {
            destinations[id] = at;
            if (at is { } d) taken[id] = Landing(id, start, d, view);
            else if (start is { } s) taken[id] = s;
            else taken.Remove(id);
        }
        foreach (var id in m.ModelIds)
        {
            var start = view.PlacedOf(id);
            Func<Point2, bool> clear = at => Clear(id, start, at, m, view, taken);
            Record(id, start is { } placed
                ? Sample(placed, m, impassable, difficult, view, goal?.Invoke(id, view), landing, clear)
                : SampleExit(id, m, view, clear), start);
        }
        // The cap holds every model once any one's path enters difficult ground (MoveRegion.DifficultCap): a model sampled
        // further than it is sampled again within the cap. Nothing is redrawn when no path enters.
        var reach = m;
        if (m.DifficultCap is { } cap && cap < m.Allowance && m.ModelIds.Any(id => view.PlacedOf(id) is { } p && destinations[id] is { } d
                && Movement.Check(p, d, m.Allowance, impassable, difficult, cap, view.Table).EntersDifficult))
        {
            reach = m with { Allowance = cap };
            foreach (var id in m.ModelIds)
                if (view.PlacedOf(id) is { } p && destinations[id] is { } d && p.Placement.Position.Horizontal.DistanceTo(d) > cap)
                {
                    taken.Remove(id);
                    Record(id, Sample(p, reach, impassable, difficult, view, goal?.Invoke(id, view), landing, at => Clear(id, p, at, m, view, taken)), p);
                }
        }
        // A capped re-sample that finds no room stands its model at its start, where a model sampled after it may since have landed. Any
        // landing no longer Clear is drawn again within the same reach, once; a second time its model stands still (or stays inside), which
        // the server does not judge. Each model is redrawn at most once and then stands, so this ends.
        var redrawn = new HashSet<Guid>();
        while (m.ModelIds.FirstOrDefault(id => destinations[id] is { } d && !Clear(id, view.PlacedOf(id), d, m, view, taken)) is var bad && bad != Guid.Empty)
        {
            var p = view.PlacedOf(bad);
            taken.Remove(bad);
            Record(bad, p is { } s && redrawn.Add(bad)
                ? Sample(s, reach, impassable, difficult, view, goal?.Invoke(bad, view), landing, at => Clear(bad, s, at, m, view, taken))
                : null, p);
        }
        var moves = new List<ModelRelocation>();
        var sampled = false;
        foreach (var id in m.ModelIds)
        {
            var from = view.LocationOf(id);
            var start = view.PlacedOf(id);
            var destination = destinations[id];
            if (destination is null)
            {
                // Nowhere to stand. A model on the table stands still (legal unless the region sets a minimum); one inside a container is left
                // out, which leaves it where it is.
                if (start is not null) moves.Add(new ModelRelocation(id, from, from));
                continue;
            }
            sampled = true;
            moves.Add(new ModelRelocation(id, from, new OnTable(Landing(id, start, destination.Value, view).Placement)));
        }
        // Nothing sampled for any model: pass when the Ask allows it, else the stand-still answer above.
        return !sampled && ask.AllowPass ? new Passed() : new Destinations(moves);
    }

    /// <summary>
    /// A disembark: the models are Inside, so there is no start to walk from. A point within the stated exit distance of
    /// the container's placed position, retried until the base lies fully within it (Containment.BaseFullyWithin, which
    /// MoveContainment names), fully on the table and clear of impassable terrain, as every move's landing is. Null when the container has no table
    /// position or no sample passes.
    /// </summary>
    private Point2? SampleExit(Guid modelId, MoveRegion m, PolicyView view, Func<Point2, bool>? clear = null)
    {
        if (m.Containment is not { ExitingId: { } exiting, ExitWithin: { } within }) return null;
        if (_o.MoveSamples <= 0 || view.PlacedOf(exiting) is not { } container) return null;
        var landing = view.Volumes.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();
        var origin = container.Placement.Position.Horizontal;
        for (var i = 0; i < _o.MoveSamples; i++)
        {
            var bearing = chooser.Double() * Math.Tau;
            var distance = chooser.Double() * within;
            var candidate = new Point2(origin.X + Math.Cos(bearing) * distance, origin.Y + Math.Sin(bearing) * distance);
            var placed = new PlacedModel(view.BodyOf(modelId), new Placement(new Point3(candidate.X, candidate.Y, 0), new Yaw(0)));
            if (Containment.BaseFullyWithin(placed, container, within) && Table.ContainsBase(view.Table, placed) && !Lands(placed, candidate, landing)
                && (clear is null || clear(candidate))) return candidate;
        }
        return null;
    }

    /// <summary>
    /// A uniform bearing and a uniform distance inside the allowance (and over its minimum), retried until Movement.Check likes it. The reach is
    /// the whole allowance: the difficult cap binds only a path that enters difficult ground, and Movement.Check, given
    /// the server-stated cap, rejects such a sample. Null when no sample passes: the caller stands still, except under a minimum, which answers
    /// the point at the minimum toward the farthest corner instead.
    /// With a <paramref name="target"/>, every sample is drawn and the passing one nearest the target wins. A sample whose base, where it
    /// lands, touches a <paramref name="landing"/> volume is rejected too.
    /// </summary>
    private static bool Lands(PlacedModel start, Point2 at, IReadOnlyList<WorldVolume> volumes)
    {
        var there = start with { Placement = start.Placement with { Position = new Point3(at.X, at.Y, start.Placement.Position.Z) } };
        return Movement.Check(there, at, 0, volumes, [], null, null).CrossesImpassable;
    }

    private Point2? Sample(PlacedModel start, MoveRegion m, IReadOnlyList<WorldVolume> impassable,
        IReadOnlyList<WorldVolume> difficult, PolicyView view, Point2? target = null, IReadOnlyList<WorldVolume>? landing = null,
        Func<Point2, bool>? clear = null)
    {
        var origin = start.Placement.Position.Horizontal;
        // A minimum (MoveRegion.MinDistance) the table has room for is sampled above; one it has no room for is met only at the farthest corner.
        var least = 0.0;
        Point2? fallback = null;
        if (m.MinDistance is { } minimum)
        {
            var (reach, corner) = Movement.FarthestOnTable(start, view.Table);
            if (reach < minimum) return reach <= m.Allowance ? corner : null;
            least = minimum;
            // When every sample misses, standing still is not legal: the point at the minimum on the ray to the farthest corner is, since the
            // table inset by the base is convex and holds both the start and the corner.
            fallback = least > m.Allowance ? null : reach > 0 ? origin + (corner - origin) * (least / reach) : origin;
        }
        Point2? best = null;
        for (var i = 0; i < _o.MoveSamples; i++)
        {
            var bearing = chooser.Double() * Math.Tau;
            var distance = least + chooser.Double() * (m.Allowance - least);
            var candidate = new Point2(origin.X + Math.Cos(bearing) * distance, origin.Y + Math.Sin(bearing) * distance);
            var check = Movement.Check(start, candidate, m.Allowance, impassable, difficult, m.DifficultCap, view.Table);
            if (check is not { WithinAllowance: true, CrossesImpassable: false, LeavesTable: false }) continue;
            if (landing is { Count: > 0 } && Lands(start, candidate, landing)) continue;
            if (clear is not null && !clear(candidate)) continue;
            if (target is not { } goal) return candidate;
            if (best is null || candidate.DistanceTo(goal) < best.Value.DistanceTo(goal)) best = candidate;
        }
        return best ?? fallback;
    }

    /// <summary>The model's body landed at a point: its start's yaw, rested on what is under its base, as the server settles it.</summary>
    private static PlacedModel Landing(Guid id, PlacedModel? start, Point2 at, PolicyView view)
    {
        var yaw = start?.Placement.Yaw ?? new Yaw(0);
        return new PlacedModel(view.BodyOf(id), new Placement(new Point3(at.X, at.Y, view.RestOf(id, at, yaw)), yaw));
    }

    /// <summary>
    /// The region's own contact fields, as the server reads them (p.7, GfMoves.ContactBreach): the landing on no base, the swept path across
    /// no model standing (unless PassesModels; one it already overlaps is left, Collision.Crosses), and Separation from every other unit's
    /// model. A geometry test, as Lands is.
    /// </summary>
    private static bool Clear(Guid id, PlacedModel? start, Point2 at, MoveRegion m, PolicyView view, IReadOnlyDictionary<Guid, PlacedModel> taken)
    {
        var landing = Landing(id, start, at, view);
        var standing = view.Projection.Models.Keys.Where(x => x != id && !m.ModelIds.Contains(x)).Select(x => (Id: x, At: view.PlacedOf(x)))
            .Where(x => x.At is not null).Select(x => (x.Id, At: x.At!.Value)).ToList();
        var everyone = standing.Concat(taken.Where(kv => kv.Key != id).Select(kv => (Id: kv.Key, At: kv.Value))).ToList();
        if (everyone.Any(o => Overlap.Bases(landing, o.At))) return false;
        if (start is { } s && !m.PassesModels && Collision.Crosses(s, at, standing.Select(o => o.At).ToList())) return false;
        var unit = view.ModelOf(id).UnitId;
        return m.Separation is not { } gap || everyone.All(o => view.ModelOf(o.Id).UnitId == unit || Distance.EdgeToEdge(landing, o.At) >= gap + 1e-6);
    }
}
