using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Tabletop.Client.Core;

/// <summary>
/// Small GLB files, one per kind of glTF material a player's miniature can bring (the web app's STL conversion, textured PBR,
/// vertex colour, blended, cut out), for the shader warm-up: Godot's glTF import makes each into the same material and vertex
/// format a real file of its kind gets, so drawing them compiles what a game's models will need. One quad, 4 vertices.
/// </summary>
public static class WarmupGlb
{
    public enum Variant { Plain, BaseColourTexture, TexturedPbr, VertexColour, Blended, CutOut }

    public static byte[] Build(Variant v, byte[] png)
    {
        var bin = new MemoryStream();
        var views = new List<object>(); var accessors = new List<object>();
        int View(byte[] data, int? target = null)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            var offset = (int)bin.Length; bin.Write(data);
            var view = new Dictionary<string, object> { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = data.Length };
            if (target is { } t) view["target"] = t;
            views.Add(view); return views.Count - 1;
        }
        int Floats(float[] values, string type, int count, float[]? min = null, float[]? max = null)
        {
            var bytes = new byte[values.Length * 4];
            for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
            var a = new Dictionary<string, object> { ["bufferView"] = View(bytes, 34962), ["componentType"] = 5126, ["count"] = count, ["type"] = type };
            if (min is not null) a["min"] = min; if (max is not null) a["max"] = max;
            accessors.Add(a); return accessors.Count - 1;
        }
        var attributes = new Dictionary<string, int>
        {
            ["POSITION"] = Floats([0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0], "VEC3", 4, [0, 0, 0], [1, 1, 0]),
            ["NORMAL"] = Floats([0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1], "VEC3", 4),
        };
        var textured = v is Variant.BaseColourTexture or Variant.TexturedPbr or Variant.CutOut;
        if (textured) attributes["TEXCOORD_0"] = Floats([0, 1, 1, 1, 1, 0, 0, 0], "VEC2", 4);
        if (v == Variant.TexturedPbr) attributes["TANGENT"] = Floats([1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1], "VEC4", 4);
        if (v == Variant.VertexColour) attributes["COLOR_0"] = Floats([1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1, 1, 1, 1, 1], "VEC4", 4);
        var indexBytes = new byte[12];
        ushort[] quad = [0, 1, 2, 0, 2, 3];
        for (var i = 0; i < quad.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(indexBytes.AsSpan(i * 2), quad[i]);
        accessors.Add(new Dictionary<string, object> { ["bufferView"] = View(indexBytes, 34963), ["componentType"] = 5123, ["count"] = 6, ["type"] = "SCALAR" });
        var indices = accessors.Count - 1;

        var pbr = new Dictionary<string, object> { ["baseColorFactor"] = new[] { 0.8f, 0.8f, 0.8f, v == Variant.Blended ? 0.5f : 1f }, ["metallicFactor"] = 0f, ["roughnessFactor"] = 0.8f };
        var material = new Dictionary<string, object> { ["name"] = $"warmup-{v}", ["pbrMetallicRoughness"] = pbr };
        var root = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = "tabletop warm-up" },
            ["scene"] = 0, ["scenes"] = new[] { new { nodes = new[] { 0 } } }, ["nodes"] = new[] { new { mesh = 0 } },
            ["meshes"] = new[] { new { primitives = new[] { new { attributes, indices, material = 0 } } } },
        };
        if (textured)
        {
            root["images"] = new[] { new { bufferView = View(png), mimeType = "image/png" } };
            root["samplers"] = new[] { new { magFilter = 9729, minFilter = 9987 } };
            root["textures"] = new[] { new { sampler = 0, source = 0 } };
            pbr["baseColorTexture"] = new { index = 0 };
            if (v == Variant.TexturedPbr) { pbr["metallicRoughnessTexture"] = new { index = 0 }; material["normalTexture"] = new { index = 0 }; material["occlusionTexture"] = new { index = 0 }; }
        }
        if (v == Variant.Blended) { material["alphaMode"] = "BLEND"; material["doubleSided"] = true; }
        if (v == Variant.CutOut) { material["alphaMode"] = "MASK"; material["alphaCutoff"] = 0.5f; }
        root["materials"] = new[] { material };
        root["accessors"] = accessors; root["bufferViews"] = views;
        while (bin.Length % 4 != 0) bin.WriteByte(0);
        root["buffers"] = new[] { new { byteLength = bin.Length } };

        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(root));
        var jsonPadded = (json.Length + 3) / 4 * 4;
        var total = 12 + 8 + jsonPadded + 8 + (int)bin.Length;
        var glb = new byte[total];
        var s = glb.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s, 0x46546C67); BinaryPrimitives.WriteUInt32LittleEndian(s[4..], 2); BinaryPrimitives.WriteUInt32LittleEndian(s[8..], (uint)total);
        BinaryPrimitives.WriteUInt32LittleEndian(s[12..], (uint)jsonPadded); BinaryPrimitives.WriteUInt32LittleEndian(s[16..], 0x4E4F534A);
        json.CopyTo(s[20..]); for (var i = 20 + json.Length; i < 20 + jsonPadded; i++) glb[i] = (byte)' ';
        var at = 20 + jsonPadded;
        BinaryPrimitives.WriteUInt32LittleEndian(s[at..], (uint)bin.Length); BinaryPrimitives.WriteUInt32LittleEndian(s[(at + 4)..], 0x004E4942);
        bin.ToArray().CopyTo(s[(at + 8)..]);
        return glb;
    }
}
