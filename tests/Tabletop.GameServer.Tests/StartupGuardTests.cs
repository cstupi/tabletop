using Microsoft.Extensions.Configuration;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

public class StartupGuardTests
{
    private static readonly Dictionary<string, string?> Production = new()
    {
        ["TABLETOP_DB_CONNECTION"] = "Host=postgres;Database=tabletop;Username=tabletop_game;Password=a-long-random-password",
        ["TABLETOP_STYTCH_PROJECT_ID"] = "project-live-1",
        ["TABLETOP_STYTCH_BASE_URL"] = "https://api.stytch.com",
    };

    private static IConfiguration C(Dictionary<string, string?> s) => new ConfigurationBuilder().AddInMemoryCollection(s).Build();
    private static Dictionary<string, string?> With(Dictionary<string, string?> b, string key, string? value) => new(b) { [key] = value };
    private static IReadOnlyList<string> Problems(string environment, Dictionary<string, string?> s) => StartupGuard.Problems(environment, C(s));

    [Fact]
    public void Production_settings_pass() => Assert.Empty(Problems("Production", Production));

    [Theory]
    [InlineData("TABLETOP_STYTCH_BASE_URL", "https://test.stytch.com", "TABLETOP_STYTCH")]
    [InlineData("TABLETOP_STYTCH_BASE_URL", null, "TABLETOP_STYTCH")]                                  // the default is the test environment
    [InlineData("TABLETOP_STYTCH_JWKS_URL", "http://api.stytch.com/v1/sessions/jwks/project-live-1", "TABLETOP_STYTCH")]
    [InlineData("TABLETOP_DB_CONNECTION", "Host=postgres;Username=tabletop;Password=tabletop", "TABLETOP_DB_CONNECTION")]
    [InlineData("TABLETOP_DB_CONNECTION", "Host=postgres;Username=tabletop_game", "TABLETOP_DB_CONNECTION")]
    [InlineData("TABLETOP_DEV_MODE", "1", "TABLETOP_DEV_MODE")]
    public void Production_refuses_development_settings(string key, string? value, string named) =>
        Assert.Contains(Problems("Production", With(Production, key, value)), p => p.Contains(named));

    [Fact]
    public void Development_needs_the_explicit_opt_in()
    {
        var dev = With(Production, "TABLETOP_STYTCH_BASE_URL", "https://test.stytch.com");
        Assert.Contains(Problems("Development", dev), p => p.Contains("TABLETOP_DEV_MODE=1"));
        Assert.Empty(Problems("Development", With(dev, "TABLETOP_DEV_MODE", "1")));
        Assert.Empty(Problems("Test", dev));                                                          // the test host's environment
        Assert.NotEmpty(Problems("Staging", dev));
    }

    [Fact]
    public void Set_dice_needs_development_and_the_opt_in()
    {
        var dev = With(Production, "TABLETOP_DEV_MODE", "1");
        Assert.True(GameServerOptions.FromEnvironment(C(dev), StartupGuard.DevMode("Development", C(dev))).AcceptsSetDice);
        Assert.False(StartupGuard.DevMode("Development", C(Production)));
        Assert.False(StartupGuard.DevMode("Production", C(dev)));
    }

    [Fact]
    public void Enforce_names_every_problem()
    {
        var bad = With(With(Production, "TABLETOP_STYTCH_BASE_URL", null), "TABLETOP_DB_CONNECTION", "Host=x;Password=tabletop");
        var e = Assert.Throws<InvalidOperationException>(() => StartupGuard.Enforce("Production", C(bad)));
        Assert.Contains("TABLETOP_STYTCH", e.Message);
        Assert.Contains("TABLETOP_DB_CONNECTION", e.Message);
    }

    [Fact]
    public async Task Healthz_answers_ok()
    {
        using var factory = new GameServerFactory();
        var response = await factory.CreateClient().GetAsync("/healthz");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}

public class DotEnvTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tabletop-dotenv-").FullName;
    private readonly string _key = "TABLETOP_DOTENV_TEST_" + Guid.NewGuid().ToString("N");
    public void Dispose() { Environment.SetEnvironmentVariable(_key, null); Directory.Delete(_root, true); }

    [Fact]
    public void Reads_only_the_given_directory_and_only_for_development()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), $"{_key}=dev\n");
        var child = Directory.CreateDirectory(Path.Combine(_root, "child")).FullName;
        DotEnv.LoadForDevelopment(child, "Development");
        DotEnv.LoadForDevelopment(_root, "Production");
        DotEnv.LoadForDevelopment(_root, null);
        Assert.Null(Environment.GetEnvironmentVariable(_key));
        DotEnv.LoadForDevelopment(_root, "Development");
        Assert.Equal("dev", Environment.GetEnvironmentVariable(_key));
    }
}
