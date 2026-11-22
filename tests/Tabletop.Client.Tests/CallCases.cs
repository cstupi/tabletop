using System.Text.Json;
using System.Text.Json.Nodes;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>A recorded log: tests/fixtures/&lt;Name&gt;.{welcome,board,events}.json, the raw texts kept for the known-id set.</summary>
public sealed record CallLog(string Name, string[] Texts, Welcome Welcome, Board Board, IReadOnlyList<GameEvent> Events)
{
    public GameInfo Game => Welcome.Game;

    static string Dir => Path.Combine(CallCases.RepoRoot(), "viewer", "tests", "fixtures");

    public static CallLog Read(string name)
    {
        var texts = new[] { "welcome", "board", "events" }.Select(s => File.ReadAllText(Path.Combine(Dir, $"{name}.{s}.json"))).ToArray();
        return new(name, texts, (Welcome)WireJson.Deserialize(texts[0]), ((BoardMessage)WireJson.Deserialize(texts[1])).Board, ((EventsMessage)WireJson.Deserialize(texts[2])).Events);
    }
}

/// <summary>A game state to decide in: a playing log cut at a seq (the projection and the log), or a lobby or setup game with its draft.</summary>
public sealed class CallState
{
    public required CallLog Log { get; init; }
    public required GameInfo Game { get; init; }
    public Projection? P { get; init; }
    public IReadOnlyList<GameEvent> Events { get; init; } = [];
    public TableState? Draft { get; init; }
    public Guid? ParticipantId { get; init; }
    public IReadOnlyList<Guid> MyTeams { get; init; } = [];
    public Guid? Acting { get; init; }
    public AskView? Mine => P is null ? null : AskViewModel.Mine(P, Game, Acting);
    public Names Names => new(Game, P);

    /// <summary>The log's events with seq &lt;= upTo applied, then (undoneTo) the Undone marker a player's Undo would append.</summary>
    public static CallState Playing(CallLog log, long upTo, Guid participantId, long? undoneTo = null)
    {
        var applier = new EventApplier(log.Board, log.Game);
        applier.ApplyAll(log.Events.Where(e => e.Seq <= upTo));
        if (undoneTo is { } to) applier.Apply(new GameEvent(upTo + 1, participantId, new Undone(to), null));
        var teams = Seats.Teams(log.Game, participantId);
        return new CallState
        {
            Log = log, Game = log.Game, P = applier.Projection, Events = applier.Log, ParticipantId = participantId, MyTeams = teams,
            Acting = Seats.Acting(applier.Projection, teams, applier.Log),
        };
    }

    /// <summary>The log's game with this status and its board's table as the draft; no projection.</summary>
    public static CallState Table(CallLog log, GameStatus status, Guid participantId) => new()
    {
        Log = log, Game = log.Game with { Status = status }, Draft = log.Board.Table, ParticipantId = participantId,
        MyTeams = Seats.Teams(log.Game, participantId),
    };
}

/// <summary>One case of viewer/tests/fixtures/calls.json (same-calls part 3).</summary>
public sealed class CallCase
{
    public required string Kind { get; init; }
    public required CallState State { get; init; }
    public required JsonObject Choice { get; init; }
    public long UpTo { get; init; }
    public string Suffix { get; init; } = "";
    public bool Creator { get; init; }
    public long? UndoneTo { get; init; }
    public IReadOnlyDictionary<Guid, int> MaxWounds { get; init; } = new Dictionary<Guid, int>();
    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> DeployInside { get; init; } = new Dictionary<Guid, IReadOnlyList<Guid>>();
    public IReadOnlySet<Guid> MaySetAside { get; init; } = new HashSet<Guid>();
    public IReadOnlyList<GrantChoice> Grantable { get; init; } = [];
    public JsonObject? Decisions { get; set; }
    public JsonNode? Message { get; set; }
    /// <summary>Answer cases only: whether the built answer equals the logged one (a disembark's facing aside).</summary>
    public bool? RebuildsLogged { get; set; }

    public string Name => $"{Kind} {State.Log.Name}@{UpTo}" + (Suffix.Length > 0 ? " " + Suffix : "");

    /// <summary>Everything the case reads except its decisions and message, which is where part 4 takes the known ids from.</summary>
    public JsonObject Inputs()
    {
        var o = new JsonObject
        {
            ["name"] = Name, ["log"] = State.Log.Name, ["upTo"] = UpTo,
            ["status"] = State.P is null ? State.Game.Status.ToString().ToLowerInvariant() : "playing",
        };
        if (State.ParticipantId is { } who) o["participantId"] = who.ToString();
        if (State.P is null) o["creator"] = Creator;
        o["maxWounds"] = CallCases.J(MaxWounds);
        o["deployInside"] = CallCases.J(DeployInside);
        o["maySetAside"] = CallCases.J(MaySetAside.ToList());
        o["grantable"] = CallCases.J(Grantable);
        if (UndoneTo is { } to) o["undoneTo"] = to;
        o["choice"] = Choice.DeepClone();
        return o;
    }
}

public static partial class CallCases
{
    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tabletop.sln"))) return dir.FullName;
        throw new InvalidOperationException("Tabletop.sln not found above " + AppContext.BaseDirectory);
    }

    public static readonly string[] LogNames =
        ["skirmish", "terrain", "calls/ambush", "calls/transport", "calls/passengers", "calls/selfplay", "calls/gameover", "calls/casting", "calls/objectives"];

    static readonly Dictionary<string, CallLog> Logs = LogNames.ToDictionary(n => n, CallLog.Read);
    public static CallLog Log(string name) => Logs[name];

    public static JsonNode? J<T>(T value) => JsonNode.Parse(WireJson.Serialize(value));
    static JsonNode? Free(JsonNode? v) => v?.Parent is null ? v : v.DeepClone();
    public static JsonObject Obj(params (string, JsonNode?)[] fields) { var o = new JsonObject(); foreach (var (k, v) in fields) o[k] = Free(v); return o; }

    /// <summary>A team's player: its participant with the lowest id.</summary>
    public static Guid PlayerOf(GameInfo game, Guid team) => game.Participants.Where(p => p.TeamId == team).OrderBy(p => p.Id).First().Id;

    public static Guid? TeamOfUnit(CallState s, Guid unit) =>
        s.P!.Units.GetValueOrDefault(unit) is { } u ? s.Game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId : null;

    /// <summary>The decisions every playing case carries.</summary>
    public static JsonObject DecisionsOf(CallState s)
    {
        var mid = new[] { s.Acting }.Concat(s.MyTeams.Cast<Guid?>()).Select(t => DeployView.MidDeploy(s.P!, s.Events, t)).FirstOrDefault(m => m is not null);
        return Obj(
            ("myTeams", J(s.MyTeams.Select(t => t.ToString()).ToList())),
            ("acting", s.Acting?.ToString()),
            ("mine", s.Mine is { } m ? Obj(("askId", m.Ask.Id.ToString()), ("mode", Camel(m.Mode.ToString()))) : null),
            ("midDeploy", mid is null ? null : Obj(("unitId", mid.UnitId.ToString()), ("question", mid.Question), ("shelved", J(mid.ShelvedModelIds.Select(i => i.ToString()).ToList())))));
    }

    public static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    public static JsonArray NumberedOf(IEnumerable<(int Key, string Label, string OptionId)> numbered) =>
        new(numbered.Select(n => (JsonNode?)new JsonArray(n.Key, n.OptionId)).ToArray());

    /// <summary>A playing case: its decisions, and its message from `build` (null: nothing is sent).</summary>
    public static CallCase Playing(string kind, CallLog log, long upTo, Guid participant, JsonObject choice, Func<CallState, JsonNode?> build,
        Action<CallState, JsonObject>? decide = null, string suffix = "", long? undoneTo = null,
        IReadOnlyDictionary<Guid, int>? maxWounds = null, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? deployInside = null,
        IReadOnlyList<GrantChoice>? grantable = null, IReadOnlySet<Guid>? maySetAside = null)
    {
        var state = CallState.Playing(log, upTo, participant, undoneTo);
        var c = new CallCase
        {
            Kind = kind, State = state, Choice = choice, UpTo = upTo, Suffix = suffix, UndoneTo = undoneTo,
            MaxWounds = maxWounds ?? new Dictionary<Guid, int>(), DeployInside = deployInside ?? new Dictionary<Guid, IReadOnlyList<Guid>>(),
            Grantable = grantable ?? [], MaySetAside = maySetAside ?? new HashSet<Guid>(),
        };
        var decisions = DecisionsOf(state);
        decide?.Invoke(state, decisions);
        c.Decisions = decisions;
        c.Message = build(state);
        return c;
    }

    public static JsonNode Command(Command c) => JsonNode.Parse(WireJson.Serialize<Command>(c))!;
    public static JsonNode EditMsg(Edit e) => JsonNode.Parse(WireJson.Serialize<Edit>(e))!;
    public static JsonObject Choice(string kind, params (string, JsonNode?)[] fields) { var o = new JsonObject { ["kind"] = kind }; foreach (var (k, v) in fields) o[k] = Free(v); return o; }

    // ---- the whole fixture ----

    public static readonly string[] EditControls =
    [
        "columnJoin", "columnMove", "watch", "createTeam", "armySeat", "waitingJoin", "waitingArmy", "configure", "configureHash", "startSetup", "endSetup",
        "size", "texture", "addBuiltin", "addAsset", "random", "clear", "load", "objective", "zones", "swapZones",
        "menuTurnLeft", "menuTurnRight", "menuFacing", "menuRemove", "menuRemoveMarker",
        "scale", "swapMesh", "swapAsset", "remove", "removeMarker", "resetVolumes",
        "volKind", "volA", "volB", "volBottom", "volTop", "volCover", "volCoverOff", "volNotes", "volNotesClear",
    ];

    public static readonly string[] Required =
    [
        "choose/option", "choose/key", "choose/unitKey", "choose/clickMine", "choose/clickNone", "pass", "confirm", "confirm/endRound",
        "spend", "pick", "pick/deploy", "pick/ambusher", "pickKey", "assign", "casualties", "move", "move/stop", "move/disembark", "embark",
        "deploy/click", "deploy/ghosts", "deploy/ambusher", "gameOver", "selfplay/answer", "selfplay/deploy",
        "menu/model", "menu/unit", "menu/marker", "menu/team", "menu/setAside", "menu/return", "menu/putIn", "menu/takeOut", "menu/abandon",
        "menu/boarding", "menu/lock", "menu/invalidInput", "menu/grant",
        "undo/button", "undo/row", "undo/roll", "undo/undone",
        "drop/models", "drop/marker", "drop/terrain", "drop/setupMarker",
        .. EditControls.Select(c => "edit/" + c),
    ];

    static IReadOnlyList<CallCase>? _all;
    public static IReadOnlyList<CallCase> All() => _all ??= [.. Asks(), .. Deployments(), .. MenuCases(), .. UndoCases(), .. DropCases(), .. EditCases()];

    public static IEnumerable<CallCase> Answers() => All().Where(c => c.RebuildsLogged is not null);

    public static JsonObject Fixture()
    {
        var cases = new JsonArray();
        foreach (var c in All())
        {
            var inputs = c.Inputs();
            var known = CallNormalise.KnownIds([.. c.State.Log.Texts, inputs.ToJsonString()]);
            var o = c.Inputs();
            o["decisions"] = c.Decisions?.DeepClone();
            o["message"] = c.Message is null ? null : CallNormalise.Of(c.Message, known);
            cases.Add(o);
        }
        return new JsonObject { ["logs"] = new JsonArray(LogNames.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()), ["cases"] = cases };
    }
}
