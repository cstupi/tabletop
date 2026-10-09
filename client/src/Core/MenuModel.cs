using System.Globalization;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum MenuInput { None, Integer, Text, PieceId, TablePoint }

/// <summary>One menu entry: a label, what it asks the player for, and the command it becomes. Null from Build means the input did not parse.</summary>
public sealed record MenuItem(string Label, MenuInput Input, Func<string?, Command?> Build);

/// <summary>Every property is editable, each item one ordinary command through the same path automation uses. No admin mode.</summary>
public static class MenuModel
{
    static int? Int(string? s) => int.TryParse(s, out var n) ? n : null;
    static Guid? Id(string? s) => Guid.TryParse(s, out var g) ? g : null;
    /// <summary>A TablePoint item's input: the wire-frame point the player clicked, as "x,y,z".</summary>
    static Point3? Point(string? s) => s?.Split(',') is [var x, var y, var z]
        && double.TryParse(x, CultureInfo.InvariantCulture, out var px) && double.TryParse(y, CultureInfo.InvariantCulture, out var py)
        && double.TryParse(z, CultureInfo.InvariantCulture, out var pz) ? new Point3(px, py, pz) : null;

    public static IReadOnlyList<MenuItem> ForUnit(Projection p, Guid unitId, IReadOnlyList<string> actionNames)
    {
        var items = new List<MenuItem>();
        foreach (var a in actionNames) items.Add(new($"Mark as having taken {a}", MenuInput.None, _ => new RecordAction(unitId, a)));
        items.Add(new("Mark as having taken…", MenuInput.Text, s => string.IsNullOrWhiteSpace(s) ? null : new RecordAction(unitId, s.Trim())));
        items.Add(new("Clear the action record", MenuInput.None, _ => new ClearActionRecord(unitId)));
        items.Add(new("Add a condition", MenuInput.Text, s => string.IsNullOrWhiteSpace(s) ? null : new SetCondition(new Subject(SubjectKind.Unit, unitId), s.Trim(), true)));
        foreach (var c in p.Units.GetValueOrDefault(unitId)?.Conditions ?? new HashSet<string>())
            items.Add(new($"Remove {c}", MenuInput.None, _ => new SetCondition(new Subject(SubjectKind.Unit, unitId), c, false)));
        items.Add(new("Set wounds this activation", MenuInput.Integer, s => Int(s) is { } n ? new SetWoundsThisActivation(unitId, n) : null));
        foreach (var g in p.Units.GetValueOrDefault(unitId)?.Grants ?? [])
            items.Add(new($"Remove grant {g.EntryName}", MenuInput.None, _ => new RemoveGrant(unitId, g.Id)));
        var models = p.Models.Values.Where(m => m.UnitId == unitId).ToList();
        items.Add(new("Move the unit to the shelf", MenuInput.None, _ => new MoveModels(models.Select(m => new ModelRelocation(m.Id, m.Location, new OnShelf())).ToList())));
        return items;
    }

    public static IReadOnlyList<MenuItem> ForModel(Projection p, Guid modelId, IReadOnlyList<string> actionNames)
    {
        var model = p.Models[modelId];
        var items = new List<MenuItem>
        {
            new("Set wounds", MenuInput.Integer, s => Int(s) is { } n ? new SetWounds(modelId, n) : null),
            new("Set spell tokens", MenuInput.Integer, s => Int(s) is { } n ? new SetSpellTokens(modelId, n) : null),
            new("Add a condition", MenuInput.Text, s => string.IsNullOrWhiteSpace(s) ? null : new SetCondition(new Subject(SubjectKind.Model, modelId), s.Trim(), true)),
            new("Move to the shelf", MenuInput.None, _ => new MoveModels([new ModelRelocation(modelId, model.Location, new OnShelf())])),
            new("Put inside a container", MenuInput.PieceId, s => Id(s) is { } c ? new MoveModels([new ModelRelocation(modelId, model.Location, new Inside(c))]) : null),
        };
        foreach (var c in model.Conditions) items.Add(new($"Remove {c}", MenuInput.None, _ => new SetCondition(new Subject(SubjectKind.Model, modelId), c, false)));
        // "move to the shelf or back", "put inside a container or take out": coming back is one relocation to OnTable,
        // and the position comes from the next click on the table (client shell -> the menu).
        if (model.Location is not OnTable)
            items.Add(BackOnTable(model, model.Location is Inside ? "Take out of the container, here" : "Place on the table, here"));
        items.AddRange(ForUnit(p, model.UnitId, actionNames).Where(i => i.Label.StartsWith("Mark") || i.Label == "Clear the action record"));
        return items;
    }

    static MenuItem BackOnTable(ModelInPlay model, string label) => new(label, MenuInput.TablePoint,
        s => Point(s) is { } at ? new MoveModels([new ModelRelocation(model.Id, model.Location, new OnTable(new Placement(at, new Yaw(0))))]) : null);

    /// <summary>A shelf or container model has no node to right-click, so the team panel lists each one: the click's table point places it.</summary>
    public static IReadOnlyList<MenuItem> OffTable(Projection p, Guid teamId, Names names) =>
        p.Models.Values.Where(m => m.Location is not OnTable && names.TeamOfModel(m.Id) == teamId)
            .Select(m => BackOnTable(m, m.Location is Inside ? $"Take {names.Model(m.Id)} out of the container, here" : $"Place {names.Model(m.Id)} on the table, here"))
            .ToList();

    public static IReadOnlyList<MenuItem> ForTeam(GameInfo game, Projection p, Guid teamId) =>
    [
        new("Set score", MenuInput.Integer, s => Int(s) is { } n ? new SetScore(teamId, n) : null),
        new("Make this the active team", MenuInput.None, _ => new SetActiveTeam(teamId)),
        new("Set the round", MenuInput.Integer, s => Int(s) is { } n ? new SetRound(n) : null),
    ];

    /// <summary>
    /// Remove a marker placed in play (a setup row is never removable, moved or not). Any objective, from setup or play, can be given to a
    /// team or made neutral: the override of who controls it (objectives spec → The menu). With locks, an objective in play
    /// ends with its local Unlock or Lock item.
    /// </summary>
    public static IReadOnlyList<MenuItem> ForMarker(GameInfo game, Projection p, Guid markerId, MarkerLocks? locks = null)
    {
        var items = new List<MenuItem>();
        if (p.Markers.ContainsKey(markerId) && p.Board.Table.Markers.All(m => m.Id != markerId)) items.Add(new("Remove", MenuInput.None, _ => new RemoveMarker(markerId)));
        var marker = p.Markers.GetValueOrDefault(markerId) ?? p.Board.Table.Markers.FirstOrDefault(m => m.Id == markerId);
        if (marker?.Kind != MarkerKind.Objective) return items;
        foreach (var team in game.Teams.OrderBy(t => t.Ordinal))
            items.Add(new($"Give to Team {team.Ordinal}", MenuInput.None, _ => new SetObjectiveControl(markerId, team.Id)));
        items.Add(new("Make neutral", MenuInput.None, _ => new SetObjectiveControl(markerId, null)));
        if (locks?.ItemFor(game.Status, p, markerId) is { } lockItem) items.Add(lockItem);
        return items;
    }

    /// <summary>Abandon the top frame: pops it and everything above; applied effects stay applied.</summary>
    public static IReadOnlyList<MenuItem> ForFrame(Projection p) =>
        p.Stack.Count == 0 ? [] : [new($"Abandon {p.Stack[^1].Sequence}", MenuInput.None, _ => new AbandonSequence(p.Stack[^1].Id))];

    /// <summary>The action names the client has seen: every action recorded in the log and each unit's current record. The rows themselves never reach the client,
    /// and the activate Ask's option labels are display text ("{unit name}: {action}"), never parsed.</summary>
    public static IReadOnlyList<string> KnownActionNames(Projection p, IReadOnlyList<GameEvent> log)
    {
        var names = new List<string>();
        names.AddRange(log.Select(e => e.Payload).OfType<ActionRecorded>().Select(a => a.Action));
        names.AddRange(p.Units.Values.SelectMany(u => u.Actions));
        return names.Distinct().ToList();
    }
}
