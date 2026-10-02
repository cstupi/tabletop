using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Sim.Recording;

/// <summary>
/// The recording's own JSON. Not WireJson: this is not a wire format, and it carries no type discriminator
/// because every shape here is closed at compile time and named by a string field instead.
/// </summary>
public static class RecordingJson
{
    public const int SchemaVersion = 1;

    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Serialize(Recording recording) => JsonSerializer.Serialize(recording, Options);
    public static Recording Deserialize(string json) =>
        JsonSerializer.Deserialize<Recording>(json, Options) ?? throw new InvalidDataException("empty recording");

    public static async Task WriteAsync(Recording recording, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var file = File.Create(path);
        await JsonSerializer.SerializeAsync(file, recording, Options, ct);
    }

    public static async Task<Recording> ReadAsync(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Recording>(file, Options, ct)
            ?? throw new InvalidDataException($"{path} is not a recording");
    }
}
