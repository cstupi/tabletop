using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Layouts;
using Tabletop.Management.Games;
using Tabletop.Management.Identity;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Tests;

/// <summary>The test host over the in-memory fakes: no Postgres, no MinIO, no Stytch, no Army Forge. The database and S3
/// settings name a closed port, so anything that still reaches for them fails at once.</summary>
public class ManagementFactory : WebApplicationFactory<Program>
{
    public const string Unreachable = "Host=127.0.0.1;Port=1;Username=none;Password=none;Database=none;Timeout=1";
    public InMemoryRows Rows { get; } = new();
    public InMemoryBlobStore Blobs { get; } = new();
    public FakeStytch Stytch { get; } = new();
    public string HostEnvironment { get; init; } = "Testing";
    /// <summary>Settings on top of the defaults below, by name without the TABLETOP_ prefix.</summary>
    public Dictionary<string, string?> Settings { get; init; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironment);
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["STYTCH_PROJECT_ID"] = TestKeys.ProjectId,
            ["STYTCH_SECRET"] = "secret-test",
            ["DB_CONNECTION"] = Unreachable,
            ["S3_ENDPOINT"] = "http://127.0.0.1:1",
            ["S3_BUCKET"] = "none",
            ["S3_ACCESS_KEY"] = "none",
            ["S3_SECRET_KEY"] = "none",
        }).AddInMemoryCollection(Settings));
        builder.ConfigureTestServices(s =>
        {
            s.AddHttpClient(StytchAuthentication.JwksClient).ConfigurePrimaryHttpMessageHandler(() => TestKeys.JwksHandler);
            s.AddHttpClient(StytchClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Stytch);
            s.AddHttpClient(ArmyForgeClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new FakeArmyForge());
            s.RemoveAll<Database>();
            s.Replace(ServiceDescriptor.Singleton<IMigrator, NoMigrations>());
            s.Replace(ServiceDescriptor.Singleton<IBlobStore>(Blobs));
            s.AddSingleton(Rows);
            s.Replace(ServiceDescriptor.Singleton<IAccountRepository, InMemoryAccountRepository>());
            s.Replace(ServiceDescriptor.Singleton<IAssetRepository, InMemoryAssetRepository>());
            s.Replace(ServiceDescriptor.Singleton<IFolderRepository, InMemoryFolderRepository>());
            s.Replace(ServiceDescriptor.Singleton<ICatalogKinds, InMemoryCatalogKinds>());
            s.Replace(ServiceDescriptor.Singleton<IArmyRepository, InMemoryArmyRepository>());
            s.Replace(ServiceDescriptor.Singleton<ILayoutRepository, InMemoryLayoutRepository>());
            s.Replace(ServiceDescriptor.Singleton<IGameRepository, InMemoryGameRepository>());
        });
    }

    public async Task<HttpClient> AuthenticatedClient(Guid accountId, string name = "Test")
    {
        await Services.GetRequiredService<IAccountRepository>().Insert(accountId, name);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestKeys.Mint(accountId));
        return client;
    }

    private sealed class NoMigrations : IMigrator
    {
        public void Run() { }
        public IReadOnlyList<string> Pending() => [];
    }
}
