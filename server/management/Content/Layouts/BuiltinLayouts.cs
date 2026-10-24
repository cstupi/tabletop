using System.Reflection;
using Tabletop.Content;

namespace Tabletop.Management.Content.Layouts;

/// <summary>The built-in content management serves from its own build: the repo catalog's terrain (embedded), for the random table.</summary>
public sealed class BuiltinLayouts
{
    public IReadOnlyDictionary<string, BuiltinTerrain> Terrain { get; }
    /// <summary>The predefined tables (embedded <c>builtin-table-*.json</c>), in <see cref="Planets"/> order.</summary>
    public IReadOnlyList<TableState> Tables { get; }
    public static readonly Planet[] Planets = [Planet.Hospitable, Planet.Wasteland, Planet.Death, Planet.Ruins];

    BuiltinLayouts(IReadOnlyDictionary<string, BuiltinTerrain> terrain, IReadOnlyList<TableState> tables) => (Terrain, Tables) = (terrain, tables);

    /// <summary>Reads the embedded <c>builtin-catalog.json</c> once; registered as a singleton.</summary>
    public static BuiltinLayouts Embedded() => new(BuiltinTerrain.FromCatalog(Read("builtin-catalog.json")),
        Planets.Select(p => ContentJson.Deserialize<TableState>(Read($"builtin-table-{p.ToString().ToLowerInvariant()}.json"))).ToList());

    static string Read(string resource)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"{resource} is not embedded.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
