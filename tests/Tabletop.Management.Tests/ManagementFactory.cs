using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Tabletop.Management.Tests;

/// <summary>The test host. Later tasks add the test database, the fake JWKS and the fake Stytch here.</summary>
public class ManagementFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["STYTCH_PROJECT_ID"] = "project-test-00000000-0000-0000-0000-000000000000",
            ["STYTCH_SECRET"] = "secret-test",
        }));
    }
}
