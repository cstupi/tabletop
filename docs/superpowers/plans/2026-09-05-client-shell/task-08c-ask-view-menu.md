# Task 8c: `AskViewModel` and `MenuModel`

Part of Task 8; its files and interfaces are in [Task 8a](task-08a-view-model-tests.md).

- [ ] **Step 3: Write `AskViewModel.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum AskMode { Choose, PickPieces, Allocate, Assign, Move, TokenSpend, Confirm }

/// <summary>What the one Ask panel renders. Highlights are the pieces the legal set names.</summary>
public sealed record AskView(Ask Ask, AskMode Mode, IReadOnlyList<Guid> Highlights, IReadOnlyList<string> AnsweredTeams, bool AutoPass);

public static class AskViewModel
{
    public static AskMode ModeOf(LegalSet legal) => legal switch
    {
        Options => AskMode.Choose, PieceSelection => AskMode.PickPieces, Allocation => AskMode.Allocate,
        Assignment => AskMode.Assign, MoveRegion => AskMode.Move, TokenSpend => AskMode.TokenSpend, Confirmation => AskMode.Confirm,
        _ => throw new ArgumentOutOfRangeException(nameof(legal)),
    };

    /// <summary>
    /// The spend map narrowed to one team's own models. p.13's window addresses both teams — the caster's side spends for
    /// +1 and the other side for -1 — so <c>TokenSpend.MaxByModel</c> names models this player may not spend, and a spin box
    /// over one of them would be a control the player cannot use. The map itself discloses nothing: every projection already
    /// carries each model's <c>SpellTokens</c>. A model whose unit or participant this projection cannot resolve is not mine.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> SpendableBy(Projection p, GameInfo game, Guid team, TokenSpend legal) =>
        legal.MaxByModel.Where(kv => TeamOfModel(p, game, kv.Key) == team).ToDictionary(kv => kv.Key, kv => kv.Value);

    static Guid? TeamOfModel(Projection p, GameInfo game, Guid modelId) =>
        p.Models.GetValueOrDefault(modelId) is { } m && p.Units.GetValueOrDefault(m.UnitId) is { } unit
            ? game.Participants.FirstOrDefault(x => x.Id == unit.ParticipantId)?.TeamId
            : null;

    /// <summary>The first open Ask addressed to my team that my team has not answered. Null for a watcher: a participant with no team is never Asked. `myTeam` comes from the participant's row by way of `Welcome`, so a player who reconnects mid-Ask is addressed by it again (game flow → Teams are frozen at the flip); callers pass null only for a real watcher, never for a client that is mid-reconnect.</summary>
    public static AskView? Mine(Projection p, GameInfo game, Guid? myTeam, IReadOnlySet<string> muted)
    {
        if (myTeam is not { } team) return null;
        var open = p.OpenAsks.Values.OrderBy(a => a.Ask.Id).FirstOrDefault(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team));
        if (open is null) return null;
        var names = new Names(game, p);
        var ask = open.Ask;
        var highlights = ask.Legal switch
        {
            Options o => o.Choices.SelectMany(c => c.Pieces).Distinct().ToList(),
            PieceSelection s => s.Eligible, Allocation a => a.Targets, Assignment a => a.Targets, MoveRegion m => MoveHighlights(m),
            TokenSpend t => SpendableBy(p, game, team, t).Keys.ToList(),          // my own spenders only (p.13)
            _ => (IReadOnlyList<Guid>)[],
        };
        var autoPass = ask.WindowKind is { } kind && muted.Contains(kind) && ask.AllowPass;
        return new AskView(ask, ModeOf(ask.Legal), highlights, open.Answers.Keys.Select(names.Team).ToList(), autoPass);
    }

    /// <summary>
    /// The pieces a move Ask names: its models, plus the containers containment offers. An enterable container is a click
    /// target for the answer, and the container a unit is exiting anchors the region, so both are highlights — "the legal set
    /// from the server becomes the panel's choices and the table's highlights" (client shell → Asks and windows). An occupant
    /// has no node, so highlighting its model id is a no-op; the container's node is the one the player can see and click.
    /// </summary>
    static IReadOnlyList<Guid> MoveHighlights(MoveRegion m)
    {
        var ids = m.ModelIds.ToList();
        if (m.Containment is { } c) { ids.AddRange(c.Enterable); if (c.ExitingId is { } exiting) ids.Add(exiting); }
        return ids.Distinct().ToList();
    }

    /// <summary>The waiting indicator: which teams the other open Asks are addressed to.</summary>
    public static string? WaitingFor(Projection p, GameInfo game, Guid? myTeam)
    {
        var names = new Names(game, p);
        var teams = p.OpenAsks.Values.SelectMany(a => a.Ask.Teams.Where(t => !a.Answers.ContainsKey(t) && t != myTeam)).Distinct().Select(names.Team).ToList();
        return teams.Count == 0 ? null : string.Join(", ", teams);
    }

    /// <summary>
    /// What the one button of a Confirm Ask says. A step gate says Continue; the round boundary's end_round Ask (unframed, addressed to
    /// every team that has a participant) says what it does, because the round advances only when a player answers it (scheduler → There is no round limit).
    /// </summary>
    public static string ConfirmLabel(Ask ask) => ask.Question == AskQuestions.EndRound ? "End the round" : "Continue";

    /// <summary>Who on the team answered: the ask_answered event's actor.</summary>
    public static string? AnsweredBy(IReadOnlyList<GameEvent> log, Guid askId, Names names) =>
        log.LastOrDefault(e => e.Payload is AskAnswered a && a.AskId == askId) is { } e ? names.Participant(e.Actor) : null;
}
```

- [ ] **Step 4: Write `MenuModel.cs`**

```csharp
using System.Globalization;
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

    public static IReadOnlyList<MenuItem> ForMarker(Guid markerId) => [new("Remove", MenuInput.None, _ => new RemoveMarker(markerId))];

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
```

`Place on the table, here` / `Take out of the container, here` is a `TablePoint` item: `PieceMenu` (Task 15) waits for the next left click on the table and hands `Build` the wire-frame point as `"x,y,z"`. That is the "or back" and the "take out" half of the spec's list; the shelf and container items are the other half.

A shelf or container model has no node on the table to right-click. `OffTable` therefore lists the same item for each of one team's off-table models, labelled with the model's name, and the team panel's menu shows them (Task 15). `ForModel` and `OffTable` build the item through one private `BackOnTable`, and `Names.TeamOfModel` decides the team.

**"Put inside a container" and "Take out of the container, here" stay, beside the action-driven path.** The move Ask's `Containment` (Task 14) is how a player embarks or disembarks *as part of an action*, under the ruleset's own conditions — the transport in reach, not both in one activation, the exit inside 6". The menu items are the manual override the spec asks for by name (*"put inside a container or take out"*, client shell → The log, undo and the menu), and they are the only path when no Ask is open at all: a transport deploying with units inside (p.14: *"Transports may deploy with units inside"*, and deployment's Ask is a `Destinations` for the transport, not a containment offer), a correction after a misclick, or a game played under `warn`/`silent` enforcement where stepping outside the offer is how the players fix things. `MoveModels` is the same command either path sends, logged with the same actor.

