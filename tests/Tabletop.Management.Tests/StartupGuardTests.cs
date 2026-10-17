using Microsoft.Extensions.Configuration;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Tests;

public class StartupGuardTests
{
    private static readonly Dictionary<string, string?> Production = new()
    {
        ["DB_CONNECTION"] = "Host=postgres;Database=tabletop;Username=tabletop_management;Password=a-long-random-password",
        ["S3_ENDPOINT"] = "http://minio:9000", ["S3_PUBLIC_ENDPOINT"] = "https://blobs.example.org", ["S3_BUCKET"] = "tabletop-blobs",
        ["S3_ACCESS_KEY"] = "tabletop-app-prod", ["S3_SECRET_KEY"] = "another-long-random-secret",
        ["STYTCH_PROJECT_ID"] = "project-live-1", ["STYTCH_SECRET"] = "secret-live-1", ["STYTCH_BASE_URL"] = "https://api.stytch.com",
        ["WEB_URL"] = "https://tabletop.example.org",
    };

    private static IReadOnlyList<string> Problems(string environment, Dictionary<string, string?> settings)
    {
        var c = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return StartupGuard.Problems(environment, c, ManagementOptions.From(c));
    }

    private static Dictionary<string, string?> With(Dictionary<string, string?> b, string key, string? value) => new(b) { [key] = value };

    [Fact]
    public void Production_settings_pass() => Assert.Empty(Problems("Production", Production));

    [Theory]
    [InlineData("STYTCH_BASE_URL", "https://test.stytch.com", "TABLETOP_STYTCH_BASE_URL")]
    [InlineData("STYTCH_BASE_URL", null, "TABLETOP_STYTCH_BASE_URL")]                       // the default is the test project's URL
    [InlineData("WEB_URL", "http://tabletop.example.org", "TABLETOP_WEB_URL")]
    [InlineData("WEB_URL", "https://localhost:5173", "TABLETOP_WEB_URL")]
    [InlineData("WEB_URL", null, "TABLETOP_WEB_URL")]
    [InlineData("S3_PUBLIC_ENDPOINT", "http://blobs.example.org", "TABLETOP_S3_PUBLIC_ENDPOINT")]
    [InlineData("S3_PUBLIC_ENDPOINT", "https://127.0.0.1:9000", "TABLETOP_S3_PUBLIC_ENDPOINT")]
    [InlineData("S3_PUBLIC_ENDPOINT", null, "TABLETOP_S3_PUBLIC_ENDPOINT")]                  // then presigned URLs name S3_ENDPOINT, http://minio:9000
    [InlineData("S3_ACCESS_KEY", "tabletop", "TABLETOP_S3_ACCESS_KEY")]
    [InlineData("S3_SECRET_KEY", "tabletop-secret", "TABLETOP_S3_SECRET_KEY")]
    [InlineData("DB_CONNECTION", "Host=postgres;Database=tabletop;Username=tabletop;Password=tabletop", "TABLETOP_DB_CONNECTION")]
    [InlineData("DB_CONNECTION", "Host=postgres;Database=tabletop;Username=tabletop_management", "TABLETOP_DB_CONNECTION")]
    [InlineData("DEV_MODE", "1", "TABLETOP_DEV_MODE")]
    public void Production_refuses_development_settings(string key, string? value, string named)
    {
        var problems = Problems("Production", With(Production, key, value));
        Assert.Contains(problems, p => p.Contains(named));
    }

    [Fact]
    public void Every_environment_but_development_and_the_test_host_is_held_to_production()
    {
        Assert.NotEmpty(Problems("Staging", With(Production, "WEB_URL", "http://localhost:5173")));
        Assert.Empty(Problems(StartupGuard.TestEnvironment, With(Production, "WEB_URL", "http://localhost:5173")));
    }

    [Fact]
    public void Development_needs_the_explicit_opt_in()
    {
        var dev = With(With(Production, "WEB_URL", "http://localhost:5173"), "S3_ACCESS_KEY", "tabletop");
        Assert.Contains(Problems("Development", dev), p => p.Contains("TABLETOP_DEV_MODE=1"));
        Assert.Empty(Problems("Development", With(dev, "DEV_MODE", "1")));
    }

    [Fact]
    public void Dev_mode_is_development_and_the_opt_in_together()
    {
        static IConfiguration C(string? v) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DEV_MODE"] = v }).Build();
        Assert.True(StartupGuard.DevMode("Development", C("1")));
        Assert.False(StartupGuard.DevMode("Development", C(null)));
        Assert.False(StartupGuard.DevMode("Development", C("true")));
        Assert.False(StartupGuard.DevMode("Production", C("1")));
    }

    [Fact]
    public void The_migration_run_checks_only_the_database()
    {
        Assert.Empty(StartupGuard.DatabaseProblems("Production", "Host=postgres;Username=tabletop_owner;Password=a-long-random-password"));
        Assert.NotEmpty(StartupGuard.DatabaseProblems("Production", "Host=postgres;Username=tabletop;Password=tabletop"));
        Assert.Empty(StartupGuard.DatabaseProblems("Development", "Host=postgres;Username=tabletop;Password=tabletop"));
    }

    [Fact]
    public void A_refusal_names_every_problem()
    {
        var bad = With(With(Production, "WEB_URL", "http://localhost:5173"), "S3_SECRET_KEY", "tabletop-secret");
        var c = new ConfigurationBuilder().AddInMemoryCollection(bad).Build();
        var e = Assert.Throws<InvalidOperationException>(() => StartupGuard.Enforce("Production", c, ManagementOptions.From(c)));
        Assert.Contains("TABLETOP_WEB_URL", e.Message);
        Assert.Contains("TABLETOP_S3_SECRET_KEY", e.Message);
    }
}

public class DotEnvTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tabletop-dotenv-").FullName;
    private readonly string _key = "TABLETOP_DOTENV_TEST_" + Guid.NewGuid().ToString("N");
    public void Dispose() { Environment.SetEnvironmentVariable(_key, null); Directory.Delete(_root, true); }

    [Fact]
    public void Reads_only_the_given_directory_never_a_parent()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), $"{_key}=parent\n");
        var child = Directory.CreateDirectory(Path.Combine(_root, "child")).FullName;
        DotEnv.Load(child);
        Assert.Null(Environment.GetEnvironmentVariable(_key));
        DotEnv.Load(_root);
        Assert.Equal("parent", Environment.GetEnvironmentVariable(_key));
    }

    [Fact]
    public void Loads_only_when_the_process_asks_for_development()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), $"{_key}=dev\n");
        DotEnv.LoadForDevelopment(_root, environment: "Production");
        DotEnv.LoadForDevelopment(_root, environment: null);
        Assert.Null(Environment.GetEnvironmentVariable(_key));
        DotEnv.LoadForDevelopment(_root, environment: "Development");
        Assert.Equal("dev", Environment.GetEnvironmentVariable(_key));
    }
}
