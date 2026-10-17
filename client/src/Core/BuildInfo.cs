using System.Reflection;

namespace Tabletop.Client.Core;

/// <summary>What a release build was stamped with (client releases spec → Versions): the release version and the web app's
/// public origin, from the TabletopVersion and TabletopWebUrl assembly metadata. A development build has neither.</summary>
public sealed record BuildInfo(string Version, string? WebUrl)
{
    public const string Dev = "dev";

    public static BuildInfo Current { get; } = From(typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .GroupBy(a => a.Key).ToDictionary(g => g.Key, g => g.Last().Value));

    public static BuildInfo From(IReadOnlyDictionary<string, string?> metadata)
    {
        string? Read(string key) => metadata.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        return new(Read("TabletopVersion") ?? Dev, Read("TabletopWebUrl")?.TrimEnd('/'));
    }
}
