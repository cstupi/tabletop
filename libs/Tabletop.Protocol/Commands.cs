using System.Text.Json.Serialization;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Protocol;

/// <summary>An intent. The server validates and emits events; under strict enforcement it may refuse.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AnswerAsk), "answer_ask")]
[JsonDerivedType(typeof(MoveModels), "move_models")]
[JsonDerivedType(typeof(MoveMarker), "move_marker")]
[JsonDerivedType(typeof(SetWounds), "set_wounds")]
[JsonDerivedType(typeof(SetCondition), "set_condition")]
[JsonDerivedType(typeof(RecordAction), "record_action")]
[JsonDerivedType(typeof(ClearActionRecord), "clear_action_record")]
[JsonDerivedType(typeof(AddGrant), "add_grant")]
[JsonDerivedType(typeof(RemoveGrant), "remove_grant")]
[JsonDerivedType(typeof(SetSpellTokens), "set_spell_tokens")]
[JsonDerivedType(typeof(SetWoundsThisActivation), "set_wounds_this_activation")]
[JsonDerivedType(typeof(SetScore), "set_score")]
[JsonDerivedType(typeof(SetObjectiveControl), "set_objective_control")]
[JsonDerivedType(typeof(SetActiveTeam), "set_active_team")]
[JsonDerivedType(typeof(SetRound), "set_round")]
[JsonDerivedType(typeof(Undo), "undo")]
[JsonDerivedType(typeof(AbandonSequence), "abandon_sequence")]
[JsonDerivedType(typeof(SetDice), "set_dice")]
public abstract record Command;

/// <summary>The only way to act inside a turn or a sequence, and the only way to pass. TeamId is the team answering, one the sender's account
/// holds (a player holding two teams answers for each); null is the first team of the sender's the Ask addresses that has not answered.</summary>
public sealed record AnswerAsk(Guid AskId, AskAnswer Answer, Guid? TeamId = null) : Command;
/// <summary>A hand move: start and stop per model. Never ends a turn.</summary>
public sealed record MoveModels(IReadOnlyList<ModelRelocation> Moves) : Command;
/// <summary>Moves an objective marker the table holds.</summary>
public sealed record MoveMarker(Guid MarkerId, Point3 Position, Yaw Yaw) : Command;

// The menu: every property is editable, accepted under every enforcement setting.
public sealed record SetWounds(Guid ModelId, int Wounds) : Command;
public sealed record SetCondition(Subject Subject, string Condition, bool Present) : Command;
public sealed record RecordAction(Guid UnitId, string Action) : Command;
public sealed record ClearActionRecord(Guid UnitId) : Command;
public sealed record AddGrant(Guid UnitId, Grant Grant) : Command;
public sealed record RemoveGrant(Guid UnitId, Guid GrantId) : Command;
public sealed record SetSpellTokens(Guid ModelId, int Tokens) : Command;
public sealed record SetWoundsThisActivation(Guid UnitId, int Wounds) : Command;
public sealed record SetScore(Guid TeamId, int Score) : Command;
/// <summary>The menu's override of who controls an objective marker; TeamId null makes it neutral.</summary>
public sealed record SetObjectiveControl(Guid MarkerId, Guid? TeamId) : Command;
/// <summary>Logs a turn_started.</summary>
public sealed record SetActiveTeam(Guid TeamId) : Command;
/// <summary>Logs a round_started with the current order.</summary>
public sealed record SetRound(int Round) : Command;

public sealed record Undo(long ToSeq) : Command;
public sealed record AbandonSequence(Guid FrameId) : Command;
public sealed record DieValue(int Index, int Raw);
/// <summary>Fixes a logged roll's raw faces. The rules suite's tool: a server accepts it only where the suite runs (GameServerOptions.AcceptsSetDice).</summary>
public sealed record SetDice(Guid FrameId, Guid RollId, IReadOnlyList<DieValue> Dice) : Command;
