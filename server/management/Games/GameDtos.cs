using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Protocol;

namespace Tabletop.Management.Games;

public sealed record PropertiesSummary(string ContentHash, string Name, string GameRules);
public sealed record CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps);
public sealed record GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants);
/// <summary>One asset in play. Scale and Orientation are the owner's import corrections: the client cannot read another participant's asset row, so they ride here.</summary>
public sealed record ManifestEntry(Guid AssetId, AssetKind Kind, string Hash, Uri Url, double Scale, Orientation Orientation);
public sealed record Manifest(IReadOnlyList<ManifestEntry> Assets, DateTimeOffset ExpiresAt);
/// <summary>What the manifest query returns per asset, before presigning.</summary>
public sealed record ManifestRow(Guid AssetId, Guid OwnerId, string Kind, string Hash, double Scale, double Pitch, double Yaw, double Roll);
