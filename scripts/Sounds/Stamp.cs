using System.Security.Cryptography;
using System.Text;

namespace Tabletop.Sounds;

/// <summary>What a full run wrote: the hash of the generator's source (its .cs and .csproj files, line endings normalised) and of
/// each shipped file, kept in <see cref="FileName"/> beside the source. The tests check the shipped files against it, not against a
/// fresh build, because Math's last bits differ between C runtimes.</summary>
public static class Stamp
{
    public const string FileName = "sounds.sha256";

    public static string Source(string sourceDir)
    {
        var sb = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs") || p.EndsWith(".csproj"))
            .Select(p => Path.GetRelativePath(sourceDir, p).Replace('\\', '/'))
            .Where(p => !p.StartsWith("bin/") && !p.StartsWith("obj/"))
            .Order(StringComparer.Ordinal))
            sb.Append(path).Append('\n').Append(File.ReadAllText(Path.Combine(sourceDir, path)).Replace("\r\n", "\n")).Append('\n');
        return Hash(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>"source &lt;hash&gt;", then "&lt;hash&gt; &lt;name&gt;.wav" per sound in order.</summary>
    public static string Of(string sourceDir, IEnumerable<(string Name, byte[] Bytes)> sounds) =>
        $"source {Source(sourceDir)}\n" + string.Concat(sounds.Select(s => $"{Hash(s.Bytes)} {s.Name}.wav\n"));
}
