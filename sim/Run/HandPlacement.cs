using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Run;

/// <summary>
/// Deployment's Ask names a unit; putting its models on the table is an ordinary MoveModels command, not an
/// Ask (scheduler → The turn is an Ask: "moving models by hand never ends one"). This is where the simulator
/// does that. It computes no rules outcome: the zone comes from the deployment_zone rows and
/// Tabletop.Geometry resolves the shape. A hand placement is judged for overlap (IGameRules.PlacementBreach, move_overlap, p.7
/// "physically impossible locations"), before round one for its zone (deploy_zone: p.6 fully within it, p.14 a Scout within 12" of it),
/// and once rounds have begun, for a set-aside unit (an Ambusher), for its distance from enemy units (ambush_distance: p.13 "anywhere over
/// 9” away from enemy units"). So an anchor is skipped where any base would touch a model on the table or leave the zone, and an
/// Ambusher's anchor is drawn from the whole table and skipped where any base is within 9" of an enemy model (the distances the
/// army-book deployment rules change, GfAmbush: Infiltrate's 3", Repel Ambushers' 12"); next to a friendly Ambush Beacon it is drawn
/// within 6" of the beacon first, where no enemy distance holds. Under 3.6 a Scout (scout_distance: 3.6.0 p.15 "over 9” away from
/// non-Scout enemy units", GfEdition.ScoutClearance) is drawn in its zone and skipped the same way where any base is within 9" of a model
/// of an enemy unit that is not itself a Scout.
/// A unit the scenario anchors (ScenarioArmy.Anchors) goes there.
/// </summary>
public static class HandPlacement
{
    private const int Attempts = 40;
    private const int AmbushAttempts = 400;
    private const int MaxColumns = 5;
    /// <summary>Kept beyond p.13's 9", so a placement the server rests on terrain is still over it.</summary>
    private const double AmbushMargin = 0.25;

    /// <summary>The chooser a run places its units with, derived from the scenario's seed: Program and every test that
    /// reproduces a console run use this, so one seed names one game through every entry point.</summary>
    public static Chooser ChooserFor(ulong scenarioSeed) => new(scenarioSeed ^ 0x5EED);

    public static IReadOnlyList<ModelRelocation> IntoZone(
        Projection projection, CompiledScenario scenario, Guid teamId, Guid unitId, Chooser chooser)
    {
        var unit = projection.Units[unitId];
        var table = scenario.Board.Table.Size;
        var ordinal = scenario.Info.Teams.Single(t => t.Id == teamId).Ordinal;
        var shape = scenario.Board.Table.Zones.FirstOrDefault(z => z.TeamOrdinal == ordinal)?.Shape;

        var shelved = unit.Definition.Models.Where(m => projection.Models[m.Id].Location is OnShelf).ToList();
        if (shelved.Count == 0) return [];

        // One grid for the whole unit: the column count and spacing come from the unit, not from a model's index,
        // so no two cells coincide. The spacing is the widest base plus half an inch.
        var cols = Math.Min(shelved.Count, MaxColumns);
        var widest = shelved.Max(m => Math.Max(m.Base.A, m.Base.B));
        var spacing = widest + 0.5;

        var offsets = shelved.Select((_, i) => new Point2(i % cols * spacing, i / cols * spacing)).ToList();
        var occupied = projection.Models.Values.Where(m => m.Location is OnTable)
            .Select(m => new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement)).ToList();
        var army = scenario.Scenario.Armies[scenario.ParticipantIds.ToList().IndexOf(unit.ParticipantId)];   // the unit's own army: a team may seat several
        var anchor = army.Anchors.TryGetValue(unit.Definition.Ordinal, out var fixedAt) ? fixedAt
            : unit.Definition.Reserve is { Property: GfReserves.Reinforcement } ? EdgeAnchor(shelved, offsets, table, occupied, chooser) ?? ChooseAnchor(shelved, offsets, null, table, occupied, chooser)
            : projection.Scheduler.Round >= 1 && unit.Conditions.Contains(scenario.SetAsideCondition)
                ? BeaconAnchor(shelved, offsets, table, occupied, Beacons(projection, scenario, teamId), chooser)
                    ?? AmbushAnchor(shelved, offsets, table, occupied, Enemies(projection, scenario, teamId, GfAmbush.Reach(unit.Definition)), chooser) ?? ChooseAnchor(shelved, offsets, shape, table, occupied, chooser, scenario.Zones)
                : projection.Scheduler.Round < 1 && unit.Conditions.Contains(scenario.SetAsideCondition) && ScoutClearance(scenario) is > 0 and var clearance
                    ? ChooseAnchor(shelved, offsets, shape, table, occupied, chooser, scenario.Zones, ScoutEnemies(projection, scenario, teamId, clearance))
                : ChooseAnchor(shelved, offsets, shape, table, occupied, chooser, scenario.Zones);

        return shelved.Select((slot, i) => new ModelRelocation(slot.Id, new OnShelf(),
            new OnTable(new Placement(new Point3(anchor.X + offsets[i].X, anchor.Y + offsets[i].Y, 0), new Yaw(0))))).ToList();
    }

    /// <summary>Scan step of the anchor search once the random draws miss.</summary>
    private const double ScanStep = 0.25;

    /// <summary>
    /// A random anchor at which every base of the grid is on the table, fully inside the zone, touches no model on the table (2D: the server
    /// rests the new base on what is under it) and stands over each of <paramref name="enemies"/>' kept distance (a 3.6 Scout's); when every draw misses, the first such anchor of a 0.25" scan of the zone's bounding box; the
    /// zone's centre only when nothing fits (a placement the server then judges).
    /// </summary>
    public static Point2 ChooseAnchor(IReadOnlyList<ModelSlot> models, IReadOnlyList<Point2> offsets, string? shape, TableSize table,
        IReadOnlyList<PlacedModel> occupied, Chooser chooser, ZoneLegs? legs = null, IReadOnlyList<(PlacedModel Body, double Kept)>? enemies = null)
    {
        // Candidates are drawn from the zone's bounding box, so a thin zone is not missed 39 times in 40.
        var vertices = shape is null ? [] : ZoneStyles.Resolve(shape, table, legs).SelectMany(p => p.Vertices).ToList();
        var (minX, maxX) = vertices.Count == 0 ? (0, table.Width) : (vertices.Min(v => v.X), vertices.Max(v => v.X));
        var (minY, maxY) = vertices.Count == 0 ? (0, table.Length) : (vertices.Min(v => v.Y), vertices.Max(v => v.Y));
        bool Fits(Point2 candidate)
        {
            var placed = models.Select((m, i) => Placed(m, new Point2(candidate.X + offsets[i].X, candidate.Y + offsets[i].Y))).ToList();
            return placed.All(pm => Geometry.Table.ContainsBase(table, pm)) && !placed.Any(pm => occupied.Any(o => Distance.EdgeToEdge(pm, o) <= 1e-6))
                && (shape is null || placed.All(pm => ZoneStyles.BaseFullyWithin(shape, table, pm, legs)))
                && (enemies is null || placed.All(pm => enemies.All(e => Distance.EdgeToEdge(pm, e.Body) > e.Kept + AmbushMargin)));
        }
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var candidate = new Point2(minX + chooser.Double() * (maxX - minX), minY + chooser.Double() * (maxY - minY));
            if (Fits(candidate)) return candidate;
        }
        for (var y = minY; y <= maxY; y += ScanStep)
            for (var x = minX; x <= maxX; x += ScanStep)
                if (Fits(new Point2(x, y))) return new Point2(x, y);
        return new Point2((minX + maxX) / 2, (minY + maxY) / 2);
    }

    /// <summary>An anchor anywhere on the table at which every base is on the table, touches no model, and stands over 9" from every enemy model; null when none was drawn.</summary>
    private static Point2? AmbushAnchor(IReadOnlyList<ModelSlot> models, IReadOnlyList<Point2> offsets, TableSize table,
        IReadOnlyList<PlacedModel> occupied, IReadOnlyList<(PlacedModel Body, double Kept)> enemies, Chooser chooser)
    {
        for (var attempt = 0; attempt < AmbushAttempts; attempt++)
        {
            var candidate = new Point2(chooser.Double() * table.Width, chooser.Double() * table.Length);
            var placed = models.Select((m, i) => Placed(m, new Point2(candidate.X + offsets[i].X, candidate.Y + offsets[i].Y))).ToList();
            if (!placed.All(pm => Geometry.Table.ContainsBase(table, pm))) continue;
            if (placed.Any(pm => occupied.Any(o => Distance.EdgeToEdge(pm, o) <= 1e-6))) continue;
            if (placed.All(pm => enemies.All(e => Distance.EdgeToEdge(pm, e.Body) > e.Kept + AmbushMargin))) return candidate;
        }
        return null;
    }

    /// <summary>An anchor at which every base is on the table, touches no model and is within 6" of one friendly Ambush Beacon; null without a beacon or when none was drawn.</summary>
    private static Point2? BeaconAnchor(IReadOnlyList<ModelSlot> models, IReadOnlyList<Point2> offsets, TableSize table,
        IReadOnlyList<PlacedModel> occupied, IReadOnlyList<PlacedModel> beacons, Chooser chooser)
    {
        foreach (var beacon in beacons)
            for (var attempt = 0; attempt < AmbushAttempts; attempt++)
            {
                var at = beacon.Placement.Position.Horizontal;
                var candidate = new Point2(at.X + (chooser.Double() * 2 - 1) * BeaconReach, at.Y + (chooser.Double() * 2 - 1) * BeaconReach);
                var placed = models.Select((m, i) => Placed(m, new Point2(candidate.X + offsets[i].X, candidate.Y + offsets[i].Y))).ToList();
                if (!placed.All(pm => Geometry.Table.ContainsBase(table, pm))) continue;
                if (placed.Any(pm => occupied.Any(o => Distance.EdgeToEdge(pm, o) <= 1e-6))) continue;
                if (placed.All(pm => Distance.EdgeToEdge(pm, beacon) <= BeaconReach - AmbushMargin)) return candidate;
            }
        return null;
    }

    /// <summary>Reinforcement: an anchor at which every base is fully within 12" of one table edge (GfReserves.NearEdge) and touches no model; null when none was drawn.</summary>
    private static Point2? EdgeAnchor(IReadOnlyList<ModelSlot> models, IReadOnlyList<Point2> offsets, TableSize table,
        IReadOnlyList<PlacedModel> occupied, Chooser chooser)
    {
        for (var attempt = 0; attempt < AmbushAttempts; attempt++)
        {
            var candidate = new Point2(chooser.Double() * table.Width, chooser.Double() * table.Length);
            var placed = models.Select((m, i) => Placed(m, new Point2(candidate.X + offsets[i].X, candidate.Y + offsets[i].Y))).ToList();
            if (!placed.All(pm => Geometry.Table.ContainsBase(table, pm))) continue;
            if (placed.Any(pm => occupied.Any(o => Distance.EdgeToEdge(pm, o) <= 1e-6))) continue;
            if (placed.All(pm => GfReserves.NearEdge(table, pm))) return candidate;
        }
        return null;
    }

    /// <summary>Ambush Beacon: "if they are deployed within 6" of this model".</summary>
    private const double BeaconReach = 6;

    private static IReadOnlyList<PlacedModel> Beacons(Projection projection, CompiledScenario scenario, Guid teamId) =>
        projection.Models.Values
            .Where(m => m.Location is OnTable && m.Slot.Properties.Any(p => p.Name == GfAmbush.AmbushBeacon)
                && scenario.TeamOfParticipant.GetValueOrDefault(projection.Units[m.UnitId].ParticipantId) == teamId)
            .Select(m => new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement)).ToList();

    /// <summary>Each enemy model on the table with the distance an Ambusher of that reach keeps from it.</summary>
    private static IReadOnlyList<(PlacedModel Body, double Kept)> Enemies(Projection projection, CompiledScenario scenario, Guid teamId, double reach) =>
        projection.Models.Values
            .Where(m => m.Location is OnTable && scenario.TeamOfParticipant.GetValueOrDefault(projection.Units[m.UnitId].ParticipantId) != teamId)
            .Select(m => (new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement), GfAmbush.Kept(projection.Units[m.UnitId].Definition, reach))).ToList();

    /// <summary>The game's edition's Scout clearance (3.6.0 p.15 9"; 0 under 3.5.1); 0 for a game that is not Grimdark Future.</summary>
    private static double ScoutClearance(CompiledScenario scenario) =>
        scenario.Info.GameRules is RuleSets.Gf351 or RuleSets.Gf360 ? GfEdition.Of(scenario.Info.GameRules).ScoutClearance : 0;

    /// <summary>Each model on the table of an enemy unit that is not itself a Scout, with the clearance a 3.6 Scout keeps from it.</summary>
    private static IReadOnlyList<(PlacedModel Body, double Kept)> ScoutEnemies(Projection projection, CompiledScenario scenario, Guid teamId, double clearance) =>
        projection.Models.Values
            .Where(m => m.Location is OnTable && scenario.TeamOfParticipant.GetValueOrDefault(projection.Units[m.UnitId].ParticipantId) != teamId
                && !GfAmbush.Carries(projection.Units[m.UnitId].Definition, GfNames.Scout))
            .Select(m => (new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), ((OnTable)m.Location).Placement), clearance)).ToList();

    private static PlacedModel Placed(ModelSlot slot, Point2 at) =>
        new(new ModelBody(slot.Base, slot.Height), new Placement(new Point3(at.X, at.Y, 0), new Yaw(0)));
}
