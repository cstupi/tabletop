using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tabletop.Management.Tests;

/// <summary>The test host. Later tasks add the test database, the fake JWKS and the fake Stytch here.</summary>
public class ManagementFactory : WebApplicationFactory<Program>
{
    public TestDatabase Database { get; } = new();
    public FakeStytch Stytch { get; } = new();
    public string HostEnvironment { get; init; } = "Testing";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironment);
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["STYTCH_PROJECT_ID"] = TestKeys.ProjectId,
            ["STYTCH_SECRET"] = "secret-test",
            ["DB_CONNECTION"] = Database.ConnectionString,
        }));
        builder.ConfigureTestServices(s =>
        {
            s.AddHttpClient(Identity.StytchAuthentication.JwksClient).ConfigurePrimaryHttpMessageHandler(() => TestKeys.JwksHandler);
            s.AddHttpClient(Identity.StytchClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Stytch);
            s.AddHttpClient(Content.Armies.ArmyForgeClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new FakeArmyForge());
        });
    }

    public async Task<HttpClient> AuthenticatedClient(Guid accountId, string name = "Test")
    {
        await Services.GetRequiredService<Identity.IAccountRepository>().Insert(accountId, name);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestKeys.Mint(accountId));
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Database.Dispose();
    }
}
