using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tabletop.Management.Content.Armies;

public sealed class ArmyForgeException(int status) : Exception($"Army Forge answered {status}") { public int Status { get; } = status; }

public sealed partial class ArmyForgeClient(HttpClient http)
{
    public const string HttpClientName = "army-forge";
    public const string BaseUrl = "https://army-forge.onepagerules.com";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    [GeneratedRegex(@"[?&]id=([A-Za-z0-9_-]+)")] private static partial Regex IdParam();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{4,64}$")] private static partial Regex BareId();

    /// <summary>The list id from a share link (the id= parameter) or a bare id; null when neither.</summary>
    public static string? ListId(string source) =>
        IdParam().Match(source) is { Success: true } m ? m.Groups[1].Value : BareId().IsMatch(source) ? source : null;

    public static AfList Parse(string json) => JsonSerializer.Deserialize<AfList>(json, Json) ?? throw new JsonException("empty export");

    public async Task<AfList> Fetch(string listId)
    {
        using var response = await http.GetAsync($"{BaseUrl}/api/tts?id={Uri.EscapeDataString(listId)}");
        if (!response.IsSuccessStatusCode) throw new ArmyForgeException((int)response.StatusCode);
        return Parse(await response.Content.ReadAsStringAsync());
    }
}
