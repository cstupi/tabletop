using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tabletop.Management.Tests;

/// <summary>Enough of Stytch to log in: users by email, one OTP code, passwords, external ids, sessions minted with TestKeys. No claim template: a session carries account_id only once session_custom_claims set it, and keeps it across refreshes, as the real project does.</summary>
public sealed class FakeStytch : HttpMessageHandler
{
    public const string Code = "123456";
    public sealed class User { public string Id = "user-test-" + Guid.NewGuid().ToString("N")[..8]; public string Email = ""; public string? ExternalId; public string? Password; public List<string> Roles = ["stytch_user"]; }
    public readonly Dictionary<string, User> Users = new();   // by email
    public readonly List<string> Revoked = new();
    public readonly List<string> Deleted = new();
    public readonly List<string> ResetEmails = new();
    public readonly List<string> Calls = new();               // every path, so tests can count round trips
    public int Authenticates => Calls.Count(p => p == "/v1/sessions/authenticate");

    /// <summary>Test hook: the next request to this exact path gets this raw, possibly non-JSON, response instead of any normal handling. Consumed on the first match.</summary>
    public (string Path, int Status, string Body)? Break;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        Assert.StartsWith("Basic ", req.Headers.Authorization?.ToString());
        var body = req.Content is null ? new JsonObject() : JsonNode.Parse(await req.Content.ReadAsStringAsync(ct))!.AsObject();
        var path = req.RequestUri!.AbsolutePath;
        Calls.Add(path);
        if (Break is { } brk && brk.Path == path)
        {
            Break = null;
            return new HttpResponseMessage((HttpStatusCode)brk.Status) { Content = new StringContent(brk.Body, System.Text.Encoding.UTF8, "text/html") };
        }
        string S(string k) => body[k]?.GetValue<string>() ?? "";
        switch (path)
        {
            case "/v1/otps/email/login_or_create":
            {
                var created = !Users.ContainsKey(S("email"));
                var u = created ? Users[S("email")] = new User { Email = S("email") } : Users[S("email")];
                return Json(200, new { user_id = u.Id, email_id = "email-" + u.Id, user_created = created });
            }
            case "/v1/otps/authenticate":
            {
                var u = Users.Values.FirstOrDefault(x => "email-" + x.Id == S("method_id"));
                return u is null || S("code") != Code ? Error(401, "unauthorized_credentials") : Session(u, body);
            }
            case "/v1/passwords/authenticate":
                return Users.TryGetValue(S("email"), out var pu) && pu.Password == S("password") ? Session(pu, body) : Error(401, "unauthorized_credentials");
            case "/v1/passwords":
                if (Users.ContainsKey(S("email"))) return Error(400, "duplicate_email");
                if (S("password").Length < 8) return Error(400, "weak_password");
                return Session(Users[S("email")] = new User { Email = S("email"), Password = S("password") }, body);
            case "/v1/passwords/email/reset/start":
                ResetEmails.Add(S("email")); return Json(200, new { user_id = "x", email_id = "y" });
            case "/v1/passwords/email/reset":
            {
                var u = Users.Values.FirstOrDefault(x => "reset-" + x.Id == S("token"));
                if (u is null) return Error(401, "unable_to_auth_magic_link");
                u.Password = S("password"); return Session(u, body);
            }
            case "/v1/sessions/authenticate":
            {
                var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(S("session_jwt"));
                var u = Users.Values.First(x => x.Id == jwt.Subject);
                var carriedRoles = jwt.Claims.Where(c => c.Type == Identity.StytchAuthentication.RolesClaim).Select(c => c.Value).ToList();
                return Session(u, body, jwt.Claims.FirstOrDefault(c => c.Type == Identity.StytchAuthentication.AccountClaim)?.Value, carriedRoles);
            }
            case "/v1/sessions/revoke": Revoked.Add(S("session_jwt")); return Json(200, new { });
            case "/v1/users":
            {
                if (Users.ContainsKey(S("email"))) return Error(400, "duplicate_email");
                var u = Users[S("email")] = new User { Email = S("email"), ExternalId = S("external_id") };
                return Json(200, new { user_id = u.Id, email_id = "email-" + u.Id, status = "active" });
            }
            case "/v1/magic_links":
            {
                var u = Find(S("user_id"));
                return u is null ? Error(404, "user_not_found") : Json(200, new { user_id = u.Id, token = "eml-" + u.Id });
            }
            case "/v1/magic_links/authenticate":
            {
                var u = Users.Values.FirstOrDefault(x => "eml-" + x.Id == S("token"));
                return u is null ? Error(401, "unable_to_auth_magic_link") : Session(u, body);
            }
        }
        if (path.StartsWith("/v1/users/") && req.Method == HttpMethod.Get)
        {
            var u = Find(path["/v1/users/".Length..]);
            return u is null ? Error(404, "user_not_found")
                : Json(200, new { user_id = u.Id, external_id = u.ExternalId, emails = new[] { new { email_id = "email-" + u.Id, email = u.Email, verified = true } } });
        }
        if (path.StartsWith("/v1/users/") && req.Method == HttpMethod.Put)
        {
            var u = Users.Values.First(x => x.Id == path["/v1/users/".Length..]);
            u.ExternalId = S("external_id"); return Json(200, new { user_id = u.Id });
        }
        if (path.StartsWith("/v1/users/") && req.Method == HttpMethod.Delete)
        {
            var key = path["/v1/users/".Length..];
            var u = Users.Values.FirstOrDefault(x => x.Id == key || x.ExternalId == key);
            if (u is null) return Error(404, "user_not_found");
            Users.Remove(u.Email); Deleted.Add(key); return Json(200, new { user_id = u.Id });
        }
        return Error(404, "unknown_route");
    }

    /// <summary>By Stytch user id or by external id, which the get, the delete and the embeddable magic link all accept interchangeably.</summary>
    private User? Find(string key) => Users.Values.FirstOrDefault(x => x.Id == key || x.ExternalId == key);

    /// <summary>A session's account_id and roles are whatever session_custom_claims set on it, carried over from the incoming JWT on a refresh.
    /// The user object lists the user's RBAC role ids in roles, as Stytch's authenticate responses do.</summary>
    private HttpResponseMessage Session(User u, JsonObject body, string? carried = null, List<string>? carriedRoles = null)
    {
        var custom = body["session_custom_claims"];
        var claim = custom?[Identity.StytchAuthentication.AccountClaim]?.GetValue<string>() ?? carried;
        var roles = custom?[Identity.StytchAuthentication.RolesClaim] is JsonArray stamped ? stamped.Select(x => x!.GetValue<string>()).ToList() : carriedRoles;
        var jwt = TestKeys.MintForUser(u.Id, Guid.TryParse(claim, out var account) ? account : null, roles);
        return Json(200, new { user_id = u.Id, session_jwt = jwt, session_token = "tok-" + u.Id,
            user = new { user_id = u.Id, external_id = u.ExternalId, emails = new[] { new { email_id = "email-" + u.Id, email = u.Email, verified = true } }, roles = u.Roles } });
    }

    private static HttpResponseMessage Json(int status, object o) =>
        new((HttpStatusCode)status) { Content = new StringContent(JsonSerializer.Serialize(o), System.Text.Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Error(int status, string type) => Json(status, new { status_code = status, error_type = type, error_message = type });
}
