using System.Globalization;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum MenuInput { None, Integer, Text, PieceId, TablePoint, Choice }

/// <summary>One menu entry: a label, what it asks the player for, and the command it becomes. Null from Build means the input did not parse.
/// A setup item builds a setup edit instead (<see cref="Edit"/>), and its Build is null.</summary>
public sealed record MenuItem(string Label, MenuInput Input, Func<string?, Command?> Build)
{
    public Func<string?, Edit?>? Edit { get; init; }
    /// <summary>The theme icon the menu draws beside the label (lock, unlock, remove, army, undo); null for none.</summary>
    public string? Icon { get; init; }
    /// <summary>A Choice item's labels; the chosen one is its input.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];
    public static MenuItem OfEdit(string label, MenuInput input, Func<string?, Edit?> edit) => new(label, input, _ => null) { Edit = edit };
}

/// <summary>Every property is editable, each item one ordinary command through the same path automation uses. No admin mode.</summary>
public static class MenuModel
{
    static int? Int(string? s) => int.TryParse(s, out var n) ? n : null;
    static Guid? Id(string? s) => Guid.TryParse(s, out var g) ? g : null;
    /// <summary>A TablePoint item's input: the wire-frame point the player clicked, as "x,y,z".</summary>
    static Point3? Point(string? s) => s?.Split(',') is [var x, var y, var z]
        && double.TryParse(x, CultureInfo.InvariantCulture, out var px) && double.TryParse(y, CultureInfo.InvariantCulture, out var py)
        && double.TryParse(z, CultureInfo.InvariantCulture, out var pz) ? new Point3(px, py, pz) : null;

    public static IReadOnlyList<MenuItem> ForUnit(Projection p, Guid unitId, IReadOnlyList<string> actionNames, IReadOnlyList<GrantChoice>? grantable = null)
    {
        var items = new List<MenuItem>();
        foreach (var a in actionNames) items.Add(new($"Mark as having taken {a}", MenuInput.None, _ => new RecordAction(unitId, a)));
        items.Add(new("Clear the action record", MenuInput.None, _ => new ClearActionRecord(unitId)) { Icon = "undo" });
        items.Add(new("Add a condition", MenuInput.Text, s => string.IsNullOrWhiteSpace(s) ? null : new SetCondition(new Subject(SubjectKind.Unit, unitId), s.Trim(), true)));
        foreach (var c in p.Units.GetValueOrDefault(unitId)?.Conditions ?? new HashSet<string>())
            items.Add(new($"Remove {c}", MenuInput.None, _ => new SetCondition(new Subject(SubjectKind.Unit, unitId), c, false)) { Icon = "remove" });
        items.Add(new("Set wounds this activation", MenuInput.Integer, s => Int(s) is { } n ? new SetWoundsThisActivation(unitId, n) : null));
        if (grantable is { Count: > 0 } choices)                        // one use, the unit as its own grantor: a hand edit like any other
            items.Add(new("Add a grant", MenuInput.Choice, s => choices.FirstOrDefault(c => c.Label == s) is { } c
                ? new AddGrant(unitId, new Grant(Guid.CreateVersion7(), c.EffectId, c.EntryName, unitId, 1)) : null) { Choices = choices.Select(c => c.Label).ToList() });
        foreach (var g in p.Units.GetValueOrDefault(unitId)?.Grants ?? [])
            items.Add(new($"Remove grant {g.EntryName}", MenuInput.None, _ => new RemoveGrant(unitId, g.Id)) { Icon = "remove" });
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
        foreach (var c in model.Conditions) items.Add(new($"Remove {c}", MenuInput.None, _ => new SetCondition(new Subject(SubjectKind.Model, modelId), c, false)) { Icon = "remove" });
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

    /// <summary>The condition the server reads as a unit kept off the deployment (setup phase → Ambush and Scout).</summary>
    public const string SetAside = "set-aside";

    /// <summary>
    /// The roster's right-click on a unit of my team while playing. Every model on the shelf unwounded (never deployed): Set aside / Return to
    /// deployment when the server names it (ModelFacts.MaySetAside: Ambush, Scout and what counts as having them), then Put in transport per Transport model the server names for it (ModelFacts.DeployInside;
    /// passengers spec). Inside a container still on the shelf: Take out of transport. The server judges each hand move.
    /// </summary>
    public static IReadOnlyList<MenuItem> ForRosterUnit(GameInfo game, Projection p, Guid unitId, Guid? myTeam, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? deployInside = null,
        IReadOnlySet<Guid>? maySetAside = null)
    {
        if (game.Status != GameStatus.Playing || myTeam is null || p.Units.GetValueOrDefault(unitId) is not { } u) return [];
        if (game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId != myTeam) return [];
        var models = p.Models.Values.Where(m => m.UnitId == unitId).ToList();
        if (models.Count > 0 && models.All(m => m.Location is Inside i && p.Models.GetValueOrDefault(i.ContainerId)?.Location is OnShelf))
            return [new("Take out of transport", MenuInput.None, _ => new MoveModels(models.Select(m => new ModelRelocation(m.Id, m.Location, new OnShelf())).ToList())) { Icon = "army" }];
        if (models.Any(m => m.Location is not OnShelf || m.Wounds > 0)) return [];   // wounded on the shelf: removed, not waiting
        var items = new List<MenuItem>();
        if (maySetAside?.Contains(unitId) == true)
        {
            var aside = u.Conditions.Contains(SetAside);
            items.Add(new(aside ? "Return to deployment" : "Set aside", MenuInput.None, _ => SetAsideView.Toggle(unitId, !aside)));
        }
        foreach (var c in deployInside?.GetValueOrDefault(unitId) ?? [])
            if (p.Models.GetValueOrDefault(c) is { } hull && p.Units.GetValueOrDefault(hull.UnitId) is { } transport)
                items.Add(new($"Put in transport: {transport.Definition.Name}", MenuInput.None,
                    _ => new MoveModels(models.Select(m => new ModelRelocation(m.Id, m.Location, new Inside(c))).ToList())) { Icon = "army" });
        return items;
    }

    /// <summary>
    /// The table's right-click on a model my open move Ask moves, while it offers containers to enter (MoveContainment.Enterable: the rules
    /// decide reach; p.15 "only one model needs to reach the Transport for everyone to embark"): Board transport per container, answering
    /// the Ask with every model inside it, as a click on the container does.
    /// </summary>
    public static IReadOnlyList<MenuItem> ForBoarding(Projection p, Ask? ask, Guid modelId)
    {
        if (ask?.Legal is not MoveRegion { Containment.Enterable: { Count: > 0 } enterable } region || !region.ModelIds.Contains(modelId)) return [];
        return enterable.Where(p.Models.ContainsKey).Select(c => new MenuItem($"Board transport: {p.Units[p.Models[c].UnitId].Definition.Name}", MenuInput.None,
            _ => new AnswerAsk(ask.Id, AskAnswers.Embark(p, region, c))) { Icon = "army" }).ToList();
    }

    public static IReadOnlyList<MenuItem> ForTeam(GameInfo game, Projection p, Guid teamId) =>
    [
        new("Set score", MenuInput.Integer, s => Int(s) is { } n ? new SetScore(teamId, n) : null),
        new("Make this the active team", MenuInput.None, _ => new SetActiveTeam(teamId)),
        new("Set the round", MenuInput.Integer, s => Int(s) is { } n ? new SetRound(n) : null),
    ];

    /// <summary>
    /// Any objective can be given to a team or made neutral: the override of who controls it (objectives spec → The menu). With locks,
    /// an objective in play ends with its local Unlock or Lock item.
    /// </summary>
    public static IReadOnlyList<MenuItem> ForMarker(GameInfo game, Projection p, Guid markerId, MarkerLocks? locks = null)
    {
        var items = new List<MenuItem>();
        if (p.Board.Table.Markers.All(m => m.Id != markerId)) return items;
        foreach (var team in game.Teams.OrderBy(t => t.Ordinal))
            items.Add(new($"Give to Team {team.Ordinal}", MenuInput.None, _ => new SetObjectiveControl(markerId, team.Id)));
        items.Add(new("Make neutral", MenuInput.None, _ => new SetObjectiveControl(markerId, null)));
        if (locks?.ItemFor(game.Status, p, markerId) is { } lockItem) items.Add(lockItem);
        return items;
    }

    /// <summary>Setup: a draft terrain piece's or setup marker's removal; null for any other id.</summary>
    public static SetupEdit? RemoveFromDraft(TableState draft, Guid id) =>
        draft.Terrain.Any(t => t.Id == id) ? new RemoveTerrain(id) : draft.Markers.Any(m => m.Id == id) ? new RemoveSetupMarker(id) : null;

    /// <summary>Setup's right-click on a terrain piece or setup marker: a terrain piece turns 15° either way or to a facing typed in degrees, then Remove.</summary>
    public static IReadOnlyList<MenuItem> ForSetupPiece(TableState draft, Guid id)
    {
        if (RemoveFromDraft(draft, id) is not { } remove) return [];
        var items = new List<MenuItem>();
        if (draft.Terrain.FirstOrDefault(t => t.Id == id) is { } piece)
        {
            items.Add(MenuItem.OfEdit("Turn left 15°", MenuInput.None, _ => TurnInDraft(draft, id, piece.Yaw.Degrees + TerrainTurn)));
            items.Add(MenuItem.OfEdit("Turn right 15°", MenuInput.None, _ => TurnInDraft(draft, id, piece.Yaw.Degrees - TerrainTurn)));
            items.Add(MenuItem.OfEdit("Set facing…", MenuInput.Integer, s => Int(s) is { } d ? TurnInDraft(draft, id, d) : null));
        }
        items.Add(MenuItem.OfEdit("Remove", MenuInput.None, _ => remove) with { Icon = "remove" });
        return items;
    }

    /// <summary>A terrain piece's turn step, as a held model's (Q/E).</summary>
    public const double TerrainTurn = 15;

    /// <summary>A terrain piece turned in place to the facing (degrees, counter-clockwise from above, kept in 0–360): where it stands, or the
    /// nearest spot clear of the pieces declared Impassable it would swing into. Null for no such piece.</summary>
    public static MoveTerrain? TurnInDraft(TableState draft, Guid id, double degrees)
    {
        if (!draft.Terrain.Any(t => t.Id == id) || !double.IsFinite(degrees)) return null;
        var yaw = new Yaw((degrees % 360 + 360) % 360);
        return new MoveTerrain(id, DragBodies.Turn(draft.Terrain, id, yaw), yaw);
    }

    /// <summary>Abandon the top frame: pops it and everything above; applied effects stay applied.</summary>
    public static IReadOnlyList<MenuItem> ForFrame(Projection p) =>
        p.Stack.Count == 0 ? [] : [new($"Abandon {p.Stack[^1].Sequence}", MenuInput.None, _ => new AbandonSequence(p.Stack[^1].Id)) { Icon = "undo" }];

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
