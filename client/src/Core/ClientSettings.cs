using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Client.Core;

/// <summary>Per-player, per-machine settings. Presentation and convenience only; nothing here reaches the server.</summary>
public sealed record ClientSettings(bool Animate, IReadOnlySet<string> MutedWindowKinds, bool RememberMe, string ManagementUrl, string GameServerUrl)
{
    public static ClientSettings Default { get; } = new(true, new HashSet<string>(), false, "http://localhost:5100", "ws://localhost:5200/ws");

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    sealed record Dto(bool? Animate, string[]? MutedWindowKinds, bool? RememberMe, string? ManagementUrl, string? GameServerUrl);

    public static ClientSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return Default;
            var d = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), Json);
            if (d is null) return Default;
            return new ClientSettings(d.Animate ?? Default.Animate, new HashSet<string>(d.MutedWindowKinds ?? []), d.RememberMe ?? Default.RememberMe,
                d.ManagementUrl ?? Default.ManagementUrl, d.GameServerUrl ?? Default.GameServerUrl);
        }
        catch (Exception e) when (e is JsonException or IOException) { return Default; }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Dto(Animate, MutedWindowKinds.Order().ToArray(), RememberMe, ManagementUrl, GameServerUrl), Json));
    }

    public ClientSettings Mute(string windowKind) => this with { MutedWindowKinds = new HashSet<string>(MutedWindowKinds) { windowKind } };
    public ClientSettings Unmute(string windowKind) => this with { MutedWindowKinds = MutedWindowKinds.Where(k => k != windowKind).ToHashSet() };

    public bool Equals(ClientSettings? other) => other is not null && Animate == other.Animate && RememberMe == other.RememberMe
        && ManagementUrl == other.ManagementUrl && GameServerUrl == other.GameServerUrl && MutedWindowKinds.SetEquals(other.MutedWindowKinds);
    public override int GetHashCode() => HashCode.Combine(Animate, RememberMe, ManagementUrl, GameServerUrl, MutedWindowKinds.Count);
}
