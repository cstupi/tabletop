using System.Net;

namespace Tabletop.Management.Tests;

/// <summary>Serves the fixture for id c2-6YsWRH3py and 404 for anything else.</summary>
public sealed class FakeArmyForge : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var id = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["id"];
        if (id != "c2-6YsWRH3py") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "army-forge-c2-6YsWRH3py.json"));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}
