using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Client.Core;

/// <summary>Which rolls the combat log shows. The order is the filter menu's.</summary>
public enum LogFilter { All, Rolls, Mine, Unit }

/// <summary>Per-player, per-machine settings. Presentation and convenience only; nothing here reaches the server.</summary>
public sealed record ClientSettings(bool Animate, IReadOnlySet<string> MutedWindowKinds, bool RememberMe, string ManagementUrl, string GameServerUrl, LogFilter LogFilter = LogFilter.All, bool LogOpen = false)
{
    public string Theme { get; init; } = UiThemes.DefaultId;
    public bool CameraFollow { get; init; } = true;
    public bool TurnBanner { get; init; } = true;
    public bool RangeRings { get; init; }
    /// <summary>The web app's origin: the update-required screen sends the player to its Download page.</summary>
    public string WebUrl { get; init; } = DevWebUrl;
    public string DownloadUrl => WebUrl.TrimEnd('/') + "/download";

    /// <summary>127.0.0.1, not localhost: Windows resolves localhost to ::1 first, WSL's loopback forwarding carries IPv4 only,
    /// and .NET's connect waits out a 21 s timeout on ::1 before falling back.</summary>
    public static ClientSettings Default { get; } = DefaultFor(BuildInfo.Current);
    const string DevWebUrl = "http://127.0.0.1:5173";

    /// <summary>A release build reaches management and the game socket through its web origin, where the reverse proxy fronts
    /// /v1 and /ws; a development build reaches the local stack.</summary>
    public static ClientSettings DefaultFor(BuildInfo build)
    {
        if (build.WebUrl is not { } web) return new(true, new HashSet<string>(), false, "http://127.0.0.1:5100", "ws://127.0.0.1:5200/ws");
        var socket = new UriBuilder(web) { Scheme = web.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws", Path = "/ws" };
        return new(true, new HashSet<string>(), false, web, socket.Uri.ToString()) { WebUrl = web };
    }

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    sealed record Dto(bool? Animate, string[]? MutedWindowKinds, bool? RememberMe, string? ManagementUrl, string? GameServerUrl, string? LogFilter, bool? LogOpen,
        string? Theme = null, bool? CameraFollow = null, bool? TurnBanner = null, bool? RangeRings = null, string? WebUrl = null);

    public static ClientSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return Default;
            var d = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), Json);
            if (d is null) return Default;
            return new ClientSettings(d.Animate ?? Default.Animate, new HashSet<string>(d.MutedWindowKinds ?? []), d.RememberMe ?? Default.RememberMe,
                d.ManagementUrl ?? Default.ManagementUrl, d.GameServerUrl ?? Default.GameServerUrl,
                Enum.TryParse<LogFilter>(d.LogFilter, true, out var f) && Enum.IsDefined(f) && !int.TryParse(d.LogFilter, out _) ? f : Default.LogFilter, d.LogOpen ?? Default.LogOpen)
                with { Theme = UiThemes.Resolve(d.Theme).Id, CameraFollow = d.CameraFollow ?? true, TurnBanner = d.TurnBanner ?? true, RangeRings = d.RangeRings ?? false, WebUrl = d.WebUrl ?? Default.WebUrl };
        }
        catch (Exception e) when (e is JsonException or IOException) { return Default; }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Dto(Animate, MutedWindowKinds.Order().ToArray(), RememberMe, ManagementUrl, GameServerUrl, LogFilter.ToString(), LogOpen, Theme, CameraFollow, TurnBanner, RangeRings, WebUrl), Json));
    }

    public ClientSettings Mute(string windowKind) => this with { MutedWindowKinds = new HashSet<string>(MutedWindowKinds) { windowKind } };
    public ClientSettings Unmute(string windowKind) => this with { MutedWindowKinds = MutedWindowKinds.Where(k => k != windowKind).ToHashSet() };

    public bool Equals(ClientSettings? other) => other is not null && Animate == other.Animate && RememberMe == other.RememberMe
        && ManagementUrl == other.ManagementUrl && GameServerUrl == other.GameServerUrl && LogFilter == other.LogFilter && LogOpen == other.LogOpen
        && Theme == other.Theme && CameraFollow == other.CameraFollow && TurnBanner == other.TurnBanner && RangeRings == other.RangeRings && WebUrl == other.WebUrl && MutedWindowKinds.SetEquals(other.MutedWindowKinds);
    public override int GetHashCode() => HashCode.Combine(Animate, RememberMe, ManagementUrl, GameServerUrl, MutedWindowKinds.Count, LogFilter, Theme);
}
