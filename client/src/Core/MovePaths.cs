using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One model's move path: where it stood (its base at the start's facing, as the server sweeps it), the route its move is measured along
/// (Movement.Measured, MoveRegions.Route), the base swept along it (SweptPath: footprints and the dangerous volumes it enters or crosses) and the
/// facing it ends at, where known (the ghost's or the answer's).</summary>
public sealed record ModelPath(Guid ModelId, PlacedModel Start, IReadOnlyList<Point2> Route, SweptBase Swept, Yaw? End = null)
{
    public IReadOnlyList<PathFootprint> Footprints => Swept.Footprints;
    public IReadOnlyList<Guid> Dangerous => Swept.Dangerous;
    public IReadOnlyList<PathStretch> Stretches => Swept.Stretches;
}

/// <summary>One answered move Ask: its parameters and the models it moved, from where to where (the move step's Destinations).</summary>
public sealed record MoveSegment(MoveRegion Legal, IReadOnlyList<ModelRelocation> Moves);

/// <summary>
/// The move path drawn as marks (PathMarks) and the dangerous terrain warning (spec 2026-11-21-move-path-and-dangerous-warning-design; owner ruling:
/// "Any part of the base counts"): each moving model's route, its base's footprints along it and the volumes the Ask lists as dangerous (the
/// unstable ones past UnstableOver) that its swept base enters or crosses, by SweptPath over Movement.VolumesOnPath, the geometry the server's
/// ApplyMove tests with, so the warning and the server's test agree. Drawn while a move Ask is open (Proposed) and, after it, for the unit's last
/// move until the next activation starts (Last, Measured), for every player and in a replay.
/// </summary>
public static class MovePaths
{
    public const string TestWords = "Dangerous terrain test";
    /// <summary>A mark where the base touches dangerous ground: the terrain's dangerous colour (TerrainEffects, the dangerous band's) at this opacity.</summary>
    public const float DangerousAlpha = 0.9f;

    /// <summary>The team colour lifted halfway to white: the trace is drawn over the move's clear band, which is the team colour itself.</summary>
    static Rgba Light(Rgba team) => new((team.R + 1) / 2, (team.G + 1) / 2, (team.B + 1) / 2);

    /// <summary>The marks' look (PathMarks): Fantasy stamps them in an ink of the team colour (`ink`), Sci-fi traces them in the team colour lifted
    /// halfway to white, both at these opacities; a mark where the base touches dangerous ground in the dangerous colour at DangerousAlpha; the kept
    /// last move's marks at KeptFade of those. Under every mark a dark rim (the shape at RimScale, RimAlpha black), so a light trace reads on a light
    /// print and a dangerous mark on the dangerous band.</summary>
    public const float InkAlpha = 0.7f, TraceAlpha = 0.75f, InkShade = 0.4f, KeptFade = 0.6f, RimScale = 1.45f, RimAlpha = 0.35f;

    /// <summary>A mark's colour for the moving team's colour, in the theme's look.</summary>
    public static Rgba MarkColour(PathMark m, Rgba team, bool ink, bool kept)
    {
        var c = m.Dangerous ? TerrainEffects.Of(TerrainDeclarations.Dangerous).Colour with { A = DangerousAlpha }
            : ink ? new Rgba(team.R * InkShade, team.G * InkShade, team.B * InkShade, InkAlpha) : Light(team) with { A = TraceAlpha };
        return kept ? c with { A = c.A * KeptFade } : c;
    }

    /// <summary>The rim under a mark.</summary>
    public static Rgba RimColour(bool kept) => new(0, 0, 0, kept ? RimAlpha * KeptFade : RimAlpha);

    /// <summary>Whether the model is a vehicle (ModelSlot.Vehicle, from the army list), whose path is drawn as treads.</summary>
    public static bool Vehicle(Projection p, Guid modelId) => p.Models.GetValueOrDefault(modelId)?.Slot.Vehicle == true;

    /// <summary>The Ask's models where they stand on the table, as MoveRegions.Of reads them.</summary>
    public static IReadOnlyDictionary<Guid, PlacedModel> Starts(Projection p, MoveRegion legal) =>
        legal.ModelIds.Where(id => p.Models.GetValueOrDefault(id)?.Location is OnTable).ToDictionary(id => id, id =>
        {
            var m = p.Models[id];
            return new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement);
        });

    /// <summary>The path of a model from its start along a route of this length, against the Ask's dangerous and unstable volumes, ending at `end`.</summary>
    public static ModelPath Of(Guid id, PlacedModel start, IReadOnlyList<Point2> route, double distance, MoveRegion legal, IReadOnlyList<WorldVolume> volumes,
        Yaw? end = null)
    {
        var dangerous = volumes.Where(v => legal.DangerousVolumeIds?.Contains(v.Id) == true).ToList();
        var unstable = volumes.Where(v => legal.UnstableVolumeIds?.Contains(v.Id) == true).ToList();
        return new ModelPath(id, start, route, SweptPath.Of(start, route, SweptPath.DangerousTo(distance, dangerous, unstable, legal.UnstableOver ?? 0)), end);
    }

    /// <summary>An open move Ask's paths: each of its models a ghost has moved from where it stands, along MoveRegions.Route (`capped`: the unit held to
    /// the cap, MoveRegions.Allowance), in the Ask's order, each ending at its ghost's facing in `facings`.</summary>
    public static IReadOnlyList<ModelPath> Proposed(MoveRegions regions, IReadOnlyDictionary<Guid, PlacedModel> starts, MoveRegion legal,
        IReadOnlyList<WorldVolume> volumes, IReadOnlyDictionary<Guid, Point2> to, bool capped = false, IReadOnlyDictionary<Guid, Yaw>? facings = null)
    {
        var paths = new List<ModelPath>();
        foreach (var id in legal.ModelIds)
        {
            if (!starts.TryGetValue(id, out var start) || !to.TryGetValue(id, out var end)) continue;
            if (start.Placement.Position.Horizontal.DistanceTo(end) <= 1e-6 || regions.Route(id, end, capped) is not { } route) continue;
            paths.Add(Of(id, start, route.Points, route.Distance, legal, volumes, facings?.GetValueOrDefault(id)));
        }
        return paths;
    }

    /// <summary>The label each crossing model carries (beside the contact marks): the test it owes.</summary>
    public static IReadOnlyDictionary<Guid, string> Marks(IEnumerable<ModelPath> paths) =>
        paths.Where(p => p.Dangerous.Count > 0).ToDictionary(p => p.ModelId, _ => TestWords);

    /// <summary>The decision box's gate lines, one per crossing model and volume: "Flayer Cavalry 1 crosses Toxic Smog: Dangerous terrain test".</summary>
    public static IReadOnlyList<string> Lines(IEnumerable<ModelPath> paths, Func<Guid, string> model, Func<Guid, string> volume) =>
        paths.SelectMany(p => p.Dangerous.Select(v => $"{model(p.ModelId)} crosses {volume(v)}: {TestWords}")).ToList();

    /// <summary>The name of the terrain piece a volume belongs to (TerrainLabel.NameOf), "terrain" when no piece holds it.</summary>
    public static string VolumeName(TableState table, Guid volumeId) =>
        table.Terrain.FirstOrDefault(t => t.Volumes.Any(v => v.Id == volumeId)) is { } piece ? TerrainLabel.NameOf(piece) : "terrain";

    /// <summary>
    /// The last activation's move, while it stands: every move step (a StepCompleted carrying Destinations, table to table) after the last
    /// ActionRecorded that comes before one, each with the move Ask opened before it. Empty once an ActionRecorded follows the last move step (the
    /// next activation has started) or when no move step is in the log.
    /// </summary>
    public static IReadOnlyList<MoveSegment> Last(IReadOnlyList<GameEvent> log)
    {
        var lastMove = -1;
        for (var i = log.Count - 1; i >= 0 && lastMove < 0; i--)
        {
            if (log[i].Payload is ActionRecorded) return [];
            if (IsMove(log[i].Payload)) lastMove = i;
        }
        if (lastMove < 0) return [];
        var from = 0;
        for (var i = lastMove; i >= 0; i--) if (log[i].Payload is ActionRecorded) { from = i; break; }
        var segments = new List<MoveSegment>();
        MoveRegion? legal = null;
        for (var i = from; i <= lastMove; i++)
        {
            if (log[i].Payload is AskOpened { Ask.Legal: MoveRegion region }) legal = region;
            else if (legal is not null && IsMove(log[i].Payload) && log[i].Payload is StepCompleted { Answer: Destinations d })
                segments.Add(new MoveSegment(legal, d.Moves.Where(m => m.From is OnTable && m.To is OnTable).ToList()));
        }
        return segments;
    }

    static bool IsMove(EventPayload e) => e is StepCompleted { Answer: Destinations d } && d.Moves.Any(m => m.From is OnTable && m.To is OnTable);

    /// <summary>A segment's paths as the server measured them (Movement.Measured over the Ask's volumes, as GfMoves.ApplyMove), each model's base
    /// from `bodyOf`; `standing`, the models on the table that did not move (none when the Ask PassesModels). A model with no body is left out.</summary>
    public static IReadOnlyList<ModelPath> Measured(MoveSegment segment, Func<Guid, ModelBody?> bodyOf, IReadOnlyList<WorldVolume> volumes, TableSize size,
        IReadOnlyList<PlacedModel> standing)
    {
        var legal = segment.Legal;
        List<WorldVolume> Named(IReadOnlyList<Guid>? ids) => ids is null ? [] : volumes.Where(v => ids.Contains(v.Id)).ToList();
        var (impassable, difficult, steep, dangerous, unstable) = (Named(legal.ImpassableVolumeIds), Named(legal.DifficultVolumeIds), Named(legal.SteepVolumeIds),
            Named(legal.DangerousVolumeIds), Named(legal.UnstableVolumeIds));
        var paths = new List<ModelPath>();
        foreach (var m in segment.Moves)
        {
            if (m.From is not OnTable from || m.To is not OnTable to || bodyOf(m.ModelId) is not { } body) continue;
            var start = new PlacedModel(body, from.Placement);
            var end = to.Placement.Position.Horizontal;
            if (from.Placement.Position.Horizontal.DistanceTo(end) <= 1e-6) continue;
            var path = Movement.Measured(start, end, Math.Max(0, legal.Allowance), impassable, difficult, legal.DifficultCap, size,
                legal.PassesModels ? null : standing, volumes, dangerous, unstable, legal.UnstableOver ?? 0, steep);
            paths.Add(Of(m.ModelId, start, path.Path, path.Distance, legal, volumes, to.Placement.Yaw));
        }
        return paths;
    }

    /// <summary>The kept segments' paths (Measured), reading the projection now and measuring later, on any thread: each moved model's base from its
    /// slot, the standing models every model on the table that none of the segments moves.</summary>
    public static Func<IReadOnlyList<ModelPath>> KeptLater(IReadOnlyList<MoveSegment> segments, Projection p, IReadOnlyList<WorldVolume> volumes, TableSize size)
    {
        var moving = segments.SelectMany(s => s.Moves.Select(m => m.ModelId)).ToHashSet();
        var bodies = moving.Where(p.Models.ContainsKey).ToDictionary(id => id, id => new ModelBody(p.Models[id].Slot.Base, p.Models[id].Slot.Height));
        var standing = p.Models.Values.Where(m => !moving.Contains(m.Id) && m.Location is OnTable)
            .Select(m => new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement)).ToList();
        return () => segments.SelectMany(s => Measured(s, id => bodies.TryGetValue(id, out var b) ? b : null, volumes, size, standing)).ToList();
    }
}
