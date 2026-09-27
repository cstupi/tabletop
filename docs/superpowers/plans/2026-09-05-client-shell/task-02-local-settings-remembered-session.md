# Task 2: Local settings and the remembered session

**Files:**
- Create: `client/src/Core/ClientSettings.cs`, `client/src/Core/SessionStore.cs`
- Test: `tests/Tabletop.Client.Tests/SettingsTests.cs`

**Interfaces:**
- Produces: `sealed record ClientSettings(bool Animate, IReadOnlySet<string> MutedWindowKinds, bool RememberMe, string ManagementUrl, string GameServerUrl)` with `static ClientSettings Default`, `static ClientSettings Load(string path)` (defaults when the file is missing or unreadable), `void Save(string path)`, `ClientSettings Mute(string windowKind)`, `ClientSettings Unmute(string windowKind)`; `sealed class SessionStore(string path)` with `string? Read()` (null when absent, unreadable, or not JWT-shaped), `void Write(string jwt)`, `void Clear()`.
- **The store is a plain file with owner-only permissions, not an OS keystore**, because the client may run on other operating systems. `Write` creates the file with `UnixFileMode.UserRead | UnixFileMode.UserWrite` on any platform that has file modes, and relies on the user profile's own ACL on Windows, where `OS.GetUserDataDir()` already resolves under `%APPDATA%`. **What that protects against:** another local account on a shared machine reading the token, and the file being picked up by a backup or sync of a directory that would otherwise be group- or world-readable. **What it does not protect against:** anything running as the same user, which can read the file, and the file being copied to another machine, where it still refreshes until the Stytch session behind it expires. Windows data protection would have stopped the second of those, at the cost of being Windows-only. The exposure is bounded by the token being a five-minute JWT whose only power is `POST /v1/auth/session/refresh`, by `Clear()` on logout and on a `401`, and by anything running as the user being able to drive the client itself regardless.
- **Rejected: a per-platform store behind one interface** (`ProtectedData` on Windows, libsecret on Linux, Keychain on macOS). Three implementations, two of which cannot be exercised on the development machine, and libsecret needs a session keyring that a minimal or headless Linux desktop may not have — a hard failure mode where the file has none. The seam is still `SessionStore`: three methods and one call site in `Main._Ready`, so a keystore can replace it later without touching `Session`.
- The JWT lives five minutes; what the store keeps alive is the Stytch session behind it — on launch the stored JWT goes to `POST /v1/auth/session/refresh` (Task 3), and a `401` shows the login screen.

Spec: client shell → Screens → Login ("holds the JWT in memory and, if the player chooses, in the OS credential store so the next launch skips the screen"), Asks and windows ("A mute list per player, in local settings"), Animation ("the player's own animate setting, kept in local settings"; whole-design review ruling 3).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class SettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-client-tests").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Defaults_when_missing_and_round_trips()
    {
        var path = Path.Combine(_dir, "settings.json");
        var s = ClientSettings.Load(path);
        Assert.Equal(ClientSettings.Default, s);
        Assert.Equal(("http://localhost:5100", "ws://localhost:5200/ws", true, false), (s.ManagementUrl, s.GameServerUrl, s.Animate, s.RememberMe));
        s = s.Mute("casting") with { Animate = false, RememberMe = true };
        s.Save(path);
        var back = ClientSettings.Load(path);
        Assert.Equal(s, back);
        Assert.Contains("casting", back.MutedWindowKinds);
        Assert.Empty(back.Unmute("casting").MutedWindowKinds);
    }

    [Fact]
    public void Unreadable_settings_fall_back_to_defaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        Assert.Equal(ClientSettings.Default, ClientSettings.Load(path));
    }

    [Fact]
    public void Session_store_round_trips_owner_only_and_clears()
    {
        var path = Path.Combine(_dir, "session.bin");
        var store = new SessionStore(path);
        Assert.Null(store.Read());
        store.Write("eyJ.jwt.here");
        Assert.Equal("eyJ.jwt.here", store.Read());
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        store.Clear();
        Assert.Null(store.Read());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_session_file_that_is_not_a_jwt_reads_as_null()
    {
        var path = Path.Combine(_dir, "session.bin");
        var store = new SessionStore(path);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Null(store.Read());
        File.WriteAllText(path, "");
        Assert.Null(store.Read());
        File.WriteAllText(path, "eyJ.only-two-segments");
        Assert.Null(store.Read());
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.Client.Tests --filter SettingsTests`; expect compile errors. Write `ClientSettings.cs`**

```csharp
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
```

- [ ] **Step 3: Write `SessionStore.cs`**

```csharp
using System.Text;

namespace Tabletop.Client.Core;

/// <summary>The remembered session JWT, in a file only its owner can read. Absent, unreadable, or not JWT-shaped reads as null.
/// Deliberately not an OS keystore: the client may run on more than one operating system, and a
/// portable file is honest about being weaker. It keeps the token from other local accounts; it does not keep it from anything
/// running as this user, nor from being copied to another machine, where it refreshes until the Stytch session expires.</summary>
public sealed class SessionStore(string path)
{
    public string? Read()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            // A JWT is three non-empty dot-separated segments. Anything else is a truncated write or an unrelated
            // file, and must not reach an Authorization header: refusing it here is what "a corrupt session reads as null" means
            // now that nothing decrypts.
            var parts = text.Split('.');
            return parts.Length == 3 && parts.All(p => p.Length > 0) ? text : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public void Write(string jwt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        // Set the mode at creation, so there is no instant where the file is readable by anyone else. Windows has no
        // file modes and throws on this property; there the user profile directory's own ACL is the protection.
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var file = new FileStream(path, options);
        file.Write(Encoding.UTF8.GetBytes(jwt));
    }

    public void Clear() { if (File.Exists(path)) File.Delete(path); }
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Client.Tests`; expect 7 passed. Commit: `Add the client's local settings and the remembered session store`.**
