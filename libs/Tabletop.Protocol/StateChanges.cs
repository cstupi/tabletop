using System.Text.Json.Serialization;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Protocol;

/// <summary>
/// One change to in-play state, stated in full. Applying one is copying. A menu edit logs one as an event;
/// a step, a round boundary or a turn end logs a list of them inside its own event.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ModelsMoved), "models_moved")]
[JsonDerivedType(typeof(WoundsSet), "wounds_set")]
[JsonDerivedType(typeof(ConditionSet), "condition_set")]
[JsonDerivedType(typeof(ActionRecorded), "action_recorded")]
[JsonDerivedType(typeof(ActionRecordCleared), "action_record_cleared")]
[JsonDerivedType(typeof(GrantAdded), "grant_added")]
[JsonDerivedType(typeof(GrantRemoved), "grant_removed")]
[JsonDerivedType(typeof(SpellTokensSet), "spell_tokens_set")]
[JsonDerivedType(typeof(WoundsThisActivationSet), "wounds_this_activation_set")]
[JsonDerivedType(typeof(ScoreSet), "score_set")]
[JsonDerivedType(typeof(MarkerPlaced), "marker_placed")]
[JsonDerivedType(typeof(MarkerMoved), "marker_moved")]
[JsonDerivedType(typeof(MarkerRemoved), "marker_removed")]
public abstract record StateChange : EventPayload;

/// <summary>Start and stop for each model. Deployment, hand moves, casualties, embarking: all this one kind.</summary>
public sealed record ModelsMoved(IReadOnlyList<ModelRelocation> Moves) : StateChange;
public sealed record WoundsSet(Guid ModelId, int Wounds) : StateChange;
public sealed record ConditionSet(Subject Subject, string Condition, bool Present) : StateChange;
public sealed record ActionRecorded(Guid UnitId, string Action) : StateChange;
public sealed record ActionRecordCleared(Guid UnitId) : StateChange;
public sealed record GrantAdded(Guid UnitId, Grant Grant) : StateChange;
public sealed record GrantRemoved(Guid UnitId, Guid GrantId) : StateChange;
public sealed record SpellTokensSet(Guid ModelId, int Tokens) : StateChange;
public sealed record WoundsThisActivationSet(Guid UnitId, int Wounds) : StateChange;
public sealed record ScoreSet(Guid TeamId, int Score) : StateChange;
/// <summary>A token dropped in play. Setup markers are rows in the snapshot, not this.</summary>
public sealed record MarkerPlaced(Marker Marker) : StateChange;
public sealed record MarkerMoved(Guid MarkerId, Point3 Position, Yaw Yaw) : StateChange;
public sealed record MarkerRemoved(Guid MarkerId) : StateChange;
