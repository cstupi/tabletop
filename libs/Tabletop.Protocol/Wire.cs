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
}
/// <summary>After every lobby edit.</summary>
public sealed record GameInfoMessage(GameInfo Game) : SocketMessage;
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
/// may fire at the target (p.8, Who can shoot). Both lists are empty when either unit is off the table.</summary>
public sealed record SightAnswer(Guid QueryId, IReadOnlyList<SightLine> Lines, IReadOnlyList<Guid> CanShoot) : SocketMessage;
/// <summary>Clear: line of sight exists. Screened: clear but through cover. BlockedBy: the terrain pieces or models that obstruct it.
/// Seen: on a clear line, the point of the target the observer's eye sees (its top, centre or rim), null on a blocked line or from a server that does not send it.</summary>
public sealed record SightLine(Guid FromModelId, Guid ToModelId, bool Clear, bool Screened, IReadOnlyList<Guid> BlockedBy, Point3? Seen = null);
/// <summary>The ruleset's facts, sent on connect once playing, then whenever a value changes. MaxWounds: wounds a model takes before it is killed. DeployInside: for each unit that may be put
/// inside a Transport now, before either deploys, the Transport models it may go in (passengers spec); null from a server that does not send it. MaySetAside: the units a player may set aside before
/// deployment now, or return to deployment (Ambush, Scout and what counts as having them); null from a server that does not send it.</summary>
public sealed record ModelFacts(IReadOnlyDictionary<Guid, int> MaxWounds, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? DeployInside = null,
    IReadOnlyList<Guid>? MaySetAside = null) : SocketMessage;

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
