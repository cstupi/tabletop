using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tabletop.Management.Content.Armies;

/// <summary>Army Forge did not deliver a list: Status is its HTTP status when it answered with one, null when it could not be
/// reached, timed out, or sent more than ArmyForgeClient.MaxBytes.</summary>
public sealed class ArmyForgeException : Exception
{
    public int? Status { get; }
    public ArmyForgeException(int status) : base($"Army Forge answered {status}") => Status = status;
    public ArmyForgeException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed partial class ArmyForgeClient(HttpClient http, TimeSpan? timeout = null)
{
    public const string HttpClientName = "army-forge";
    public const string BaseUrl = "https://army-forge.onepagerules.com";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    /// <summary>The largest export read; a real list is tens of kilobytes.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    [GeneratedRegex(@"[?&]id=([A-Za-z0-9_-]+)")] private static partial Regex IdParam();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{4,64}$")] private static partial Regex BareId();

    /// <summary>The list id from a share link (the id= parameter) or a bare id; null when neither.</summary>
    public static string? ListId(string source) =>
        IdParam().Match(source) is { Success: true } m ? m.Groups[1].Value : BareId().IsMatch(source) ? source : null;

    public static AfList Parse(string json) => JsonSerializer.Deserialize<AfList>(json, Json) ?? throw new JsonException("empty export");
    public static AfList Parse(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<AfList>(json, Json) ?? throw new JsonException("empty export");

    /// <summary>The list, or ArmyForgeException; a body that is not an export throws JsonException.</summary>
    public async Task<AfList> Fetch(string listId)
    {
        using var cts = new CancellationTokenSource(_timeout);
        byte[] body;
        try
        {
            using var response = await http.GetAsync($"{BaseUrl}/api/tts?id={Uri.EscapeDataString(listId)}", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode) throw new ArmyForgeException((int)response.StatusCode);
            if (response.Content.Headers.ContentLength > MaxBytes) throw new ArmyForgeException($"Army Forge declared {response.Content.Headers.ContentLength} bytes");
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            body = await ReadAtMost(stream, MaxBytes, cts.Token);
        }
        catch (OperationCanceledException e) when (cts.IsCancellationRequested) { throw new ArmyForgeException($"Army Forge took longer than {_timeout}", e); }
        catch (HttpRequestException e) { throw new ArmyForgeException("Army Forge unreachable", e); }
        return Parse(body);
    }

    /// <summary>The stream's bytes, refusing the read once it passes max whatever the headers said.</summary>
    private static async Task<byte[]> ReadAtMost(Stream stream, int max, CancellationToken ct)
    {
        using var into = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (into.Length + n > max) throw new ArmyForgeException($"Army Forge sent more than {max} bytes");
            into.Write(buffer, 0, n);
        }
        return into.ToArray();
    }
}
