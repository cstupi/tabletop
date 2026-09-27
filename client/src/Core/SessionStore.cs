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
