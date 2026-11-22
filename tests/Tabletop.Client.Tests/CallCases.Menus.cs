using System.Globalization;
using System.Text.Json.Nodes;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public static partial class CallCases
{
    /// <summary>A deploy pick's placement: the state before its MoveModels from the shelf, the click and the drags.</summary>
    static IEnumerable<CallCase> Deployments()
    {
        var counts = new Dictionary<string, int>();
        foreach (var name in LogNames)
        {
            var log = Log(name);
            var selfPlay = name == "calls/selfplay";
            var opened = log.Events.Where(e => e.Payload is AskOpened).ToDictionary(e => ((AskOpened)e.Payload).Ask.Id, e => ((AskOpened)e.Payload).Ask);
            foreach (var closed in log.Events.Where(e => e.Payload is AskClosed c && DeployQuestions.Contains(opened[c.AskId].Question)))
            {
                var close = (AskClosed)closed.Payload;
                var ask = opened[close.AskId];
                if (close.Answers.FirstOrDefault(a => a.Answer is SelectedPieces { PieceIds.Count: 1 }) is not { } ta) continue;
                var unit = ((SelectedPieces)ta.Answer).PieceIds[0];
                var moved = log.Events.FirstOrDefault(e => e.Seq > closed.Seq && e.Payload is ModelsMoved m && m.Moves.Count > 0 && m.Moves.All(r => r.From is OnShelf && r.To is OnTable));
                if (moved is null) continue;
                var moves = ((ModelsMoved)moved.Payload).Moves;
                var upTo = moved.Seq - 1;
                var player = PlayerOf(log.Game, ta.TeamId);
                var probe = CallState.Playing(log, upTo, player);
                if (!moves.All(r => probe.P!.Models.GetValueOrDefault(r.ModelId)?.UnitId == unit)) continue;
                if (new[] { probe.Acting }.Concat(probe.MyTeams.Cast<Guid?>()).Select(t => DeployView.MidDeploy(probe.P!, probe.Events, t)).FirstOrDefault(m => m is not null) is not { } mid) continue;
                var at = ((OnTable)moves[0].To).Placement.Position;
                var logged = moves.ToDictionary(r => r.ModelId, r => ((OnTable)r.To).Placement);

                IReadOnlyList<(Guid, Placement)> Ghosts(CallState s, bool dragged)
                {
                    var shelved = s.P!.Models.Values.Where(m => m.UnitId == unit && m.Location is OnShelf).OrderBy(m => m.Slot.Ordinal).ToList();
                    var layout = DeployView.Layout(shelved, at, s.Log.Board.Table.Size);
                    return layout.Select(r => (r.ModelId, dragged && logged.TryGetValue(r.ModelId, out var pl) ? pl : ((OnTable)r.To).Placement)).ToList();
                }
                JsonNode Place(CallState s, bool dragged) => Command(DeployView.Place(s.P!, Ghosts(s, dragged)));
                var ambusher = ask.Question == AskQuestions.DeployAmbusher;
                foreach (var (kind, dragged) in new[] { ("deploy/click", false), ("deploy/ghosts", true) })
                {
                    var full = ambusher && dragged ? "deploy/ambusher" : kind;
                    if (ambusher && !dragged) continue;
                    var key = $"{name}|{ask.Question}|{full}";
                    if ((counts[key] = counts.GetValueOrDefault(key) + 1) > 2) continue;
                    var choice = Choice("deploy", ("at", J(at)));
                    if (dragged) choice["ghosts"] = J(logged);
                    yield return Playing(selfPlay ? "selfplay/deploy" : full, log, upTo, player, choice, s => Place(s, dragged), suffix: selfPlay ? full : "");
                }
            }
        }
    }

    // ---- menus ----

    static MenuContext Ctx(CallCase c) => new(c.State.Game, c.State.P!, c.State.Events, false, c.State.Acting, c.State.Mine, c.Grantable,
        c.DeployInside, c.MaySetAside, new MarkerLocks());

    /// <summary>One menu case: the menu `open` opens, the item `label`, the player's `input`; the labels are the decision, the item's command the message.</summary>
    static CallCase Menu(string kind, CallLog log, long upTo, Guid player, JsonObject open, string label, string? input,
        Func<MenuContext, IReadOnlyList<MenuItem>> items, string suffix = "", IReadOnlyList<GrantChoice>? grantable = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? deployInside = null, IReadOnlySet<Guid>? maySetAside = null)
    {
        var choice = Choice("menu", ("open", open), ("label", label));
        if (input is not null) choice["input"] = input;
        var made = Playing(kind, log, upTo, player, choice, _ => null, suffix: suffix, grantable: grantable, deployInside: deployInside, maySetAside: maySetAside);
        var shown = items(Ctx(made));
        made.Decisions!["menu"] = J(shown.Select(i => i.Label).ToList());
        var item = shown.FirstOrDefault(i => i.Label == label) ?? shown.First(i => i.Label.StartsWith(label, StringComparison.Ordinal));
        choice["label"] = item.Label;
        made.Message = item.Build(input) is { } command ? Command(command) : null;
        return made;
    }

    static IEnumerable<CallCase> MenuCases()
    {
        // The first state after deployment: an activate Ask is open.
        foreach (var name in new[] { "skirmish", "calls/objectives" })
        {
            var log = Log(name);
            var open = log.Events.First(e => e.Payload is AskOpened { Ask.Question: AskQuestions.Activate });
            var ask = ((AskOpened)open.Payload).Ask;
            var team = ask.Teams[0];
            var player = PlayerOf(log.Game, team);
            var state = CallState.Playing(log, open.Seq, player);
            var unit = state.P!.Units.Values.First(u => TeamOfUnit(state, u.Id) == team);
            var model = state.P.Models.Values.First(m => m.UnitId == unit.Id && m.Location is OnTable);
            var other = log.Game.Teams.First(t => t.Id != team);

            if (name == "skirmish")
            {
                JsonObject Piece(bool shift) => Obj(("piece", S(model.Id)), ("shift", shift));
                foreach (var input in new[] { "3", " 3 ", })
                    yield return Menu("menu/model", log, open.Seq, player, Piece(false), "Set wounds", input, c => Menus.Piece(c, model.Id, false), suffix: $"wounds {input.Replace(' ', '_')}");
                foreach (var input in new[] { "3.5", "abc" })
                    yield return Menu("menu/invalidInput", log, open.Seq, player, Piece(false), "Set wounds", input, c => Menus.Piece(c, model.Id, false), suffix: $"wounds {input}");
                yield return Menu("menu/model", log, open.Seq, player, Piece(false), "Add a condition", "Pinned", c => Menus.Piece(c, model.Id, false), suffix: "condition");
                yield return Menu("menu/invalidInput", log, open.Seq, player, Piece(false), "Add a condition", "  ", c => Menus.Piece(c, model.Id, false), suffix: "condition blank");
                yield return Menu("menu/unit", log, open.Seq, player, Piece(true), "Set wounds this activation", "2", c => Menus.Piece(c, model.Id, true), suffix: "activation wounds");
                yield return Menu("menu/unit", log, open.Seq, player, Piece(true), "Clear the action record", null, c => Menus.Piece(c, model.Id, true), suffix: "clear");
                yield return Menu("menu/unit", log, open.Seq, player, Piece(true), "Move the unit to the shelf", null, c => Menus.Piece(c, model.Id, true), suffix: "shelf");
                var grant = new GrantChoice("Test grant", "Test entry", Guid.Parse("019a0000-0000-7000-8000-0000000000c1"));
                yield return Menu("menu/grant", log, open.Seq, player, Piece(true), "Add a grant", "Test grant", c => Menus.Piece(c, model.Id, true), grantable: [grant]);
                yield return Menu("menu/invalidInput", log, open.Seq, player, Piece(true), "Add a grant", "Other grant", c => Menus.Piece(c, model.Id, true), suffix: "grant unknown", grantable: [grant]);
                var teamOpen = Obj(("team", S(team)));
                yield return Menu("menu/team", log, open.Seq, player, teamOpen, "Set score", "3", c => Menus.Team(c, team), suffix: "score");
                yield return Menu("menu/team", log, open.Seq, player, teamOpen, "Make this the active team", null, c => Menus.Team(c, team), suffix: "active");
                yield return Menu("menu/team", log, open.Seq, player, teamOpen, "Set the round", "2", c => Menus.Team(c, team), suffix: "round");
                yield return Menu("menu/invalidInput", log, open.Seq, player, teamOpen, "Set the round", "x", c => Menus.Team(c, team), suffix: "round text");

                // Abandon: the first state with a frame open.
                var applier = new EventApplier(log.Board, log.Game);
                foreach (var e in log.Events)
                {
                    applier.Apply(e);
                    if (e.Seq <= open.Seq || applier.Projection.Stack.Count == 0) continue;
                    var seq = e.Seq;
                    var p2 = CallState.Playing(log, seq, player);
                    var m2 = p2.P!.Models.Values.First(m => m.Location is OnTable && TeamOfUnit(p2, m.UnitId) == team);
                    yield return Menu("menu/abandon", log, seq, player, Obj(("piece", S(m2.Id)), ("shift", false)), "Abandon", null, c => Menus.Piece(c, m2.Id, false));
                    break;
                }

                // A model off the table: the team menu's back-on-table item takes the clicked point.
                var early = log.Events.First(e => e.Payload is AskOpened);
                var p0 = CallState.Playing(log, early.Seq, player);
                var shelved = p0.P!.Models.Values.First(m => m.Location is OnShelf && TeamOfUnit(p0, m.UnitId) == team);
                yield return Menu("menu/model", log, early.Seq, player, Obj(("team", S(team))), "Place " + p0.Names.Model(shelved.Id) + " on the table, here", "10.5,12,0",
                    c => Menus.Team(c, team), suffix: "back on table");
            }
            else
            {
                var marker = state.P.Board.Table.Markers.First(m => m.Kind == Tabletop.Content.MarkerKind.Objective);
                var open2 = Obj(("piece", S(marker.Id)), ("shift", false));
                yield return Menu("menu/marker", log, open.Seq, player, open2, "Give to Team 1", null, c => Menus.Piece(c, marker.Id, false), suffix: "give");
                yield return Menu("menu/marker", log, open.Seq, player, open2, "Make neutral", null, c => Menus.Piece(c, marker.Id, false), suffix: "neutral");
                yield return Menu("menu/lock", log, open.Seq, player, open2, "Unlock", null, c => Menus.Piece(c, marker.Id, false));
            }
            _ = other;
        }

        // The roster menu of a set-aside unit, before and after.
        {
            var log = Log("calls/ambush");
            var set = log.Events.First(e => e.Payload is ConditionSet { Condition: "set-aside", Present: true });
            var unit = ((ConditionSet)set.Payload).Subject.Id;
            var state = CallState.Playing(log, set.Seq - 1, log.Game.Participants.First().Id);
            var player = PlayerOf(log.Game, TeamOfUnit(state, unit)!.Value);
            IReadOnlySet<Guid> aside = new HashSet<Guid> { unit };                                    // ModelFacts.MaySetAside names it
            yield return Menu("menu/setAside", log, set.Seq - 1, player, Obj(("roster", S(unit))), "Set aside", null, c => Menus.Roster(c, unit), maySetAside: aside);
            yield return Menu("menu/return", log, set.Seq, player, Obj(("roster", S(unit))), "Return to deployment", null, c => Menus.Roster(c, unit), maySetAside: aside);
            // The deploy pick's Set aside beside the unit sends what the roster menu sends.
            yield return Playing("pick/setAside", log, set.Seq - 1, player, Choice("asideButton", ("unitId", S(unit))),
                s => SetAsideView.SetAside(s.P!, unit, aside) is { } cmd ? Command(cmd) : null, maySetAside: aside);
        }

        // A unit put in a transport before it deploys, and taken out again.
        {
            var log = Log("calls/passengers");
            var put = log.Events.First(e => e.Payload is ModelsMoved m && m.Moves.Any(r => r.From is OnShelf && r.To is Inside));
            var moves = ((ModelsMoved)put.Payload).Moves.Where(r => r.To is Inside).ToList();
            var state = CallState.Playing(log, put.Seq, log.Game.Participants.First().Id);
            var unit = state.P!.Models[moves[0].ModelId].UnitId;
            var container = ((Inside)moves[0].To).ContainerId;
            var player = PlayerOf(log.Game, TeamOfUnit(state, unit)!.Value);
            IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> inside = new Dictionary<Guid, IReadOnlyList<Guid>> { [unit] = [container] };
            yield return Menu("menu/putIn", log, put.Seq - 1, player, Obj(("roster", S(unit))), "Put in transport", null, c => Menus.Roster(c, unit), deployInside: inside);
            yield return Menu("menu/takeOut", log, put.Seq, player, Obj(("roster", S(unit))), "Take out of transport", null, c => Menus.Roster(c, unit), deployInside: inside);
        }

        // Board transport: a move Ask that offers containers.
        {
            var log = Log("calls/transport");
            var open = log.Events.First(e => e.Payload is AskOpened { Ask.Legal: MoveRegion { Containment.Enterable.Count: > 0 } });
            var ask = ((AskOpened)open.Payload).Ask;
            var region = (MoveRegion)ask.Legal;
            var player = PlayerOf(log.Game, ask.Teams[0]);
            yield return Menu("menu/boarding", log, open.Seq, player, Obj(("piece", S(region.ModelIds[0])), ("shift", false)), "Board transport", null,
                c => Menus.Piece(c, region.ModelIds[0], false));
        }
    }
}
