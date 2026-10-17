using System.Text.Json.Serialization;
using Tabletop.Content;

namespace Tabletop.Protocol;

public enum GameStatus { Lobby, Setup, Playing, Ended }

public enum SchedulerShape
{
    [JsonStringEnumMemberName("player-turn")] PlayerTurn,
    [JsonStringEnumMemberName("alternating-activation")] AlternatingActivation,
}

public enum Enforcement { Silent, Warn, Strict }

public sealed record Team(Guid Id, int Ordinal);

/// <summary>TeamId is null until the participant picks one; a participant with no team is never Asked. ArmyId is the lobby pick, null after deployment.</summary>
public sealed record Participant(Guid Id, Guid AccountId, string Name, Guid? TeamId, Guid? ArmyId);

/// <summary>
/// Teams, participants and configuration: what a lobby connect receives.
/// RngSeed is the game's stored dice seed, generated once when the game row is created and never changed —
/// which is what makes undo and replay exact. It is not derived from Id. It is server-only: every die is a pure function of it,
/// so it is never serialized, and a deserialized GameInfo carries 0. The game server reads it from the game row.
/// </summary>
public sealed record GameInfo(
    Guid Id, string Name, GameStatus Status, Guid CreatedBy,
    string GameRules, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps,
    [property: JsonIgnore] long RngSeed,
    IReadOnlyList<Team> Teams, IReadOnlyList<Participant> Participants)
{
    /// <summary>The optional rules the players opted into: keys and values the game's ruleset defines (snake_case), opaque outside it. An absent key is that
    /// option's default, so an empty map is the core game. Set in the lobby with SetConfiguration; frozen from playing on.</summary>
    public IReadOnlyDictionary<string, string> OptionalRules { get; init; } = new Dictionary<string, string>();
}

public sealed record ArmyInPlay(Guid ParticipantId, ArmySnapshot Snapshot);

/// <summary>The draft during setup (Armies empty) or the log's initial state from playing on.</summary>
public sealed record Board(TableState Table, IReadOnlyList<ArmyInPlay> Armies);
