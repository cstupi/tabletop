using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Protocol;

namespace Tabletop.Management.Games;

public sealed record PropertiesSummary(string ContentHash, string Name, string GameRules, bool Default);
public sealed record CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps);
/// <summary>A row of the public game list. ParticipantNames: the account names of its participants, in join order. Joined: the caller holds a
/// participant row in the game (what the client's Replay offers). Teams and TeamsReady: the game's teams, and those with a participant
/// holding an army. Progress: where a game in play stands, as the game server last wrote it; null before its first write (lobby, setup).</summary>
public sealed record GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants, IReadOnlyList<string> ParticipantNames, bool Joined,
    string? RuleSet = null, int Teams = 0, int TeamsReady = 0, GameProgress? Progress = null);
/// <summary>One asset in play. Scale and Orientation are the owner's import corrections: the client cannot read another participant's asset row, so they ride here.</summary>
public sealed record ManifestEntry(Guid AssetId, AssetKind Kind, string Hash, Uri Url, double Scale, Orientation Orientation);
public sealed record Manifest(IReadOnlyList<ManifestEntry> Assets, DateTimeOffset ExpiresAt);
/// <summary>What the manifest query returns per asset, before presigning.</summary>
/// <summary><c>Key</c> is the blob row's S3 key: a copy of a shared asset names the sharer's object.</summary>
public sealed record ManifestRow(Guid AssetId, Guid OwnerId, string Kind, string Hash, string Key, double Scale, double Pitch, double Yaw, double Roll);
