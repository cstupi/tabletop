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

    public static ClientSettings Load(string path) => Load(path, BuildInfo.Current);

    /// <summary>A release build always reaches the servers it was built for: persisted server addresses are a development
    /// build's override, and a release build neither reads nor writes them.</summary>
    public static ClientSettings Load(string path, BuildInfo build)
    {
        var def = DefaultFor(build);
        var d = Read(path);
        if (d is null) return def;
        var release = build.WebUrl is not null;
        return new ClientSettings(d.Animate ?? def.Animate, new HashSet<string>(d.MutedWindowKinds ?? []), d.RememberMe ?? def.RememberMe,
            release ? def.ManagementUrl : d.ManagementUrl ?? def.ManagementUrl, release ? def.GameServerUrl : d.GameServerUrl ?? def.GameServerUrl,
            Enum.TryParse<LogFilter>(d.LogFilter, true, out var f) && Enum.IsDefined(f) && !int.TryParse(d.LogFilter, out _) ? f : def.LogFilter, d.LogOpen ?? def.LogOpen)
            with { Theme = UiThemes.Resolve(d.Theme).Id, CameraFollow = d.CameraFollow ?? true, TurnBanner = d.TurnBanner ?? true, RangeRings = d.RangeRings ?? false,
                WebUrl = release ? def.WebUrl : d.WebUrl ?? def.WebUrl };
    }

    /// <summary>Whether the file at <paramref name="path"/> carries a server address, which only a development build (or a
    /// release before release builds stopped writing them) saves.</summary>
    public static bool NamesServers(string path) => Read(path) is { } d && (d.ManagementUrl ?? d.GameServerUrl ?? d.WebUrl) is not null;

    static Dto? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), Json) : null; }
        catch (Exception e) when (e is JsonException or IOException) { return null; }
    }

    public void Save(string path) => Save(path, BuildInfo.Current);

    public void Save(string path, BuildInfo build)
    {
        var dev = build.WebUrl is null;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Dto(Animate, MutedWindowKinds.Order().ToArray(), RememberMe, dev ? ManagementUrl : null, dev ? GameServerUrl : null,
            LogFilter.ToString(), LogOpen, Theme, CameraFollow, TurnBanner, RangeRings, dev ? WebUrl : null), Json));
    }

    public ClientSettings Mute(string windowKind) => this with { MutedWindowKinds = new HashSet<string>(MutedWindowKinds) { windowKind } };
    public ClientSettings Unmute(string windowKind) => this with { MutedWindowKinds = MutedWindowKinds.Where(k => k != windowKind).ToHashSet() };

    public bool Equals(ClientSettings? other) => other is not null && Animate == other.Animate && RememberMe == other.RememberMe
        && ManagementUrl == other.ManagementUrl && GameServerUrl == other.GameServerUrl && LogFilter == other.LogFilter && LogOpen == other.LogOpen
        && Theme == other.Theme && CameraFollow == other.CameraFollow && TurnBanner == other.TurnBanner && RangeRings == other.RangeRings && WebUrl == other.WebUrl && MutedWindowKinds.SetEquals(other.MutedWindowKinds);
    public override int GetHashCode() => HashCode.Combine(Animate, RememberMe, ManagementUrl, GameServerUrl, MutedWindowKinds.Count, LogFilter, Theme);
}
