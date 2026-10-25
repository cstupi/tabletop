using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tabletop.Client.Tests;

/// <summary>
/// The calls agreement's normalisation (same-calls part 3, Task 8); viewer/tests/callNormalise.ts is the same algorithm. Nulls drop, keys sort
/// ordinally, numbers round to nine places, and an id no log file or case input holds is renamed new:n in the order the canonical value meets it.
/// </summary>
public static partial class CallNormalise
{
    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)]
    private static partial Regex GuidShape();

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex GuidInside();

    /// <summary>Every GUID-shaped string in these JSON texts (lower case).</summary>
    public static HashSet<string> KnownIds(params string[] json)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in json)
            foreach (Match m in GuidInside().Matches(text)) ids.Add(m.Value.ToLowerInvariant());
        return ids;
    }

    /// <summary>Keys sorted, nulls dropped, numbers rounded, unknown ids renamed new:n in canonical order.</summary>
    public static JsonNode Of(JsonNode message, IReadOnlySet<string> known)
    {
        var sorted = Sort(message)!;
        var fresh = new Dictionary<string, string>(StringComparer.Ordinal);
        string Rename(string s)
        {
            if (!GuidShape().IsMatch(s)) return s;
            var lower = s.ToLowerInvariant();
            if (known.Contains(lower)) return s;
            if (!fresh.TryGetValue(lower, out var name)) fresh[lower] = name = "new:" + (fresh.Count + 1);
            return name;
        }
        return Walk(sorted, Rename)!;
    }

    /// <summary>The canonical text: compact, keys in the order Of left them, numbers as JavaScript prints them.</summary>
    public static string Text(JsonNode normalised)
    {
        var sb = new StringBuilder();
        Write(sb, normalised);
        return sb.ToString();
    }

    static JsonNode? Sort(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                var result = new JsonObject();
                foreach (var kv in o.Where(kv => kv.Value is not null).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    result[kv.Key] = Sort(kv.Value);
                return result;
            case JsonArray a:
                var list = new JsonArray();
                foreach (var item in a) list.Add(Sort(item));
                return list;
            case JsonValue v when v.TryGetValue<JsonElement>(out var e) && e.ValueKind == JsonValueKind.Number:
                return JsonValue.Create(Round(e.GetDouble()));
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                return JsonValue.Create(Round(v.GetValue<double>()));
            case JsonValue v:
                return v.DeepClone();
            default:
                return null;
        }
    }

    static double Round(double x) => x == 0 ? 0 : Math.Sign(x) * (Math.Round(Math.Abs(x) * 1e9, MidpointRounding.AwayFromZero) / 1e9);

    static JsonNode? Walk(JsonNode? node, Func<string, string> rename)
    {
        switch (node)
        {
            case JsonObject o:
                var result = new JsonObject();
                foreach (var kv in o) result[rename(kv.Key)] = Walk(kv.Value, rename);
                return result;
            case JsonArray a:
                var list = new JsonArray();
                foreach (var item in a) list.Add(Walk(item, rename));
                return list;
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                return JsonValue.Create(rename(v.GetValue<string>()));
            default:
                return node?.DeepClone();
        }
    }

    static readonly JsonSerializerOptions StringOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static void Write(StringBuilder sb, JsonNode? node)
    {
        switch (node)
        {
            case null: sb.Append("null"); break;
            case JsonObject o:
                sb.Append('{');
                var first = true;
                foreach (var kv in o)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonSerializer.Serialize(kv.Key, StringOptions)).Append(':');
                    Write(sb, kv.Value);
                }
                sb.Append('}');
                break;
            case JsonArray a:
                sb.Append('[');
                for (var i = 0; i < a.Count; i++) { if (i > 0) sb.Append(','); Write(sb, a[i]); }
                sb.Append(']');
                break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                sb.Append(Number(v.GetValue<double>()));
                break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.True: sb.Append("true"); break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.False: sb.Append("false"); break;
            case JsonValue v: sb.Append(JsonSerializer.Serialize(v.GetValue<string>(), StringOptions)); break;
        }
    }

    /// <summary>The shortest round-trip text, in JavaScript's exponent form where it uses one.</summary>
    static string Number(double x)
    {
        var text = x.ToString("R", CultureInfo.InvariantCulture);
        var e = text.IndexOf('E');
        if (e < 0) return text;
        var exponent = int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture);
        return text[..e] + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent);
    }
}
