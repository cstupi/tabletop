using Tabletop.Content;
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

    /// <summary>The objective markers where they stand now: the setup rows, then those placed in play, the projection's entry winning on a shared id.</summary>
    public IReadOnlyList<Marker> ObjectiveMarkers => ObjectiveMarkersOf(Projection);

    public static IReadOnlyList<Marker> ObjectiveMarkersOf(Projection projection)
    {
        var play = projection.Markers;
        return projection.Board.Table.Markers.Where(m => !play.ContainsKey(m.Id)).Concat(play.Values)
            .Where(m => m.Kind == MarkerKind.Objective).ToList();
    }

    // Wounds, conditions, actions, grants and the unit definition with its stats.
    public UnitInPlay UnitOf(Guid unitId) => Projection.Units[unitId];
    public ModelInPlay ModelOf(Guid modelId) => Projection.Models[modelId];

    /// <summary>The team owning a unit, through its participant. Null when no GameInfo was supplied.</summary>
    public Guid? TeamOf(Guid unitId) =>
        Info?.Participants.FirstOrDefault(p => p.Id == Projection.Units[unitId].ParticipantId)?.TeamId;
}
