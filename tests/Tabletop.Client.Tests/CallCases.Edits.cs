using System.Text.Json.Nodes;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public static partial class CallCases
{
    // ---- undo ----

    static JsonArray Seqs(IEnumerable<long> seqs) => new(seqs.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());

    static IEnumerable<CallCase> UndoCases()
    {
        var skirmish = Log("skirmish");
        var player = PlayerOf(skirmish.Game, skirmish.Game.Teams[0].Id);
        var n = skirmish.Events.Count;
        foreach (var at in new[] { n / 4, n / 2, 3 * n / 4 })
            yield return Playing("undo/button", skirmish, skirmish.Events[at].Seq, player, Choice("undoButton"),
                s => UndoPlan.Of(s.Events, s.Names, CombatLog.Build(s.Events, s.Names)) is { } plan ? Command(new Undo(plan.ToSeq)) : null,
                (s, d) => d["undo"] = Obj(("toSeq", UndoPlan.Of(s.Events, s.Names)?.ToSeq)));

        JsonNode? Row(CallState s, long seq) =>
            CombatLog.Flatten(CombatLog.Build(s.Events, s.Names)).FirstOrDefault(e => e.Seq == seq) is { } e && CombatLog.UndoHere(e, false) ? Command(new Undo(seq)) : null;
        void Here(CallState s, JsonObject d) =>
            d["undo"] = Obj(("here", Seqs(CombatLog.Flatten(CombatLog.Build(s.Events, s.Names)).Where(e => CombatLog.UndoHere(e, false)).Select(e => e.Seq))));

        var mid = skirmish.Events[n / 2].Seq;
        var entries = CombatLog.Build(CallState.Playing(skirmish, mid, player).Events, CallState.Playing(skirmish, mid, player).Names);
        var line = CombatLog.Flatten(entries).First(e => e is LineEntry && !e.Undone);
        yield return Playing("undo/row", skirmish, mid, player, Choice("undoRow", ("seq", line.Seq)), s => Row(s, line.Seq), Here);

        var transport = Log("calls/transport");
        var tPlayer = PlayerOf(transport.Game, transport.Game.Teams[0].Id);
        var tMid = transport.Events[transport.Events.Count * 3 / 4].Seq;
        var tState = CallState.Playing(transport, tMid, tPlayer);
        var roll = CombatLog.Flatten(CombatLog.Build(tState.Events, tState.Names)).OfType<RollEntry>().First(r => !r.Undone);
        yield return Playing("undo/roll", transport, tMid, tPlayer, Choice("undoRow", ("seq", roll.Seq)), s => Row(s, roll.Seq), Here);

        // An undone row: the state with the Undo marker a player's Undo to an earlier seq appends.
        var to = skirmish.Events[n / 4].Seq;
        var undone = CallState.Playing(skirmish, mid, player, undoneTo: to);
        var gone = CombatLog.Flatten(CombatLog.Build(undone.Events, undone.Names)).First(e => e is LineEntry && e.Undone);
        yield return Playing("undo/undone", skirmish, mid, player, Choice("undoRow", ("seq", gone.Seq)), s => Row(s, gone.Seq), Here, undoneTo: to);
    }

    // ---- drops ----

    static JsonObject DropChoice(HeldDrop d) => Choice("drop", ("pieceId", S(d.PieceId)), ("modelIds", Ids(d.ModelIds)), ("grabs", J(d.Grabs)),
        ("current", J(d.Current)), ("yaws", J(d.Yaws)));

    static HeldDrop Along(Guid piece, IReadOnlyList<Guid> ids, IReadOnlyDictionary<Guid, Placement> from) => new(piece, ids, from,
        ids.ToDictionary(i => i, i => from[i].Position with { X = from[i].Position.X + 1 }), ids.ToDictionary(i => i, i => from[i].Yaw));

    /// <summary>The creator's row, or the row Welcome named (a seat with no team) as the other player.</summary>
    static Guid Who(CallLog l, bool creator) => creator ? l.Game.Participants.First(p => p.AccountId == l.Game.CreatedBy).Id : l.Welcome.ParticipantId;

    static CallCase TableCase(string kind, string log, GameStatus status, bool creator, JsonObject choice, Func<CallState, JsonNode?> build, string suffix = "",
        Action<CallState, JsonObject>? decide = null)
    {
        var l = Log(log);
        var state = CallState.Table(l, status, Who(l, creator));
        var c = new CallCase { Kind = kind, State = state, Choice = choice, UpTo = 0, Suffix = suffix, Creator = creator };
        var d = new JsonObject();
        decide?.Invoke(state, d);
        c.Decisions = d;
        c.Message = build(state);
        return c;
    }

    static IEnumerable<CallCase> DropCases()
    {
        var skirmish = Log("skirmish");
        var open = skirmish.Events.First(e => e.Payload is AskOpened { Ask.Question: AskQuestions.Activate });
        var team = ((AskOpened)open.Payload).Ask.Teams[0];
        var player = PlayerOf(skirmish.Game, team);
        var s0 = CallState.Playing(skirmish, open.Seq, player);
        var models = s0.P!.Models.Values.Where(m => TeamOfUnit(s0, m.UnitId) == team && m.Location is OnTable).Take(3).ToList();
        var held = Along(models[0].Id, models.Select(m => m.Id).ToList(), models.ToDictionary(m => m.Id, m => ((OnTable)m.Location).Placement));
        yield return Playing("drop/models", skirmish, open.Seq, player, DropChoice(held),
            s => HandMoves.Drop(GameStatus.Playing, s.P, null, held) is { Command: { } c } ? Command(c) : null);

        var objectives = Log("calls/objectives");
        var oOpen = objectives.Events.First(e => e.Payload is AskOpened { Ask.Question: AskQuestions.Activate });
        var oPlayer = PlayerOf(objectives.Game, ((AskOpened)oOpen.Payload).Ask.Teams[0]);
        var marker = objectives.Board.Table.Markers[0];
        var markerHeld = Along(marker.Id, [marker.Id], new Dictionary<Guid, Placement> { [marker.Id] = new Placement(marker.Position, marker.Yaw) });
        yield return Playing("drop/marker", objectives, oOpen.Seq, oPlayer, DropChoice(markerHeld),
            s => HandMoves.Drop(GameStatus.Playing, s.P, null, markerHeld) is { Command: { } c } ? Command(c) : null);

        var piece = Log("terrain").Board.Table.Terrain[0];
        var pieceHeld = Along(piece.Id, [piece.Id], new Dictionary<Guid, Placement> { [piece.Id] = new Placement(piece.Position, piece.Yaw) });
        yield return TableCase("drop/terrain", "terrain", GameStatus.Setup, true, DropChoice(pieceHeld),
            s => HandMoves.Drop(GameStatus.Setup, null, s.Draft, pieceHeld) is { Edit: { } e } ? EditMsg(e) : null);
        yield return TableCase("drop/setupMarker", "calls/objectives", GameStatus.Setup, true, DropChoice(markerHeld),
            s => HandMoves.Drop(GameStatus.Setup, null, s.Draft, markerHeld) is { Edit: { } e } ? EditMsg(e) : null);
    }

    // ---- lobby and setup ----

    static readonly Guid AssetId = Guid.Parse("019a0000-0000-7000-8000-0000000000a1");
    static readonly Guid ArmyId = Guid.Parse("019a0000-0000-7000-8000-0000000000b1");

    static IEnumerable<CallCase> EditCases()
    {
        IEnumerable<CallCase> Lobby(string log, GameStatus status, bool creator, string who)
        {
            CallCase E(string control, JsonObject extra, Func<CallState, Edit?> build, string suffix = "", Action<CallState, JsonObject>? decide = null)
            {
                var choice = Choice("edit", ("control", control));
                foreach (var (k, v) in extra) choice[k] = v?.DeepClone();
                return TableCase("edit/" + control, log, status, creator, choice, s => build(s) is { } e ? EditMsg(e) : null, $"{status.ToString().ToLowerInvariant()} {who} {suffix}".Trim(), decide);
            }
            var game = Log(log).Game;
            for (var i = 0; i < game.Teams.Count; i++)
            {
                var index = i;
                yield return E("teamButton", Obj(("team", index)), s => LobbyView.Teams(s.Game, s.ParticipantId, creator)[index] is { Enabled: true } t ? t.Edit : null, $"team {index + 1}",
                    (s, d) => { var t = LobbyView.Teams(s.Game, s.ParticipantId, creator)[index]; d["button"] = Obj(("text", t.Text), ("enabled", t.Enabled), ("leave", t.Leave)); });
            }
            yield return E("watch", Obj(), _ => LobbyEdits.Watch());
            yield return E("createTeam", Obj(), _ => LobbyEdits.CreateTeam());
            if (status == GameStatus.Lobby && creator)
            {
                foreach (var choice in new[] { 0, 2 })
                {
                    var target = game.Participants[0].Id;
                    yield return E("place", Obj(("participantId", S(target)), ("choice", choice)), s => LobbyView.Place(s.Game, target, choice), $"choice {choice}");
                }
                yield return E("startSetup", Obj(), _ => LobbyEdits.StartSetup());
                yield return E("configure", Obj(("name", "  Friday  "), ("shape", "player-turn"), ("enforcement", "strict")), s => Configure(s), "");
            }
            if (status == GameStatus.Setup && creator) yield return E("endSetup", Obj(), _ => LobbyEdits.EndSetup());
            if (status == GameStatus.Lobby)
            {
                yield return E("createArmy", Obj(("armyId", S(ArmyId))), _ => LobbyEdits.ArmyFromCreate(ArmyId));
                var seats = LobbyView.Armies(game, Who(Log(log), creator));
                for (var i = 0; i < Math.Max(1, seats.Count); i++)
                {
                    var seat = i;
                    foreach (var army in new Guid?[] { ArmyId, null })
                        yield return E("armySeat", Obj(("seat", seat), ("armyId", army?.ToString())),
                            s => LobbyView.Armies(s.Game, s.ParticipantId) is var all && seat < all.Count ? all[seat].Pick(army) : null, $"seat {seat} {(army is null ? "none" : "army")}");
                }
                // Deployment waits: a team nobody holds (Join) and a team that holds no army (pick one).
                foreach (var (control, dropPlayers, dropArmies) in new[] { ("waitingJoin", 2, 0), ("waitingArmy", 0, 2) })
                    yield return E(control, Obj(("dropPlayers", dropPlayers), ("dropArmies", dropArmies)), s => Waiting(s, dropPlayers, dropArmies, control), "",
                        (s, d) => d["lines"] = J(WaitingLines(s, dropPlayers, dropArmies).Select(l => l.Text).ToList()));
            }
        }

        foreach (var (log, creatorsOnly) in new[] { ("calls/selfplay", false), ("terrain", false) })
        {
            _ = creatorsOnly;
            foreach (var c in Lobby(log, GameStatus.Lobby, true, "creator")) yield return c;
            foreach (var c in Lobby(log, GameStatus.Lobby, false, "player")) yield return c;
            foreach (var c in Lobby(log, GameStatus.Setup, true, "creator")) yield return c;
        }

        foreach (var c in SetupEdits_()) yield return c;
    }

    static (string Text, TeamChoice? Join, ArmySeat? Army)[] WaitingLines(CallState s, int dropPlayers, int dropArmies)
    {
        var (game, board) = Waited(s, dropPlayers, dropArmies);
        return WaitingView.Of(game, WaitingForPlayers.Teams(game, board), s.ParticipantId).Select(l => (l.Text, l.Join, l.Army)).ToArray();
    }

    /// <summary>The game without the participants of team `dropPlayers` (and their armies), or without the armies of team `dropArmies`.</summary>
    static (GameInfo, Board) Waited(CallState s, int dropPlayers, int dropArmies)
    {
        var game = s.Game;
        IEnumerable<Participant> rows = game.Participants;
        var gone = new HashSet<Guid>();
        if (dropPlayers > 0) { var t = game.Teams.First(x => x.Ordinal == dropPlayers).Id; foreach (var p in game.Participants.Where(p => p.TeamId == t)) gone.Add(p.Id); rows = rows.Where(p => !gone.Contains(p.Id)); }
        var noArmy = new HashSet<Guid>();
        if (dropArmies > 0) { var t = game.Teams.First(x => x.Ordinal == dropArmies).Id; foreach (var p in game.Participants.Where(p => p.TeamId == t)) noArmy.Add(p.Id); }
        var board = s.Log.Board with { Armies = s.Log.Board.Armies.Where(a => !gone.Contains(a.ParticipantId) && !noArmy.Contains(a.ParticipantId)).ToList() };
        return (game with { Participants = rows.ToList() }, board);
    }

    static Edit? Waiting(CallState s, int dropPlayers, int dropArmies, string control)
    {
        var line = WaitingLines(s, dropPlayers, dropArmies);
        return control == "waitingJoin" ? line.Select(l => l.Join).FirstOrDefault(j => j is not null)?.Edit : line.Select(l => l.Army).FirstOrDefault(a => a is not null)?.Pick(ArmyId);
    }

    static Edit Configure(CallState s)
    {
        var catalog = s.Log.Welcome.Options;
        var chosen = catalog.Options.Take(1).ToDictionary(o => o.Key, o => o.Values[1].Value);
        return LobbyEdits.Configure(s.Game, "  Friday  ", SchedulerShape.PlayerTurn, Enforcement.Strict, catalog, chosen);
    }

    static IEnumerable<CallCase> SetupEdits_()
    {
        CallCase E(string control, string log, JsonObject extra, Func<CallState, Edit?> build, string suffix = "")
        {
            var choice = Choice("edit", ("control", control));
            foreach (var (k, v) in extra) choice[k] = v?.DeepClone();
            return TableCase("edit/" + control, log, GameStatus.Setup, true, choice, s => build(s) is { } e ? EditMsg(e) : null, suffix);
        }
        var terrain = Log("terrain");
        var piece = terrain.Board.Table.Terrain[0];
        var objectives = Log("calls/objectives");
        var marker = objectives.Board.Table.Markers[0];
        var ruin = BuiltinContent.Terrain["ruin"];
        var draws = new[] { 0.5, 0.25 };
        Func<double> Draw(double[] d) { var q = new Queue<double>(d); return () => q.Dequeue(); }

        yield return E("size", "terrain", Obj(("width", 60), ("length", 44)), _ => SetupEdits.TableSize(60, 44));
        yield return E("texture", "terrain", Obj(("builtin", "desert")), _ => SetupEdits.Texture("desert"));
        yield return E("addBuiltin", "terrain", Obj(("name", "ruin"), ("draws", J(draws))), s => SetupEdits.AddBuiltin("ruin", s.Draft!.Size, Draw(draws)));
        var assetDraws = new[] { 0.0, 0.99 };
        yield return E("addAsset", "terrain", Obj(("assetId", S(AssetId)), ("volumes", J(new[] { ruin.Volume })), ("draws", J(assetDraws))),
            s => SetupEdits.AddAsset(AssetId, [ruin.Volume], s.Draft!.Size, Draw(assetDraws)));
        yield return E("random", "calls/selfplay", Obj(("terrain", J(terrain.Board.Table.Terrain))),
            s => SetupEdits.Random(s.Draft!, new RandomTable(0, terrain.Board.Table.Terrain, [], [])));
        yield return E("clear", "terrain", Obj(), s => SetupEdits.ClearTerrain(s.Draft!));
        yield return E("load", "calls/selfplay", Obj(("layout", J(terrain.Board.Table))), _ => SetupEdits.Load(terrain.Board.Table));
        yield return E("objective", "calls/objectives", Obj(), s => SetupEdits.Objective(s.Draft!));
        yield return E("token", "calls/objectives", Obj(), s => SetupEdits.Token(s.Draft!));
        yield return E("zones", "terrain", Obj(("style", "spearhead")), _ => SetupEdits.Zones("spearhead"));
        yield return E("swapZones", "terrain", Obj(("a", 1), ("b", 2)), _ => SetupEdits.SwapZones(1, 2));

        JsonObject Piece() => Obj(("pieceId", S(piece.Id)));
        Edit? Menu(CallState s, string label, string? input) =>
            MenuModel.ForSetupPiece(s.Draft!, label == "Remove marker" ? marker.Id : piece.Id).First(i => i.Label == (label == "Remove marker" ? "Remove" : label)).Edit!(input);
        yield return E("menuTurnLeft", "terrain", Obj(("pieceId", S(piece.Id)), ("label", "Turn left 15°")), s => Menu(s, "Turn left 15°", null));
        yield return E("menuTurnRight", "terrain", Obj(("pieceId", S(piece.Id)), ("label", "Turn right 15°")), s => Menu(s, "Turn right 15°", null));
        yield return E("menuFacing", "terrain", Obj(("pieceId", S(piece.Id)), ("label", "Set facing…"), ("input", "90")), s => Menu(s, "Set facing…", "90"));
        yield return E("menuRemove", "terrain", Obj(("pieceId", S(piece.Id)), ("label", "Remove")), s => Menu(s, "Remove", null));
        yield return E("menuRemoveMarker", "calls/objectives", Obj(("pieceId", S(marker.Id)), ("label", "Remove")), s => Menu(s, "Remove marker", null));

        yield return E("scale", "terrain", Obj(("pieceId", S(piece.Id)), ("scale", 1.5)), _ => SetupEdits.Scale(piece.Id, 1.5));
        var first = BuiltinContent.Terrain.Keys.First();
        yield return E("swapMesh", "terrain", Obj(("pieceId", S(piece.Id)), ("builtin", first)), _ => SetupEdits.SwapBuiltin(piece.Id, first));
        yield return E("swapAsset", "terrain", Obj(("pieceId", S(piece.Id)), ("assetId", S(AssetId))), _ => SetupEdits.SwapAsset(piece.Id, AssetId));
        yield return E("remove", "terrain", Piece(), _ => SetupEdits.RemoveTerrain(piece.Id));
        yield return E("removeMarker", "calls/objectives", Obj(("markerId", S(marker.Id))), _ => SetupEdits.RemoveMarker(marker.Id));
        yield return E("resetVolumes", "terrain", Obj(("pieceId", S(piece.Id)), ("volumes", J(piece.Volumes.Select(v => v.Volume).ToList()))),
            _ => SetupEdits.ResetVolumes(piece.Id, piece.Volumes.Select(v => v.Volume).ToList()));

        var vol = piece.Volumes[0];
        JsonObject Vol(params (string, JsonNode?)[] more) => Obj([("pieceId", S(piece.Id)), ("volumeId", S(vol.Id)), .. more]);
        Edit Change(Func<Volume, Volume> f) => SetupEdits.ChangeVolume(piece, vol.Id, f);
        yield return E("volKind", "terrain", Vol(("kind", "area")), _ => Change(SetupEdits.WithKind(VolumeKind.Area)));
        yield return E("volA", "terrain", Vol(("value", 7)), _ => Change(SetupEdits.WithFootprintA(7)));
        yield return E("volB", "terrain", Vol(("value", 5)), _ => Change(SetupEdits.WithFootprintB(5)));
        yield return E("volBottom", "terrain", Vol(("value", 0.5)), _ => Change(SetupEdits.WithBottom(0.5)));
        yield return E("volTop", "terrain", Vol(("value", 4)), _ => Change(SetupEdits.WithTop(4)));
        yield return E("volCover", "terrain", Vol(("on", true)), _ => Change(SetupEdits.WithDeclaration(TerrainDeclarations.Cover, true)));
        yield return E("volCoverOff", "terrain", Vol(("on", false)), _ => Change(SetupEdits.WithDeclaration(TerrainDeclarations.Cover, false)));
        yield return E("volNotes", "terrain", Vol(("text", "ford")), _ => Change(SetupEdits.WithNotes("ford")));
        yield return E("volNotesClear", "terrain", Vol(("text", "")), _ => Change(SetupEdits.WithNotes("")));
    }
}
