using System.Security.Cryptography;
using System.Text;

namespace Tabletop.TerrainMeshes;

/// <summary>What a full run wrote: the hash of the generator's source (its .cs and .csproj files, line endings normalised)
/// and of each shipped mesh, kept in <see cref="FileName"/> beside the source. The tests check the shipped meshes against it,
/// not against a fresh build, because MathF's last bits differ between C runtimes and a vertex weld or colour byte on a
/// rounding boundary then lands differently.</summary>
public static class Stamp
{
    public const string FileName = "meshes.sha256";

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

    /// <summary>"source &lt;hash&gt;", then "&lt;hash&gt; &lt;name&gt;.glb" per mesh in catalog order.</summary>
    public static string Of(string sourceDir, IEnumerable<(string Name, byte[] Bytes)> meshes) =>
        $"source {Source(sourceDir)}\n" + string.Concat(meshes.Select(m => $"{Hash(m.Bytes)} {m.Name}.glb\n"));
}
