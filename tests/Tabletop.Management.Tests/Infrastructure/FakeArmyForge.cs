using System.Collections.Concurrent;
using System.Net;

namespace Tabletop.Management.Tests;

/// <summary>Serves the fixture for id c2-6YsWRH3py, a test's own export for an id it put in Lists, a network failure for
/// Unreachable, and 404 for anything else.</summary>
public sealed class FakeArmyForge : HttpMessageHandler
{
    public const string Unreachable = "unreachable-list";
    /// <summary>Extra exports by list id; tests add ids of their own.</summary>
    public static readonly ConcurrentDictionary<string, string> Lists = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var id = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["id"];
        if (id == Unreachable) throw new HttpRequestException("connection refused by 10.0.0.7:443");
        var json = id == "c2-6YsWRH3py" ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "army-forge-c2-6YsWRH3py.json"))
            : id is not null && Lists.TryGetValue(id, out var own) ? own : null;
        if (json is null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}
