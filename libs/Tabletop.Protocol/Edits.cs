using System.Text.Json.Serialization;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Protocol;

/// <summary>The third message kind: applied to the game's rows or draft, broadcast, persisted, never logged.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CreateTeam), "create_team")]
[JsonDerivedType(typeof(PickTeam), "pick_team")]
[JsonDerivedType(typeof(PickArmy), "pick_army")]
[JsonDerivedType(typeof(LeaveTeam), "leave_team")]
[JsonDerivedType(typeof(SetConfiguration), "set_configuration")]
[JsonDerivedType(typeof(StartSetup), "start_setup")]
[JsonDerivedType(typeof(EndSetup), "end_setup")]
[JsonDerivedType(typeof(SetTableSize), "set_table_size")]
[JsonDerivedType(typeof(SetTexture), "set_texture")]
[JsonDerivedType(typeof(AddTerrain), "add_terrain")]
[JsonDerivedType(typeof(MoveTerrain), "move_terrain")]
[JsonDerivedType(typeof(ScaleTerrain), "scale_terrain")]
[JsonDerivedType(typeof(RemoveTerrain), "remove_terrain")]
[JsonDerivedType(typeof(OverrideTerrainVolumes), "override_terrain_volumes")]
[JsonDerivedType(typeof(SwapTerrainMesh), "swap_terrain_mesh")]
[JsonDerivedType(typeof(AddSetupMarker), "add_setup_marker")]
[JsonDerivedType(typeof(MoveSetupMarker), "move_setup_marker")]
[JsonDerivedType(typeof(RemoveSetupMarker), "remove_setup_marker")]
[JsonDerivedType(typeof(SetZoneStyle), "set_zone_style")]
[JsonDerivedType(typeof(SwapZones), "swap_zones")]
[JsonDerivedType(typeof(ReplaceDraft), "replace_draft")]
public abstract record Edit;

/// <summary>Writes the game's rows; accepted in lobby and setup (teams and armies stay editable through setup), and PickTeam and PickArmy
/// also while deployment waits for players (WaitingForPlayers).</summary>
public abstract record LobbyEdit : Edit;
public sealed record CreateTeam : LobbyEdit;
/// <summary>
/// The sender (ParticipantId null) takes TeamId as one more of its teams: an account may hold several teams, each a participant row of its own
/// with its own army (self-play). Null gives up every team: a participant with no team watches. A team other accounts hold is taken the same way: any
/// number of players share a side. ParticipantId names a participant row to move onto TeamId: the sender's own, or any row for the creator.
/// </summary>
public sealed record PickTeam(Guid? TeamId, Guid? ParticipantId = null) : LobbyEdit;
/// <summary>The army the sender brings to TeamId, one of its teams; null TeamId is its first team (or its row when it holds none).</summary>
public sealed record PickArmy(Guid? ArmyId, Guid? TeamId = null) : LobbyEdit;
/// <summary>The sender gives up one of its teams, and the army it brought to it; the account keeps its other teams. Lobby and setup only.</summary>
public sealed record LeaveTeam(Guid TeamId) : LobbyEdit;
/// <summary>Creator only. OptionalRules replaces the game's map whole; the ruleset refuses a key or value it does not define (RefusalReason.Unknown).</summary>
public sealed record SetConfiguration(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps,
    IReadOnlyDictionary<string, string> OptionalRules) : LobbyEdit;
/// <summary>Creator only. Lobby → setup; creates the draft row.</summary>
public sealed record StartSetup : LobbyEdit;
/// <summary>Creator only. Setup → playing; off-slab terrain dropped, snapshots written, the log opens.</summary>
public sealed record EndSetup : LobbyEdit;

/// <summary>Writes the draft; accepted only while status is setup. Applied in arrival order.</summary>
public abstract record SetupEdit : Edit;
public sealed record SetTableSize(TableSize Size) : SetupEdit;
public sealed record SetTexture(VisualRef Texture) : SetupEdit;
/// <summary>Client-generated ids; volumes always present (copied from the asset or built-in).</summary>
public sealed record AddTerrain(TerrainPlacement Placement) : SetupEdit;
public sealed record MoveTerrain(Guid PlacementId, Point3 Position, Yaw Yaw) : SetupEdit;
public sealed record ScaleTerrain(Guid PlacementId, double Scale) : SetupEdit;
public sealed record RemoveTerrain(Guid PlacementId) : SetupEdit;
/// <summary>Also how overrides are "cleared": send the asset's or built-in's own volumes again.</summary>
public sealed record OverrideTerrainVolumes(Guid PlacementId, IReadOnlyList<TerrainVolume> Volumes) : SetupEdit;
/// <summary>The placement keeps its volumes and declarations.</summary>
public sealed record SwapTerrainMesh(Guid PlacementId, VisualRef Mesh) : SetupEdit;
public sealed record AddSetupMarker(Marker Marker) : SetupEdit;
public sealed record MoveSetupMarker(Guid MarkerId, Point3 Position, Yaw Yaw) : SetupEdit;
public sealed record RemoveSetupMarker(Guid MarkerId) : SetupEdit;
/// <summary>Shapes are the style's zone shape ids in order; team one gets the first.</summary>
public sealed record SetZoneStyle(string Style, IReadOnlyList<string> Shapes) : SetupEdit;
public sealed record SwapZones(int TeamOrdinalA, int TeamOrdinalB) : SetupEdit;
/// <summary>How a layout is loaded. Size included: the layout wins.</summary>
public sealed record ReplaceDraft(TableState Draft) : SetupEdit;
