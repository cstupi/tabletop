using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A move measured once while the game loads (frame drops plan → Third round): the first unit on the table moved 2" of a 6" move, as a
/// move Ask's render and its first drag frame measure it (its regions, the lead's landing, the contacts, the rulers, the gate and the bar). .NET
/// compiles that code then, so the first move Ask of the session does not. Reads the projection; changes nothing.</summary>
public static class MoveWarmup
{
    public const double Allowance = 6;

    /// <summary>A move of every on-table model of the first unit with one on the table; null when none stands there.</summary>
    public static MoveRegion? Legal(Projection p)
    {
        var lead = p.Models.Values.Where(m => m.Location is OnTable).OrderBy(m => m.Id).FirstOrDefault();
        if (lead is null) return null;
        var ids = p.Models.Values.Where(m => m.UnitId == lead.UnitId && m.Location is OnTable).OrderBy(m => m.Slot.Ordinal).Select(m => m.Id).ToList();
        return new MoveRegion(ids, Allowance, [], [], null);
    }

    /// <summary>One drag frame of the warm-up move: the lead held 2" east of where it stands. Null when nothing stands on the table.</summary>
    public static (MoveGate Gate, IReadOnlyDictionary<Guid, Contact> Contacts)? Run(Projection p, IReadOnlyList<WorldVolume> volumes, TableSize size)
    {
        if (Legal(p) is not { } legal) return null;
        var regions = MoveRegions.Of(p, legal, volumes, size);
        var lead = p.Models[legal.ModelIds[0]];
        var start = ((OnTable)lead.Location).Placement;
        _ = regions.Bands(lead.Id);
        _ = regions.Area([.. legal.ModelIds.Take(2)]);                        // two models' areas as one (MoveReach.Union), as a pickup draws them
        var target = regions.Clamp(lead.Id, start.Position.Horizontal + new Point2(2, 0));
        var held = new HeldBody(lead.Id, new PlacedModel(new ModelBody(lead.Slot.Base, lead.Slot.Height), start), target);
        var stationary = DragBodies.Stationary(p, [lead.Id]);
        var ends = DragBodies.Models([held], stationary, volumes, legal, free: false, level: null, within: (id, at) => regions.Reaches(id, at));
        var proposed = legal.ModelIds.ToDictionary(id => id, id => id == lead.Id ? start with { Position = ends[id].Lands } : ((OnTable)p.Models[id].Location).Placement);
        var unit = regions.Allowance(proposed.ToDictionary(kv => kv.Key, kv => kv.Value.Position.Horizontal));
        var contacts = ContactCheck.Move(p, legal, proposed, volumes, size, unit, regions);
        var readouts = legal.ModelIds.Select(id => MoveRuler.Read(regions.Route(id, proposed[id].Position.Horizontal)?.Distance ?? 0, unit,
            regions.Minimum(id, proposed[id].Position.Horizontal))).ToList();
        _ = DecisionCard.Bar(readouts, unit, regions.Minimum(lead.Id));
        return (MoveRuler.Gate(readouts, Enforcement.Strict), contacts);
    }
}
