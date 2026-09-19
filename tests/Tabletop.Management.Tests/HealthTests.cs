namespace Tabletop.Management.Tests;

public class HealthTests : IClassFixture<ManagementFactory>
{
    private readonly HttpClient _client;
    public HealthTests(ManagementFactory f) => _client = f.CreateClient();

    [Fact]
    public async Task Healthz_answers_ok()
    {
        var response = await _client.GetAsync("/healthz");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
