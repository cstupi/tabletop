using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Identity;

public sealed record StytchEmail(string Email);
public sealed record StytchUser(string UserId, string? ExternalId, IReadOnlyList<StytchEmail> Emails);
public sealed record StytchSession(string SessionJwt, StytchUser User);
public sealed record OtpSent(string MethodId, string UserId, bool UserCreated);

public sealed class StytchException(int status, string errorType, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string ErrorType { get; } = errorType;
}

/// <summary>The only code in the project that knows Stytch exists. Basic auth, snake_case JSON, one method per endpoint.</summary>
public sealed class StytchClient(HttpClient http, ManagementOptions options)
{
    public const string HttpClientName = "stytch";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async Task<OtpSent> SendEmailOtp(string email)
    {
        var r = await Call(HttpMethod.Post, "/v1/otps/email/login_or_create", new { email, expiration_minutes = 10 });
        return new OtpSent(r.GetProperty("email_id").GetString()!, r.GetProperty("user_id").GetString()!, r.GetProperty("user_created").GetBoolean());
    }

    public Task<StytchSession> AuthenticateOtp(string methodId, string code) =>
        Session(HttpMethod.Post, "/v1/otps/authenticate", new { method_id = methodId, code, session_duration_minutes = options.SessionMinutes });
    public Task<StytchSession> AuthenticatePassword(string email, string password) =>
        Session(HttpMethod.Post, "/v1/passwords/authenticate", new { email, password, session_duration_minutes = options.SessionMinutes });
    public Task<StytchSession> CreatePassword(string email, string password) =>
        Session(HttpMethod.Post, "/v1/passwords", new { email, password, session_duration_minutes = options.SessionMinutes });
    public Task StartPasswordReset(string email, string redirectUrl) =>
        Call(HttpMethod.Post, "/v1/passwords/email/reset/start", new { email, reset_password_redirect_url = redirectUrl });
    public Task<StytchSession> CompletePasswordReset(string token, string password) =>
        Session(HttpMethod.Post, "/v1/passwords/email/reset", new { token, password, session_duration_minutes = options.SessionMinutes });
    /// <summary>Extends the session and mints a fresh JWT; the project's claim template runs on every mint, so nothing is passed here.</summary>
    public Task<StytchSession> RefreshSession(string sessionJwt) =>
        Session(HttpMethod.Post, "/v1/sessions/authenticate", new { session_jwt = sessionJwt, session_duration_minutes = options.SessionMinutes });
    public Task RevokeSession(string sessionJwt) => Call(HttpMethod.Post, "/v1/sessions/revoke", new { session_jwt = sessionJwt });
    public Task SetExternalId(string userId, string externalId) => Call(HttpMethod.Put, $"/v1/users/{userId}", new { external_id = externalId });
    public Task DeleteUser(string externalId) => Call(HttpMethod.Delete, $"/v1/users/{externalId}", null);

    // The four below are reached only from POST /v1/dev/sessions, which exists only in a development environment.
    // They live here because this class holds the Basic-auth credential and nothing else may.
    public async Task<string?> FindUserId(string externalId)
    {
        try { return (await Call(HttpMethod.Get, $"/v1/users/{externalId}", null)).GetProperty("user_id").GetString(); }
        catch (StytchException e) when (e.Status == 404) { return null; }
    }
    public async Task<string> CreateUser(string email, string externalId) =>
        (await Call(HttpMethod.Post, "/v1/users", new { email, external_id = externalId })).GetProperty("user_id").GetString()!;
    /// <summary>An embeddable magic link: Stytch returns the token instead of emailing it. Consumer API only.</summary>
    public async Task<string> CreateEmbeddableToken(string userId) =>
        (await Call(HttpMethod.Post, "/v1/magic_links", new { user_id = userId, expiration_minutes = 5 })).GetProperty("token").GetString()!;
    public Task<StytchSession> AuthenticateMagicLink(string token) =>
        Session(HttpMethod.Post, "/v1/magic_links/authenticate", new { token, session_duration_minutes = options.SessionMinutes });

    private async Task<StytchSession> Session(HttpMethod method, string path, object body)
    {
        var r = await Call(method, path, body);
        var user = r.GetProperty("user");
        var emails = user.TryGetProperty("emails", out var e) ? e.EnumerateArray().Select(x => new StytchEmail(x.GetProperty("email").GetString()!)).ToList() : [];
        var externalId = user.TryGetProperty("external_id", out var ext) && ext.ValueKind == JsonValueKind.String ? ext.GetString() : null;
        return new StytchSession(r.GetProperty("session_jwt").GetString()!, new StytchUser(user.GetProperty("user_id").GetString()!, string.IsNullOrEmpty(externalId) ? null : externalId, emails));
    }

    private async Task<JsonElement> Call(HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, options.StytchBaseUrl.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.StytchProjectId}:{options.StytchSecret}")));
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try { response = await http.SendAsync(request); }
        // A transport failure (DNS, connect, timeout) never reaches Stytch at all; report it the
        // same way an upstream 5xx is reported, rather than letting it fall through as a 500.
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or SocketException)
        {
            throw new StytchException(502, "transport", e.Message);
        }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            JsonElement json;
            try
            {
                json = text.Length == 0 ? JsonDocument.Parse("{}").RootElement : JsonDocument.Parse(text).RootElement.Clone();
            }
            catch (JsonException)
            {
                // A non-JSON body (a proxy's HTML error page) still needs to fail the documented
                // way: keep the real status when it is already a server error, otherwise call it 502.
                var status = (int)response.StatusCode >= 500 ? (int)response.StatusCode : 502;
                throw new StytchException(status, "unparseable_response", text.Length > 0 ? text : "empty body");
            }
            if (response.IsSuccessStatusCode) return json;
            var type = json.TryGetProperty("error_type", out var t) ? t.GetString() ?? "unknown" : "unknown";
            var message = json.TryGetProperty("error_message", out var m) ? m.GetString() ?? type : type;
            throw new StytchException((int)response.StatusCode, type, message);
        }
    }
}
