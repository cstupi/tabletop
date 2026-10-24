using System.Reflection;
using Tabletop.Content;

namespace Tabletop.Management.Content.Layouts;

/// <summary>The built-in content management serves from its own build: the repo catalog's terrain (embedded), for the random table.</summary>
public sealed class BuiltinLayouts
{
    public IReadOnlyDictionary<string, BuiltinTerrain> Terrain { get; }

    BuiltinLayouts(IReadOnlyDictionary<string, BuiltinTerrain> terrain) => Terrain = terrain;

    /// <summary>Reads the embedded <c>builtin-catalog.json</c> once; registered as a singleton.</summary>
    public static BuiltinLayouts Embedded() => new(BuiltinTerrain.FromCatalog(Read("builtin-catalog.json")));

    static string Read(string resource)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"{resource} is not embedded.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
