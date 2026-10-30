using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Recording;

/// <summary>
/// The second applier. It is a viewer, exactly like the client: it applies the events the driver drained and
/// writes down what the table looked like afterwards. It never decides anything and never talks to the engine.
/// </summary>
public sealed class FrameProjector(CompiledScenario scenario)
{
    private readonly EventApplier _applier = new(scenario.Board, scenario.Info);
    private readonly List<RecordedFrame> _frames = [];
    private readonly IReadOnlyDictionary<Guid, int> _ordinals = scenario.Info.Teams.ToDictionary(t => t.Id, t => t.Ordinal);

    public RecordedBoard Board { get; } = BuildBoard(scenario);
    public IReadOnlyList<RecordedFrame> Frames => _frames;
    public Projection Projection => _applier.Projection;

    public RecordedFrame Add(GameEvent e)
    {
        _applier.Apply(e);
        var p = _applier.Projection;
        var rolls = e.Payload is StepCompleted step ? step.Rolls.Select(RollOf).ToList() : [];
        var frame = new RecordedFrame(
            _frames.Count, e.Seq, EventKinds.Of(e.Payload), e.Actor, e.Warning,
            p.Scheduler.Round, p.Scheduler.Phase, p.Scheduler.ActiveTeamId,
            LogLines.Of(e, p, _ordinals),
            p.Models.Values.Select(ModelState).ToList(),
            p.Units.Values.Select(UnitState).ToList(),
            // Setup markers not moved in play, then the moved ones' projection entries.
            p.Board.Table.Markers.Where(m => !p.Markers.ContainsKey(m.Id)).Concat(p.Markers.Values).Select(m => MarkerState(m, p.Control)).ToList(),
            p.OpenAsks.Values.Select(AskOf).ToList(),
            rolls,
            // Copied, not aliased: the applier keeps mutating these and every frame is kept for the run.
            p.Scores.ToDictionary(kv => kv.Key, kv => kv.Value),
            p.Scheduler.Passed.ToList());
        _frames.Add(frame);
        return frame;
    }

    public Recording Build(RecordingMeta meta) => new(RecordingJson.SchemaVersion, meta, Board, _frames);

    private static RecordedModelState ModelState(ModelInPlay m) => m.Location switch
    {
        OnTable t => new RecordedModelState(m.Id, "table", t.Placement.Position.X, t.Placement.Position.Y,
            t.Placement.Position.Z, t.Placement.Yaw.Degrees, null, m.Wounds, m.SpellTokens, m.Conditions.Order().ToList()),
        Inside i => new RecordedModelState(m.Id, "inside", null, null, null, null, i.ContainerId,
            m.Wounds, m.SpellTokens, m.Conditions.Order().ToList()),
        _ => new RecordedModelState(m.Id, "shelf", null, null, null, null, null,
            m.Wounds, m.SpellTokens, m.Conditions.Order().ToList()),
    };

    // Kind, diameter and label ride along per frame, so a frame is whole state.
    private static RecordedMarkerState MarkerState(Marker m, IReadOnlyDictionary<Guid, Guid> control) => new(
        m.Id, m.Kind.ToString().ToLowerInvariant(), m.Diameter, m.Label,
        m.Position.X, m.Position.Y, m.Position.Z, m.Yaw.Degrees,
        control.TryGetValue(m.Id, out var team) ? team : null);

    private static RecordedUnitState UnitState(UnitInPlay u) => new(
        u.Id, u.Actions.Order().ToList(), u.Conditions.Order().ToList(),
        u.Grants.Select(g => g.EntryName).ToList(), u.WoundsThisActivation);

    private static RecordedAsk AskOf(OpenAsk open)
    {
        var ask = open.Ask;
        var (kind, options, pieces, allowance) = ask.Legal switch
        {
            Options o => ("options", o.Choices.Select(OptionOf).ToList(), (IReadOnlyList<Guid>)[], (double?)null),
            PieceSelection p => ("pieces", [], p.Eligible, null),
            Allocation a => ("allocation", [], a.Targets, null),
            Assignment a => ("assignment", a.Items.Select(OptionOf).ToList(), a.Targets, null),
            MoveRegion m => ("move_region", [], m.ModelIds, m.Allowance),
            TokenSpend t => ("token_spend", [], t.MaxByModel.Keys.ToList(), (double)t.MaxByModel.Values.Sum()),
            Confirmation => ("confirmation", [], [], null),
            _ => (ask.Legal.GetType().Name, [], [], null),
        };
        return new RecordedAsk(ask.Id, ask.Question, ask.Prompt, ask.Teams, ask.AllowPass, ask.Sealed,
            ask.WindowKind, ask.FrameId, kind, open.Answers.Keys.ToList(), options, pieces, allowance);
    }

    private static RecordedAskOption OptionOf(AskOption o) => new(o.Id, o.Label, o.Pieces);

    private static RecordedRoll RollOf(RollRecord r) => new(
        r.Id, r.Roll, r.Threshold, r.Dice.Count == 0 ? 0 : r.Dice[0].Modified - r.Dice[0].Raw,
        r.Dice.Select(d => d.Raw).ToList(), r.Dice.Select(d => d.Modified).ToList(), r.Dice.Select(d => d.Success).ToList(),
        r.Modifiers.Select(m => new RecordedModifier(m.Source, m.Amount)).ToList());

    private static RecordedBoard BuildBoard(CompiledScenario s)
    {
        var table = s.Board.Table;
        var teamOf = s.Info.Participants.Where(p => p.TeamId is not null).ToDictionary(p => p.Id, p => p.TeamId!.Value);
        var ordinalOf = s.Info.Teams.ToDictionary(t => t.Id, t => t.Ordinal);
        var models = s.Board.Armies.SelectMany(a => a.Snapshot.Units.SelectMany(u => u.Models.Select(m =>
            new RecordedModel(m.Id, u.Id, u.Name, m.Name, m.Ordinal, teamOf[a.ParticipantId], ordinalOf[teamOf[a.ParticipantId]],
                m.Base.Shape.ToString().ToLowerInvariant(), m.Base.A, m.Base.B, m.Height)))).ToList();

        return new RecordedBoard(
            table.Size.Width, table.Size.Length, VisualName(table.Texture),
            table.Terrain.Select(t => new RecordedTerrain(t.Id, VisualName(t.Mesh),
                t.Position.X, t.Position.Y, t.Position.Z, t.Yaw.Degrees, t.Scale,
                t.Volumes.Select(v => VolumeOf(v.Id, v.Volume)).ToList())).ToList(),
            table.Markers.Select(m => new RecordedMarker(m.Id, m.Kind.ToString().ToLowerInvariant(), m.Diameter, m.Label)).ToList(),
            table.Zones.Select(z => new RecordedZone(z.Id, z.Shape, z.TeamOrdinal, RingsOf(z.Shape, table.Size, s.Zones))).ToList(),
            models);
    }

    private static IReadOnlyList<IReadOnlyList<RecordedPoint>> RingsOf(string shape, TableSize size, ZoneLegs legs)
    {
        try
        {
            return ZoneStyles.Resolve(shape, size, legs)
                .Select(g => (IReadOnlyList<RecordedPoint>)g.Vertices.Select(v => new RecordedPoint(v.X, v.Y)).ToList()).ToList();
        }
        catch (ArgumentException) { return []; }
    }

    private static string VisualName(VisualRef visual) => visual.Builtin ?? visual.AssetId!.Value.ToString();

    private static RecordedVolume VolumeOf(Guid id, Volume v) => new(
        id, v.Kind.ToString().ToLowerInvariant(), v.Footprint.Kind.ToString().ToLowerInvariant(),
        v.Footprint.A, v.Footprint.B, v.Offset.X, v.Offset.Y, v.Yaw.Degrees, v.Bottom, v.Top,
        Declarations(v.Declarations), v.Notes);

    private static IReadOnlyList<string> Declarations(TerrainDeclarations d) =>
        Enum.GetValues<TerrainDeclarations>()
            .Where(f => f != TerrainDeclarations.None && d.HasFlag(f))
            .Select(f => string.Concat(f.ToString().Select((ch, i) => char.IsUpper(ch) && i > 0 ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString())))
            .ToList();
}
