# Task 2: The answer policy — every legal-set shape, from the shape alone

**Files:**
- Create: `sim/Answers/PolicyView.cs`, `sim/Answers/AnswerPolicy.cs`
- Test: `tests/Tabletop.Sim.Tests/AnswerPolicyTests.cs`

**Interfaces:**
- Consumes: `Ask`, `AskOption`, `LegalSet` and its seven members (`Options`, `PieceSelection`, `Allocation`, `Assignment`, `MoveRegion`, `TokenSpend`, `Confirmation`), `MoveContainment`, `AskAnswer` and its eight members (`Chosen`, `SelectedPieces`, `Allocated`, `Assigned`, `Destinations`, `TokensSpent`, `Confirmed`, `Passed`), `ModelRelocation`, `Location` (`OnTable`, `OnShelf`, `Inside`), `Projection`, `ModelInPlay`, `UnitInPlay`, `Board`, `GameInfo` (Protocol); `PropertySet` (game server `Tabletop.GameServer.Properties`); `PlacedModel`, `ModelBody`, `WorldVolume`, `Placement`, `Point2`, `Point3`, `Yaw`, `TableSize`, `Movement`, `MoveCheck`, `Settling` (Geometry); `TerrainPlacement.WorldVolumes()` (Content); `Chooser` (Task 1).
- Produces: `sealed class PolicyView(Projection projection, Board board, IReadOnlyList<WorldVolume>? volumes = null, GameInfo? info = null, PropertySet? properties = null)` with `Projection Projection`, `Board Board`, `GameInfo? Info`, `PropertySet? Properties`, `TableSize Table`, `IReadOnlyList<WorldVolume> Volumes`, `IReadOnlyList<WorldVolume> VolumesById(IReadOnlyList<Guid> ids)`, `Location LocationOf(Guid modelId)`, `PlacedModel? PlacedOf(Guid modelId)`, `ModelBody BodyOf(Guid modelId)`, `double SurfaceAt(Point2 point)`, `UnitInPlay UnitOf(Guid unitId)`, `ModelInPlay ModelOf(Guid modelId)`, `Guid? TeamOf(Guid unitId)`; `sealed record PolicyOptions(double PassProbability = 0, double EmbarkProbability = 0.25, int MoveSamples = 24)`; `sealed class NoLegalAnswerException(Ask ask) : Exception`; `sealed class AnswerPolicy(Chooser chooser, PolicyOptions? options = null)` with `AskAnswer Answer(Ask ask, PolicyView view)` and `static IReadOnlyList<string> ShapeNames`.

Spec: spec → *Two ways to answer an Ask* — "A seeded policy. Any Ask, answered from its legal set with a seeded random choice"; "It chooses legally, not well"; "An answer is chosen from what the server offered, whole"; "the default policy stays uniform over the legal set and ignorant of state". Spec → *Deliberately not decided*: "the policy prefers to act. `PolicyOptions.PassProbability` defaults to `0`, so a pass is taken only when the legal set admits nothing else. It stays a knob rather than a decision"; that it is uniform over the legal set is spec → *Two ways to answer an Ask*.

**What `PolicyView` exposes, and what constrains what is done with it.** It is the whole state an
answer may read: the projection (positions, wounds, conditions, grants, scores, the scheduler
position, the open Asks), the board and its unit and model definitions with their stats, the team
each unit belongs to, and the pinned `PropertySet`. It also carries the geometry an answer needs to
be *expressible* — a `MoveRegion` names model ids, an allowance and volume ids and says nothing
about where those models stand, so `PlacedOf`, `BodyOf`, `VolumesById` and `SurfaceAt` are how a
destination gets computed at all.

Reading state is allowed, and the two prohibitions in
*Global Constraints* bound it: nothing here may narrow the legal set, and nothing here may compute
a rules outcome the engine should compute. Those are properties of the *policy*, not of the view, so
they are enforced by the tests below — `AnswerPolicy` answers from the whole set and its answers are
checked to be uniform over it — rather than by leaving members out of a class.

`Info` and `Properties` are nullable because a unit test builds a view from a bare board with
neither. `RunLoop` always supplies both, so the production path is complete; `TeamOf` returns null
when `Info` is absent.

**Two implementations, not one policy with a switch.** `AnswerPolicy` is the default and stays
uniform over the legal set and ignorant of state: it reads only the geometry members, and never
`UnitOf`, `ModelOf`, `TeamOf` or `Properties`. A policy that reads state is a **second
implementation of `IAnswerSource`** (Task 3), added when someone wants one, and it is not built here.

That shape rather than a `PolicyOptions` flag, for three reasons. The spec requires that a
state-reading policy be "opt-in and named as such" — a type name is named, a boolean defaulting to
false is not, and a boolean is one careless `new PolicyOptions(readState: true)` away from becoming
the soak default. `IAnswerSource` is already the seam the spec says a cleverer opponent "would live
behind", and `ComposedAnswers` already composes sources, so the second implementation costs no new
abstraction. And keeping `AnswerPolicy` free of state reads keeps its own tests able to pass a bare
view, which is what makes "uniform over the legal set" cheap to assert.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

public class AnswerPolicyTests
{
    static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7(), C = Guid.CreateVersion7();
    static Ask With(LegalSet legal, bool allowPass = false, bool sealedAsk = false) =>
        new(Guid.CreateVersion7(), "q", "p", [A], legal, allowPass, sealedAsk, null, null);
    static AnswerPolicy Policy(ulong seed = 1, PolicyOptions? o = null) => new(new Chooser(seed), o);
    internal static PolicyView EmptyViewForOtherTests() => new(Projection.Initial(new Board(Bare(), []), Info()), new Board(Bare(), []));
    static TableState Bare() => new(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
    static GameInfo Info() => new(Guid.CreateVersion7(), "g", GameStatus.Playing, A, "grimdark-future", "sha256:x",
        SchedulerShape.AlternatingActivation, Enforcement.Strict, false, RngSeed: 1, [new Team(A, 1)], []);

    [Fact]
    public void Options_picks_one_of_the_choices_by_id()
    {
        var ask = With(new Options([new AskOption("x", "X", []), new AskOption("y", "Y", [])]));
        var chosen = Assert.IsType<Chosen>(Policy().Answer(ask, EmptyViewForOtherTests()));
        Assert.Contains(chosen.OptionId, (string[])["x", "y"]);
    }

    [Fact]
    public void An_empty_options_set_passes_when_it_may_and_throws_when_it_may_not()
    {
        Assert.IsType<Passed>(Policy().Answer(With(new Options([]), allowPass: true), EmptyViewForOtherTests()));
        Assert.Throws<NoLegalAnswerException>(() => Policy().Answer(With(new Options([])), EmptyViewForOtherTests()));
    }

    [Fact]
    public void Pieces_respects_min_and_max_and_picks_only_eligible_ones()
    {
        var answer = Assert.IsType<SelectedPieces>(Policy().Answer(With(new PieceSelection([A, B, C], 2, 2)), EmptyViewForOtherTests()));
        Assert.Equal(2, answer.PieceIds.Count);
        Assert.All(answer.PieceIds, id => Assert.Contains(id, (Guid[])[A, B, C]));
        Assert.Equal(2, answer.PieceIds.Distinct().Count());
    }

    [Fact]
    public void Allocation_spends_the_whole_amount_over_the_targets()
    {
        var answer = Assert.IsType<Allocated>(Policy().Answer(With(new Allocation([A, B], 5)), EmptyViewForOtherTests()));
        Assert.Equal(5, answer.Counts.Values.Sum());
        Assert.All(answer.Counts.Keys, k => Assert.Contains(k, (Guid[])[A, B]));
    }

    [Fact]
    public void Assignment_covers_every_item_within_the_distinct_target_cap()
    {
        var ask = With(new Assignment([new AskOption("g1", "", []), new AskOption("g2", "", []), new AskOption("g3", "", [])], [A, B, C], 2));
        var answer = Assert.IsType<Assigned>(Policy().Answer(ask, EmptyViewForOtherTests()));
        Assert.Equal(["g1", "g2", "g3"], answer.Targets.Keys.Order());
        Assert.True(answer.Targets.Values.Distinct().Count() <= 2);
    }

    [Fact]
    public void A_token_spend_stays_within_every_model_own_cap_and_confirmation_confirms()
    {
        // p.13: any number of tokens up to what each model holds, so the bound is per model and not a total.
        var spend = new TokenSpend(new Dictionary<Guid, int> { [A] = 1, [B] = 4 });
        var answer = Assert.IsType<TokensSpent>(Policy().Answer(With(spend), EmptyViewForOtherTests()));
        Assert.Equal(new HashSet<Guid> { A, B }, answer.Counts.Keys.ToHashSet());   // one entry per model the map named, whatever order Guid comparison puts them in
        Assert.InRange(answer.Counts[A], 0, 1);
        Assert.InRange(answer.Counts[B], 0, 4);
        Assert.IsType<Confirmed>(Policy().Answer(With(new Confirmation()), EmptyViewForOtherTests()));
    }

    [Fact]
    public void Pass_is_taken_only_when_the_ask_allows_it_and_the_probability_says_so()
    {
        var ask = With(new Options([new AskOption("x", "X", [])]), allowPass: true);
        Assert.IsType<Passed>(Policy(1, new PolicyOptions(PassProbability: 1)).Answer(ask, EmptyViewForOtherTests()));
        Assert.IsType<Chosen>(Policy(1, new PolicyOptions(PassProbability: 0)).Answer(ask, EmptyViewForOtherTests()));
        Assert.IsType<Chosen>(Policy(1, new PolicyOptions(PassProbability: 1))
            .Answer(With(new Options([new AskOption("x", "X", [])])), EmptyViewForOtherTests()));
    }

    [Fact]
    public void The_same_seed_answers_a_run_of_asks_identically()
    {
        var asks = Enumerable.Range(0, 30).Select(i => With(new PieceSelection([A, B, C], 1, 3))).ToList();
        var one = Policy(77); var two = Policy(77);
        foreach (var ask in asks)
        {
            // SelectedPieces holds a List, so record equality is by reference: compare the ids.
            var a = Assert.IsType<SelectedPieces>(one.Answer(ask, EmptyViewForOtherTests()));
            var b = Assert.IsType<SelectedPieces>(two.Answer(ask, EmptyViewForOtherTests()));
            Assert.Equal(a.PieceIds, b.PieceIds);
        }
    }

    [Fact]
    public void Every_legal_set_shape_is_answered()
    {
        Assert.Equal(["allocation", "assignment", "confirmation", "move_region", "options", "pieces", "token_spend"], AnswerPolicy.ShapeNames.Order());
        var policy = Policy();
        LegalSet[] all =
        [
            new Options([new AskOption("x", "X", [])]),
            new PieceSelection([A], 1, 1),
            new Allocation([A], 1),
            new Assignment([new AskOption("g", "", [])], [A], 1),
            new TokenSpend(new Dictionary<Guid, int> { [A] = 1 }),
            new Confirmation(),
        ];
        foreach (var legal in all) Assert.NotNull(policy.Answer(With(legal), EmptyViewForOtherTests()));
    }
}
```

`EmptyViewForOtherTests` is `internal static` because other test classes in the project build a bare view with it.

- [ ] **Step 2: Write the failing move-region test** — the one shape that reads the view

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

public class MoveRegionPolicyTests
{
    static readonly Guid Model = Guid.CreateVersion7(), Unit = Guid.CreateVersion7(), Part = Guid.CreateVersion7(), Team = Guid.CreateVersion7();

    static (PolicyView View, Guid ModelId) Standing(Point3 at)
    {
        var slot = new ModelSlot(Model, 1, "m", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, []);
        var unit = new Unit(Unit, 1, "u", new Dictionary<string, string>(), [], [slot]);
        var table = new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
        var info = new GameInfo(Guid.CreateVersion7(), "g", GameStatus.Playing, Part, "grimdark-future", "sha256:x",
            SchedulerShape.AlternatingActivation, Enforcement.Strict, false, RngSeed: 2, [new Team(Team, 1)],
            [new Participant(Part, Guid.CreateVersion7(), "p", Team, null)]);
        var board = new Board(table, [new ArmyInPlay(Part, new ArmySnapshot(Guid.CreateVersion7(), "a", "grimdark-future", [unit]))]);
        var projection = Projection.Initial(board, info);
        var applier = new EventApplier(board, info);
        applier.Apply(new GameEvent(1, null, new ModelsMoved([new ModelRelocation(Model, new OnShelf(), new OnTable(new Placement(at, new Yaw(0))))]), null));
        return (new PolicyView(applier.Projection, board), Model);
    }

    [Fact]
    public void A_destination_is_within_the_allowance_and_on_the_table()
    {
        var (view, model) = Standing(new Point3(36, 24, 0));
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], new MoveRegion([model], 6, [], [], null, null), false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, view));
            var to = Assert.IsType<OnTable>(Assert.Single(answer.Moves).To);
            var check = Movement.Check(view.PlacedOf(model)!.Value, to.Placement.Position.Horizontal, 6, [], [], null, view.Table);
            Assert.True(check.WithinAllowance, $"seed {seed} moved {check.Distance}");
            Assert.False(check.LeavesTable);
            Assert.Equal(new OnTable(new Placement(view.PlacedOf(model)!.Value.Placement.Position, new Yaw(0))).GetType(), answer.Moves[0].From.GetType());
        }
    }

    [Fact]
    public void An_impassable_volume_is_not_crossed()
    {
        var (view, model) = Standing(new Point3(10, 24, 0));
        var wall = new WorldVolume(Guid.CreateVersion7(),
            new Volume(VolumeKind.Solid, Footprint.Rect(2, 40), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(14, 24, 0), new Yaw(0), 1));
        var walled = new PolicyView(view.Projection, view.Board, [wall]);
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team],
            new MoveRegion([model], 12, [wall.Id], [], null, null), false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, walled));
            var to = Assert.IsType<OnTable>(answer.Moves[0].To);
            Assert.False(Movement.Check(walled.PlacedOf(model)!.Value, to.Placement.Position.Horizontal, 12, [wall], [], null, walled.Table).CrossesImpassable);
        }
    }

    [Fact]
    public void Embarking_puts_every_model_inside_an_enterable_container()
    {
        var (view, model) = Standing(new Point3(36, 24, 0));
        var container = Guid.CreateVersion7();
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team],
            new MoveRegion([model], 6, [], [], null, new MoveContainment([container], null, null)), false, false, null, Guid.CreateVersion7());
        var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(4), new PolicyOptions(EmbarkProbability: 1)).Answer(ask, view));
        Assert.Equal(new Inside(container), answer.Moves[0].To);
    }

    [Fact]
    public void The_difficult_cap_does_not_shorten_the_reach_in_open_ground()
    {
        var (view, model) = Standing(new Point3(36, 24, 0));
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], new MoveRegion([model], 12, [], [], 6), false, false, null, Guid.CreateVersion7());
        var farthest = 0.0;
        for (var seed = 0ul; seed < 40; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, view));
            var to = Assert.IsType<OnTable>(Assert.Single(answer.Moves).To);
            var d = Movement.Check(view.PlacedOf(model)!.Value, to.Placement.Position.Horizontal, 12, [], [], null, view.Table).Distance;
            Assert.True(d <= 12 + 1e-9);
            farthest = Math.Max(farthest, d);
        }
        Assert.True(farthest > 6, $"farthest {farthest}");
    }

    [Fact]
    public void No_sampled_destination_stands_still_or_passes_and_never_throws()
    {
        var (view, model) = Standing(new Point3(36, 24, 0));
        var region = new MoveRegion([model], 6, [], [], null, null);
        var none = new PolicyOptions(MoveSamples: 0);
        var forced = new Ask(Guid.CreateVersion7(), "move", "Move", [Team], region, false, false, null, Guid.CreateVersion7());
        var moves = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(1), none).Answer(forced, view)).Moves;
        Assert.Equal(moves[0].From, moves[0].To);   // OnTable at the same placement: standing still
        var optional = forced with { AllowPass = true };
        Assert.IsType<Passed>(new AnswerPolicy(new Chooser(1), none).Answer(optional, view));
    }

    [Fact]
    public void Disembarking_places_the_models_on_the_table_within_the_exit_distance_of_the_container()
    {
        var wreck = Guid.CreateVersion7(); var rider = Guid.CreateVersion7();
        ModelSlot Slot(Guid id) => new(id, 1, "m", new Dictionary<string, string>(), [], Base.Circle(1.26), 1.5, null, []);
        var unit = new Unit(Unit, 1, "u", new Dictionary<string, string>(), [], [Slot(wreck), Slot(rider)]);
        var table = new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
        var info = new GameInfo(Guid.CreateVersion7(), "g", GameStatus.Playing, Part, "grimdark-future", "sha256:x",
            SchedulerShape.AlternatingActivation, Enforcement.Strict, false, RngSeed: 2, [new Team(Team, 1)],
            [new Participant(Part, Guid.CreateVersion7(), "p", Team, null)]);
        var board = new Board(table, [new ArmyInPlay(Part, new ArmySnapshot(Guid.CreateVersion7(), "a", "grimdark-future", [unit]))]);
        var applier = new EventApplier(board, info);
        var at = new Point3(30, 20, 0);
        applier.Apply(new GameEvent(1, null, new ModelsMoved([
            new ModelRelocation(wreck, new OnShelf(), new OnTable(new Placement(at, new Yaw(0)))),
            new ModelRelocation(rider, new OnShelf(), new Inside(wreck))]), null));
        var view = new PolicyView(applier.Projection, board);
        var ask = new Ask(Guid.CreateVersion7(), "disembark", "Disembark", [Team],
            new MoveRegion([rider], 6, [], [], null, new MoveContainment([], wreck, 6)), false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, view));
            var move = Assert.Single(answer.Moves);
            Assert.Equal(new Inside(wreck), move.From);
            var to = Assert.IsType<OnTable>(move.To);
            Assert.True(to.Placement.Position.Horizontal.DistanceTo(at.Horizontal) <= 6 + 1e-9, $"seed {seed}");
        }
    }
}
```

`PolicyView` therefore takes an optional volume override — the third parameter of `PolicyView(Projection, Board, IReadOnlyList<WorldVolume>? volumes = null, GameInfo? info = null, PropertySet? properties = null)` — because a test builds volumes without a `TerrainPlacement`, and `SocketDriver` gets its board from the wire. The last two are the state members the ruling permits; every test above passes the first two or three positionally and leaves them null.
- [ ] **Step 3: Run; expect failure, then write `PolicyView.cs`**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "AnswerPolicyTests|MoveRegionPolicyTests"`
Expected: `CS0246: ... 'AnswerPolicy' could not be found`.

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Answers;

/// <summary>
/// The state an answer may read, which is all of it: the projection (positions, wounds, conditions,
/// grants, scores, the scheduler position), the board and its definitions with their stats, the team
/// each unit belongs to, and the pinned PropertySet. The geometry members are what makes an answer
/// expressible at all — a MoveRegion names model ids without saying where they stand.
///
/// Reading is allowed; two things are not (Global Constraints). No caller
/// may narrow the legal set, and no caller may compute a rules outcome the engine should compute.
/// AnswerPolicy, the default, touches only the geometry members and is uniform over the legal set; a
/// state-reading policy is a second IAnswerSource and is not this class's business.
/// </summary>
public sealed class PolicyView(
    Projection projection, Board board, IReadOnlyList<WorldVolume>? volumes = null,
    GameInfo? info = null, PropertySet? properties = null)
{
    public Projection Projection { get; } = projection;
    public Board Board { get; } = board;

    /// <summary>Null only in a unit test that builds a bare board; RunLoop always supplies both.</summary>
    public GameInfo? Info { get; } = info;
    public PropertySet? Properties { get; } = properties;

    public TableSize Table { get; } = board.Table.Size;
    public IReadOnlyList<WorldVolume> Volumes { get; } = volumes ?? board.Table.Terrain.SelectMany(t => t.WorldVolumes()).ToList();

    public IReadOnlyList<WorldVolume> VolumesById(IReadOnlyList<Guid> ids) =>
        ids.Count == 0 ? [] : Volumes.Where(v => ids.Contains(v.Id)).ToList();

    public Location LocationOf(Guid modelId) => Projection.Models[modelId].Location;

    public PlacedModel? PlacedOf(Guid modelId)
    {
        var model = Projection.Models[modelId];
        return model.Location is OnTable table ? new PlacedModel(BodyOf(modelId), table.Placement) : null;
    }

    public ModelBody BodyOf(Guid modelId)
    {
        var slot = Projection.Models[modelId].Slot;
        return new ModelBody(slot.Base, slot.Height);
    }

    public double SurfaceAt(Point2 point) => Settling.SurfaceHeightAt(point, Volumes);

    // Wounds, conditions, actions, grants and the unit definition with its stats.
    public UnitInPlay UnitOf(Guid unitId) => Projection.Units[unitId];
    public ModelInPlay ModelOf(Guid modelId) => Projection.Models[modelId];

    /// <summary>The team owning a unit, through its participant. Null when no GameInfo was supplied.</summary>
    public Guid? TeamOf(Guid unitId) =>
        Info?.Participants.FirstOrDefault(p => p.Id == Projection.Units[unitId].ParticipantId)?.TeamId;
}
```

- [ ] **Step 4: Write `AnswerPolicy.cs`**

```csharp
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
```

The reach of a move is the whole allowance the server stated. The policy does not apply `DifficultCap`, because the engine sends it for every unit and applying it would cap Rush and Charge at 6 inches. `Movement.Check`, given the server's cap, rejects a sample that enters difficult ground past it.

The retry budget is per model, not per Ask: `Sample` is called once per model id in the region, so a unit of five gets five independent budgets and one model that cannot find a destination does not freeze the others.

A model that is inside a container has no start to walk from. `SampleExit` draws one point within the stated exit distance of the container's table position, and the server checks the placement. Embarking, disembarking and moving on the table are all exercised.

When no sample passes for any model, the answer is `Passed` if the Ask allows a pass, and otherwise the models stand still. The policy never throws for want of a destination.

- [ ] **Step 5: Run the tests; expect all green**

Run: `dotnet test tests/Tabletop.Sim.Tests --filter "AnswerPolicyTests|MoveRegionPolicyTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add sim/Answers tests/Tabletop.Sim.Tests
git commit -m "Answer every legal-set shape from the shape alone, with a seeded chooser"
```
