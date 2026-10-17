using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Run;

/// <summary>
/// Deployment's Ask names a unit; putting its models on the table is an ordinary MoveModels command, not an
/// Ask (scheduler → The turn is an Ask: "moving models by hand never ends one"). This is where the simulator
/// does that. It computes no rules outcome: the zone comes from the deployment_zone rows and
/// Tabletop.Geometry resolves the shape. A hand placement is judged for overlap (IGameRules.PlacementBreach, move_overlap, p.7
/// "physically impossible locations") and, before round one, for its zone (deploy_zone: p.6 fully within it, p.14 a Scout within 12" of it),
/// so an anchor is skipped where any base would touch a model on the table or leave the zone.
/// </summary>
public static class HandPlacement
{
    private const int Attempts = 40;
    private const int MaxColumns = 5;

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
        var anchor = ChooseAnchor(shelved, offsets, shape, table, occupied, chooser);

        return shelved.Select((slot, i) => new ModelRelocation(slot.Id, new OnShelf(),
            new OnTable(new Placement(new Point3(anchor.X + offsets[i].X, anchor.Y + offsets[i].Y, 0), new Yaw(0))))).ToList();
    }

    /// <summary>
    /// A random anchor at which every base of the grid is fully inside the zone and touches no model on the table (2D: the server rests
    /// the new base on what is under it); the zone-centre-ish fallback otherwise.
    /// </summary>
    private static Point2 ChooseAnchor(IReadOnlyList<ModelSlot> models, IReadOnlyList<Point2> offsets, string? shape, TableSize table,
        IReadOnlyList<PlacedModel> occupied, Chooser chooser)
    {
        // Candidates are drawn from the zone's bounding box, so a thin zone is not missed 39 times in 40.
        var vertices = shape is null ? [] : ZoneStyles.Resolve(shape, table).SelectMany(p => p.Vertices).ToList();
        var (minX, maxX) = vertices.Count == 0 ? (0, table.Width) : (vertices.Min(v => v.X), vertices.Max(v => v.X));
        var (minY, maxY) = vertices.Count == 0 ? (0, table.Length) : (vertices.Min(v => v.Y), vertices.Max(v => v.Y));
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var candidate = new Point2(minX + chooser.Double() * (maxX - minX), minY + chooser.Double() * (maxY - minY));
            var placed = models.Select((m, i) => Placed(m, new Point2(candidate.X + offsets[i].X, candidate.Y + offsets[i].Y))).ToList();
            if (!placed.All(pm => Geometry.Table.ContainsBase(table, pm))) continue;
            if (placed.Any(pm => occupied.Any(o => Distance.EdgeToEdge(pm, o) <= 1e-6))) continue;
            if (shape is null || placed.All(pm => ZoneStyles.BaseFullyWithin(shape, table, pm))) return candidate;
        }
        return new Point2((minX + maxX) / 2, (minY + maxY) / 2);
    }

    private static PlacedModel Placed(ModelSlot slot, Point2 at) =>
        new(new ModelBody(slot.Base, slot.Height), new Placement(new Point3(at.X, at.Y, 0), new Yaw(0)));
}
