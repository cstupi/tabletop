using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Tabletop.Geometry.Wasm.Tests;

public class AgreementFixtureTests
{
    static readonly JsonSerializerOptions FileOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>One case per line: a diff shows which cases changed without tens of thousands of indented lines.</summary>
    static string FixtureText() => "[\n" + string.Join(",\n", AgreementCases.All.Select(c => JsonSerializer.Serialize(c, FileOptions))) + "\n]\n";

    [Fact]
    public void Every_facade_method_has_a_case()
    {
        var methods = typeof(Facade).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToHashSet();
        Assert.Equal(methods.OrderBy(m => m), AgreementCases.All.Select(c => c.Method).Distinct().OrderBy(m => m));
        Assert.Equal(30, methods.Count);
    }

    [Fact]
    public void The_contacts_case_has_all_four_kinds()
    {
        var c = AgreementCases.All.Single(c => c.Name == "contacts_every_kind");
        var kinds = c.Expected.EnumerateArray().Select(e => e.GetProperty("kind").GetString()).OrderBy(k => k);
        Assert.Equal(new[] { "crosses", "in_impassable", "on_base", "too_close" }, kinds);
    }

    [Fact]
    public void The_facade_answers_every_case_as_Geometry_does()
    {
        foreach (var c in AgreementCases.All)
        {
            var method = typeof(Facade).GetMethod(c.Method, BindingFlags.Public | BindingFlags.Static)!;
            var answer = JsonDocument.Parse((string)method.Invoke(null, [c.Request.GetRawText()])!).RootElement;
            Assert.True(Close(c.Expected, answer, out var where), $"{c.Name}: {where}");
        }
    }

    [Fact]
    public void The_checked_in_fixture_is_current()
    {
        var text = FixtureText();
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1")
        {
            Directory.CreateDirectory(RepoPaths.Fixtures);
            File.WriteAllText(RepoPaths.GeometryCases, text, new UTF8Encoding(false));
        }
        Assert.True(File.Exists(RepoPaths.GeometryCases), "run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Geometry.Wasm.Tests");
        Assert.True(File.ReadAllText(RepoPaths.GeometryCases) == text, "run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Geometry.Wasm.Tests");
    }

    /// <summary>Equal structure, numbers within 1e-9.</summary>
    static bool Close(JsonElement a, JsonElement b, out string where, string path = "$")
    {
        where = "";
        if (a.ValueKind != b.ValueKind) { where = $"{path}: {a.ValueKind} vs {b.ValueKind}"; return false; }
        switch (a.ValueKind)
        {
            case JsonValueKind.Number:
                if (Math.Abs(a.GetDouble() - b.GetDouble()) <= 1e-9) return true;
                where = $"{path}: {a.GetDouble()} vs {b.GetDouble()}"; return false;
            case JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength()) { where = $"{path}: length {a.GetArrayLength()} vs {b.GetArrayLength()}"; return false; }
                for (var i = 0; i < a.GetArrayLength(); i++) if (!Close(a[i], b[i], out where, $"{path}[{i}]")) return false;
                return true;
            case JsonValueKind.Object:
                var pa = a.EnumerateObject().OrderBy(p => p.Name).ToList(); var pb = b.EnumerateObject().OrderBy(p => p.Name).ToList();
                if (!pa.Select(p => p.Name).SequenceEqual(pb.Select(p => p.Name))) { where = $"{path}: members differ"; return false; }
                for (var i = 0; i < pa.Count; i++) if (!Close(pa[i].Value, pb[i].Value, out where, $"{path}.{pa[i].Name}")) return false;
                return true;
            default:
                if (a.GetRawText() == b.GetRawText()) return true;
                where = $"{path}: {a.GetRawText()} vs {b.GetRawText()}"; return false;
        }
    }
}
