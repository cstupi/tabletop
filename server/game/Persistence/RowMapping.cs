using Npgsql;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Persistence;

/// <summary>Enum spellings come from the wire format, which is the closed set's one spelling. Reading and writing the table_state tree.</summary>
public static class RowMapping
{
    public static string EnumText<T>(T value) where T : struct, Enum => WireJson.Serialize(value).Trim('"');
    public static T EnumFrom<T>(string text) where T : struct, Enum => WireJson.Deserialize<T>($"\"{text}\"");

    public static VisualRef Visual(string? builtin, Guid? assetId) => builtin is not null ? VisualRef.OfBuiltin(builtin) : VisualRef.OfAsset(assetId!.Value);

    public static Volume VolumeFrom(NpgsqlDataReader r, int start) => new(
        EnumFrom<VolumeKind>(r.GetString(start)), new Footprint(EnumFrom<FootprintKind>(r.GetString(start + 1)), r.GetDouble(start + 2), r.GetDouble(start + 3)),
        new Point2(r.GetDouble(start + 4), r.GetDouble(start + 5)), new Yaw(r.GetDouble(start + 6)), r.GetDouble(start + 7), r.GetDouble(start + 8),
        Declarations(r, start + 9), r.IsDBNull(start + 16) ? null : r.GetString(start + 16));

    static TerrainDeclarations Declarations(NpgsqlDataReader r, int i)
    {
        var d = TerrainDeclarations.None;
        if (r.GetBoolean(i)) d |= TerrainDeclarations.Impassable;
        if (r.GetBoolean(i + 1)) d |= TerrainDeclarations.BlocksLineOfSight;
        if (r.GetBoolean(i + 2)) d |= TerrainDeclarations.SeeInAndOut;
        if (r.GetBoolean(i + 3)) d |= TerrainDeclarations.Cover;
        if (r.GetBoolean(i + 4)) d |= TerrainDeclarations.Difficult;
        if (r.GetBoolean(i + 5)) d |= TerrainDeclarations.Dangerous;
        if (r.GetBoolean(i + 6)) d |= TerrainDeclarations.DangerousOnRushCharge;
        return d;
    }

    public const string VolumeColumns = "kind, footprint, dim_a, dim_b, offset_x, offset_y, yaw, bottom, top, impassable, blocks_los, see_in_out, cover, difficult, dangerous, dangerous_rush_charge, notes";

    public static object?[] VolumeValues(Volume v) =>
    [
        EnumText(v.Kind), EnumText(v.Footprint.Kind), v.Footprint.A, v.Footprint.B, v.Offset.X, v.Offset.Y, v.Yaw.Degrees,
        v.Bottom, v.Top, v.Declarations.HasFlag(TerrainDeclarations.Impassable), v.Declarations.HasFlag(TerrainDeclarations.BlocksLineOfSight),
        v.Declarations.HasFlag(TerrainDeclarations.SeeInAndOut), v.Declarations.HasFlag(TerrainDeclarations.Cover), v.Declarations.HasFlag(TerrainDeclarations.Difficult),
        v.Declarations.HasFlag(TerrainDeclarations.Dangerous), v.Declarations.HasFlag(TerrainDeclarations.DangerousOnRushCharge), v.Notes,
    ];

    /// <summary>game.optional_rules (jsonb, an object of strings) read as text.</summary>
    public static IReadOnlyDictionary<string, string> OptionalRules(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    public static string OptionalRulesJson(IReadOnlyDictionary<string, string> map) => System.Text.Json.JsonSerializer.Serialize(map);

    public static Guid? NullableGuid(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetGuid(i);
    public static string? NullableString(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
