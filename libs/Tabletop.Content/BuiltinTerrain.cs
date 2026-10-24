using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Tabletop.Geometry;

namespace Tabletop.Content;

/// <summary>A built-in terrain piece: its volume (the data) and the mesh it draws with (builtin content spec).</summary>
public sealed partial record BuiltinTerrain(string Name, string Label, Volume Volume, string Mesh)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")] private static partial Regex NamePattern();

    static readonly Dictionary<string, TerrainDeclarations> DeclarationNames = new()
    {
        ["impassable"] = TerrainDeclarations.Impassable, ["blocks_line_of_sight"] = TerrainDeclarations.BlocksLineOfSight,
        ["see_in_and_out"] = TerrainDeclarations.SeeInAndOut, ["cover"] = TerrainDeclarations.Cover,
        ["difficult"] = TerrainDeclarations.Difficult, ["dangerous"] = TerrainDeclarations.Dangerous,
        ["dangerous_on_rush_charge"] = TerrainDeclarations.DangerousOnRushCharge,
    };

    /// <summary>The catalog's <c>terrain</c> array, checked; <see cref="FormatException"/> names the first fault. Ordinal key order is the array's.</summary>
    public static IReadOnlyDictionary<string, BuiltinTerrain> Parse(JsonElement terrain, IReadOnlyCollection<string> meshes)
    {
        List<TerrainDto> dtos;
        try { dtos = terrain.Deserialize<List<TerrainDto>>(Json) ?? throw new FormatException("The catalog has no terrain."); }
        catch (JsonException e) { throw new FormatException($"The catalog is not JSON: {e.Message}"); }
        var result = new Dictionary<string, BuiltinTerrain>();
        foreach (var t in dtos)
        {
            var id = t.Id is { } i && NamePattern().IsMatch(i) ? i : throw new FormatException($"Terrain id '{t.Id}' is not a name.");
            if (result.ContainsKey(id)) throw new FormatException($"Terrain '{id}' is listed twice.");
            if (string.IsNullOrWhiteSpace(t.Label) || t.Label.Length > 64) throw new FormatException($"Terrain '{id}' has no label, or one over 64 characters.");
            if (t.Notes is { Length: > 500 }) throw new FormatException($"Terrain '{id}' has notes over 500 characters.");
            if (t.Mesh is null || !meshes.Contains(t.Mesh)) throw new FormatException($"Terrain '{id}' names mesh '{t.Mesh}', which the catalog does not list.");
            var kind = t.Kind switch { "solid" => VolumeKind.Solid, "area" => VolumeKind.Area, _ => throw new FormatException($"Terrain '{id}' has kind '{t.Kind}', not solid or area.") };
            var footprint = t.Footprint switch
            {
                { Kind: "circle", A: > 0 and <= 144 } f => Footprint.Circle(f.A),
                { Kind: "rect", A: > 0 and <= 144, B: > 0 and <= 144 } f => Footprint.Rect(f.A, f.B!.Value),
                _ => throw new FormatException($"Terrain '{id}' has no circle or rect footprint with positive sizes up to 144\"."),
            };
            if (t.Top is not (> 0 and <= 144)) throw new FormatException($"Terrain '{id}' has top {t.Top}, not above 0 and up to 144\".");
            var declarations = TerrainDeclarations.None;
            foreach (var d in t.Declarations ?? [])
                declarations |= DeclarationNames.TryGetValue(d, out var flag) ? flag : throw new FormatException($"Terrain '{id}' declares '{d}', which is not a terrain declaration.");
            result[id] = new BuiltinTerrain(id, t.Label, new Volume(kind, footprint, new Point2(0, 0), new Yaw(0), 0, t.Top, declarations, t.Notes), t.Mesh);
        }
        if (result.Count == 0) throw new FormatException("The catalog has no terrain.");
        return result;
    }

    /// <summary>The <c>terrain</c> of a whole catalog file (the repo's or the published one).</summary>
    public static IReadOnlyDictionary<string, BuiltinTerrain> FromCatalog(string catalogJson)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(catalogJson).RootElement; }
        catch (JsonException e) { throw new FormatException($"The catalog is not JSON: {e.Message}"); }
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("terrain", out var terrain)) throw new FormatException("The catalog has no terrain.");
        var meshes = root.TryGetProperty("meshes", out var m) && m.ValueKind == JsonValueKind.Array ? m.EnumerateArray().Select(e => e.GetString() ?? "").ToList() : [];
        return Parse(terrain, meshes);
    }

    sealed record TerrainDto(string? Id, string? Label, string? Mesh, string? Kind, FootprintDto? Footprint, double Top, List<string>? Declarations, string? Notes);
    sealed record FootprintDto(string? Kind, double A, double? B);
}
