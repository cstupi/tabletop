using System.Text.Json;
using System.Text.Json.Serialization;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Protocol;

/// <summary>One frame on the socket, either direction. Kinds say which way they travel.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Connect), "connect")]
[JsonDerivedType(typeof(CommandMessage), "command")]
[JsonDerivedType(typeof(EditMessage), "edit")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(GameInfoMessage), "game")]
[JsonDerivedType(typeof(RulesOptionsMessage), "rules_options")]
[JsonDerivedType(typeof(BoardMessage), "board")]
[JsonDerivedType(typeof(EventsMessage), "events")]
[JsonDerivedType(typeof(AtRest), "at_rest")]
[JsonDerivedType(typeof(PresenceMessage), "presence")]
[JsonDerivedType(typeof(PingMessage), "ping")]
[JsonDerivedType(typeof(PresenceEnded), "presence_ended")]
[JsonDerivedType(typeof(Refused), "refused")]
[JsonDerivedType(typeof(Rejected), "rejected")]
[JsonDerivedType(typeof(SightQuery), "sight_query")]
[JsonDerivedType(typeof(Reauth), "reauth")]
[JsonDerivedType(typeof(SightAnswer), "sight_answer")]
[JsonDerivedType(typeof(ModelFacts), "model_facts")]
public abstract record SocketMessage;

// Client → server
/// <summary>First frame. The JWT carries the account id; the game id names the game; the version is checked.</summary>
public sealed record Connect(string SessionJwt, Guid GameId, int ClientVersion) : SocketMessage;
public sealed record CommandMessage(Guid CommandId, Command Command) : SocketMessage;
public sealed record EditMessage(Guid EditId, Edit Edit) : SocketMessage;
/// <summary>Which models of the observer can see which of the target, and which may shoot it. Answered outside the log, to the asker alone.</summary>
public sealed record SightQuery(Guid QueryId, Guid ObserverUnitId, Guid TargetUnitId) : SocketMessage;
/// <summary>A refreshed session JWT for this socket, sent before the one it connected with expires. Taken silently when it verifies and
/// names the same account: the socket then closes at the new token's expiry. Otherwise Refused by ReauthId, and the old expiry stands.</summary>
public sealed record Reauth(Guid ReauthId, string SessionJwt) : SocketMessage;

// Server → client
/// <summary>ParticipantId is Guid.Empty for an admin watcher, which is no participant of the game.</summary>
public sealed record Welcome(Guid ParticipantId, GameInfo Game) : SocketMessage
{
    /// <summary>The game ruleset's optional rules and presets, for the lobby's controls.</summary>
    public RulesOptionCatalog Options { get; init; } = RulesOptionCatalog.Empty;
    /// <summary>Side Battle's legs under the game's rule set (IGameRules.Zones): what a client resolves the zone shapes with.</summary>
    public ZoneLegs Zones { get; init; } = ZoneLegs.Default;
    /// <summary>The words a client shows for a terrain declaration under the game's rule set (IGameRules.TerrainWords), keyed by its snake_case
    /// name; a client keeps its own word for a name not listed.</summary>
    public IReadOnlyDictionary<string, string> TerrainWords { get; init; } = new Dictionary<string, string>();
    /// <summary>What a terrain declaration does to a unit under the game's rule set (IGameRules.TerrainRules), one sentence keyed by its
    /// snake_case name; a client shows none for a name not listed.</summary>
    public IReadOnlyDictionary<string, string> TerrainRules { get; init; } = new Dictionary<string, string>();
    /// <summary>How far from an objective marker's centre a model's base edge holds it under the game's rule set (IGameRules.MarkerRange),
    /// in inches; a client draws it about each marker. Null: the rule set holds no markers by range, and nothing is drawn.</summary>
    public double? MarkerRange { get; init; }
    /// <summary>The unit coherency the game's rule set judges a move by (IGameRules.Coherency), which a client measures its ghosts with as the
    /// server does (Coherency.Check). Null: the rule set holds no coherency, and nothing is measured.</summary>
    public UnitCoherency? Coherency { get; init; }
}
/// <summary>A rule set's unit coherency, in inches, edge to edge: every model within Chain of another, one chain, and within Spread of every
/// other; Elevated in place of Chain to a model at a different elevation.</summary>
public sealed record UnitCoherency(double Chain, double Elevated, double Spread);
/// <summary>After every lobby edit.</summary>
public sealed record GameInfoMessage(GameInfo Game) : SocketMessage;
/// <summary>A lobby SetConfiguration moved the game to another rule set's id: the new ruleset's whole per-rules bundle (catalog, zone legs,
/// terrain words), shaped as Welcome's, before its GameInfoMessage.</summary>
public sealed record RulesOptionsMessage(RulesOptionCatalog Options) : SocketMessage
{
    /// <summary>As Welcome.Zones, under the new rule set.</summary>
    public ZoneLegs Zones { get; init; } = ZoneLegs.Default;
    /// <summary>As Welcome.TerrainWords, under the new rule set.</summary>
    public IReadOnlyDictionary<string, string> TerrainWords { get; init; } = new Dictionary<string, string>();
    /// <summary>As Welcome.TerrainRules, under the new rule set.</summary>
    public IReadOnlyDictionary<string, string> TerrainRules { get; init; } = new Dictionary<string, string>();
    /// <summary>As Welcome.MarkerRange, under the new rule set.</summary>
    public double? MarkerRange { get; init; }
    /// <summary>As Welcome.Coherency, under the new rule set.</summary>
    public UnitCoherency? Coherency { get; init; }
}
/// <summary>On connect from setup on, and after every setup edit (the whole draft).</summary>
public sealed record BoardMessage(Board Board) : SocketMessage;
/// <summary>The log from the start on connect, then live events. Filtered per viewer for sealed answers.</summary>
public sealed record EventsMessage(IReadOnlyList<GameEvent> Events) : SocketMessage;
/// <summary>The server has finished processing and has nothing further to emit, sent after the last frame that
/// processing produced. Who receives it, and which inbound frames produce one, are the game server's to decide and
/// are settled in its plan: the frame follows its batch, so it is broadcast when the batch appended an event and
/// goes to the provoking client alone when it did not. A relayed presence or ping never produces one.</summary>
/// <param name="Seq">The last sequence number in the log. It repeats the previous value when this round of
/// processing appended no event, and is 0 for a game whose log is empty, so it is a rest point and not a receipt.</param>
public sealed record AtRest(long Seq) : SocketMessage;
public sealed record PresenceEnded(Guid ParticipantId) : SocketMessage;
/// <summary>A command or edit was not applied. MessageId is the CommandId or EditId; Guid.Empty for a watcher's presence or ping, which carry no id.</summary>
public sealed record Refused(Guid MessageId, RefusalReason Reason, string Detail) : SocketMessage;
/// <summary>The connect was not accepted; the server closes the socket after this.</summary>
public sealed record Rejected(RejectReason Reason) : SocketMessage;
/// <summary>The answer to a SightQuery, to the asker alone: one line per observer-model, target-model pair, and the observer's models that
/// may fire at the target (p.8, Who can shoot). Both lists are empty when either unit is off the table. Ranges: each of the observer's ranged weapons'
/// range against this target as the rules leave it; null from a server that does not send it.</summary>
public sealed record SightAnswer(Guid QueryId, IReadOnlyList<SightLine> Lines, IReadOnlyList<Guid> CanShoot, IReadOnlyList<ShotRange>? Ranges = null) : SocketMessage;
/// <summary>A ranged weapon type's printed range and its range against one target in inches, with the rules that changed it (Shadowborn), none when it is the printed one.</summary>
public sealed record ShotRange(string Weapon, double Printed, double Inches, IReadOnlyList<string> By);
/// <summary>Clear: line of sight exists. Screened: clear but through cover. BlockedBy: the terrain pieces or models that obstruct it.
/// Seen: on a clear line, the point of the target the observer's eye sees (its top, centre or rim), null on a blocked line or from a server that does not send it.</summary>
public sealed record SightLine(Guid FromModelId, Guid ToModelId, bool Clear, bool Screened, IReadOnlyList<Guid> BlockedBy, Point3? Seen = null);
/// <summary>The ruleset's facts, sent on connect once playing, then whenever a value changes. MaxWounds: wounds a model takes before it is killed. DeployInside: for each unit that may be put
/// inside a Transport now, before either deploys, the Transport models it may go in (passengers spec); null from a server that does not send it. MaySetAside: the units a player may set aside before
/// deployment now, or return to deployment (Ambush, Scout and what counts as having them); null from a server that does not send it. Threats: per unit
/// with a living model on the table, how far its next activation reaches (threat range plan); null from a server that does not send it. InCover: the
/// units in cover where they stand, the rules' terrain test needing no shooter (the unit card's shield); null from a server that does not send it.
/// SetAsideRules: per unit MaySetAside names, the rule that lets it be set aside as the unit carries it (Ambush, Scout, Infiltrate…) and that rule's
/// text, for the deployment chip; null from a server that does not send it.</summary>
public sealed record ModelFacts(IReadOnlyDictionary<Guid, int> MaxWounds, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? DeployInside = null,
    IReadOnlyList<Guid>? MaySetAside = null, IReadOnlyDictionary<Guid, UnitThreat>? Threats = null, IReadOnlyList<Guid>? InCover = null,
    IReadOnlyDictionary<Guid, SetAsideRule>? SetAsideRules = null) : SocketMessage;
/// <summary>A rule that lets a unit be set aside before deployment: its name as the army book prints it, and its rule text.</summary>
public sealed record SetAsideRule(string Name, string Text);
/// <summary>A unit's reach in its next activation, in inches from each model's base edge. Charge: its charge move, null when it may not charge.
/// ShootMove: the move it may shoot after (0 when it shoots only standing), null when it may not shoot. Ranges: per living model on the table with a
/// ranged weapon, its longest range as its rules leave it.</summary>
public sealed record UnitThreat(double? Charge, double? ShootMove, IReadOnlyDictionary<Guid, double> Ranges);

// Both directions
public sealed record PresenceMessage(Presence Presence) : SocketMessage;
public sealed record PingMessage(Ping Ping) : SocketMessage;

public enum RejectReason { UpgradeRequired, InvalidSession, NotAParticipant }
/// <summary>Watcher: the sender is an admin watching the game, which may send nothing that changes it (admin watch spec).</summary>
public enum RefusalReason { OutsideLegalSet, Illegal, WrongStatus, NotPermitted, Unknown, Watcher }

public static class WireJson
{
    public static JsonSerializerOptions Options { get; } = ContentJson.Configure(new JsonSerializerOptions(), integerEnums: false);

    public static string Serialize(SocketMessage message) => JsonSerializer.Serialize(message, Options);
    public static SocketMessage Deserialize(string json) => Deserialize<SocketMessage>(json);
    /// <summary>Serializes as the nearest polymorphic ancestor of T (or T itself), so a concrete union member
    /// passed with its own static type still writes its "kind" — the wire shape never depends on the caller's
    /// declared type.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, PolymorphicTypeOf(typeof(T)), Options);
    /// <summary>Reads with T itself, not its polymorphic root: a union is read through its base type. A concrete T whose JSON
    /// carries "kind" relies on the options' default UnmappedMemberHandling.Skip to ignore the discriminator.</summary>
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("null frame");

    static Type PolymorphicTypeOf(Type type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.IsDefined(typeof(JsonPolymorphicAttribute), inherit: false))
                return t;
        return type;
    }
}
