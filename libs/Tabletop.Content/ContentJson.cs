using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Content;

/// <summary>The JSON conventions for Geometry and Content types. Protocol builds its options on these.</summary>
public static class ContentJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions());

    /// <param name="integerEnums">False on the game socket (WireJson): an enum is read by name only, as an integer could name no member.
    /// Management keeps integers readable and checks Enum.IsDefined itself, so its own refusal names the field.</param>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options, bool integerEnums = true)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: integerEnums));
        options.AllowOutOfOrderMetadataProperties = true;
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("null");
}
