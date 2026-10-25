using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>What a disembark's facing is judged against: the Ask's exit distance and the board's volumes and size. Move judges it against the
/// volumes the Ask names Impassable, as the region it drew does.</summary>
public sealed record ExitSite(double Within, IReadOnlyList<WorldVolume> Volumes, TableSize Size);

/// <summary>The decision box's answers as messages: what the player's choice sends. The browser's decisionBox.ts builds the same.</summary>
public static class AskAnswers
{
    /// <summary>The wrapper: for the team the Ask was rendered for, not the acting team at send time.</summary>
    public static AnswerAsk Answer(Ask ask, AskAnswer answer, Guid? team) => new(ask.Id, answer, team);

    /// <summary>What a table click names in a legal set, which holds model ids or unit ids: the clicked id itself, its unit, or the set's model of the clicked unit.</summary>
    public static Guid? Resolve(Projection p, Guid clicked, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Contains(clicked)) return clicked;
        if (p.Models.GetValueOrDefault(clicked)?.UnitId is { } unit && ids.Contains(unit)) return unit;
        if (p.Units.ContainsKey(clicked)) foreach (var id in ids) if (p.Models.GetValueOrDefault(id)?.UnitId == clicked) return id;
        return null;
    }

    /// <summary>A click on a piece exactly one option names answers that option; none or several answer nothing.</summary>
    public static Chosen? ChosenByClick(Options options, Projection p, Guid clicked)
    {
        var hits = options.Choices.Where(c => Resolve(p, clicked, c.Pieces) is not null).ToList();
        return hits.Count == 1 ? new Chosen(hits[0].Id) : null;
    }

    public static TokensSpent Spent(IReadOnlyDictionary<Guid, int> values) =>
        new(values.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value));

    /// <summary>The facing the answer carries. A model on the table takes its ghost's facing. A disembarking model takes whatever fits at
    /// the point it was dropped (p.7), from the same search that drew the region; the container's facing is the fallback.</summary>
    public static Yaw FacingOf(Projection p, Guid modelId, Yaw? ghostYaw, Point2 dropped, ExitSite? exit) => p.Models[modelId].Location switch
    {
        OnTable on => ghostYaw ?? on.Placement.Yaw,
        Inside i when p.Models.GetValueOrDefault(i.ContainerId)?.Location is OnTable c =>
            exit is null ? c.Placement.Yaw
                : MoveRuler.ExitYawAt(BodyOf(p, modelId), new PlacedModel(BodyOf(p, i.ContainerId), c.Placement), exit.Within, dropped, exit.Volumes, exit.Size) ?? c.Placement.Yaw,
        OnShelf => ghostYaw ?? new Yaw(0),                                         // a reserve placed from the shelf faces as its ghost does (Q/E)
        _ => new Yaw(0),
    };

    /// <summary>One relocation per model of the region, in its order, from where it stands to its ghost.</summary>
    public static Destinations Move(Projection p, MoveRegion legal, IReadOnlyDictionary<Guid, Point3> proposed,
        IReadOnlyDictionary<Guid, Yaw> ghostYaws, bool continues, ExitSite? exit)
    {
        if (exit is not null) exit = exit with { Volumes = exit.Volumes.Where(v => legal.ImpassableVolumeIds.Contains(v.Id)).ToList() };
        var moves = legal.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location,
            new OnTable(new Placement(proposed[id], FacingOf(p, id, ghostYaws.TryGetValue(id, out var y) ? y : null, proposed[id].Horizontal, exit))))).ToList();
        return new Destinations(moves, continues);
    }

    /// <summary>p.15: every model of the region goes Inside the container.</summary>
    public static Destinations Embark(Projection p, MoveRegion legal, Guid container) =>
        new(legal.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new Inside(container))).ToList());

    static ModelBody BodyOf(Projection p, Guid modelId) { var m = p.Models[modelId]; return new ModelBody(m.Slot.Base, m.Slot.Height); }
}
