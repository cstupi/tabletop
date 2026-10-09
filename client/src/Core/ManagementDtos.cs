using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

// Mirrors of Tabletop.Management's DTOs (management plan → Interfaces → Produces). Same property names, same JSON under ContentJson.
public sealed record AccountRow(Guid Id, string Name);
public sealed record SessionResponse(string SessionJwt, AccountRow Account);
public sealed record OtpSentResponse(string MethodId);
public sealed record Page<T>(IReadOnlyList<T> Items, Guid? Next);
public sealed record GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants);
public sealed record CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps);
public sealed record PropertiesSummary(string ContentHash, string Name, string GameRules);

/// <summary>One catalog entry of a pinned property set (GET /v1/properties/{hash}/rules).</summary>
public sealed record RuleTextDto(string Name, string Kind, bool TakesParameter, string RuleText);
public sealed record ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models);
public sealed record LayoutSummary(Guid Id, string Name, TableSize Size, int Terrain, int Markers);
public enum AssetKind { Miniature, Terrain, Texture }
public sealed record AssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, bool Uploaded, double Scale, Orientation Orientation, Base? ModelledBase);
public sealed record DownloadResponse(Uri Url, DateTimeOffset ExpiresAt);
public sealed record ManifestEntry(Guid AssetId, AssetKind Kind, string Hash, Uri Url, double Scale, Orientation Orientation);
public sealed record Manifest(IReadOnlyList<ManifestEntry> Assets, DateTimeOffset ExpiresAt);

/// <summary>A non-2xx answer: the problem+json title and detail when the body had them.</summary>
public class ManagementException(int status, string title, string? detail) : Exception(detail is null ? title : $"{title}: {detail}")
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
}

/// <summary>426: this build is older than the server accepts. The client shows the update screen and stops.</summary>
public sealed class UpdateRequiredException(string title, string? detail) : ManagementException(ProtocolVersion.UpgradeRequiredStatusCode, title, detail);
