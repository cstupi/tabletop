using Tabletop.Management.Http;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class ClientVersionTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public ClientVersionTests(ManagementFactory f) => _f = f;

    [Theory]
    [InlineData(null, 1, ClientVersionVerdict.Accept)]
    [InlineData("1", 1, ClientVersionVerdict.Accept)]
    [InlineData("2", 1, ClientVersionVerdict.Accept)]
    [InlineData("0", 1, ClientVersionVerdict.UpgradeRequired)]
    [InlineData("abc", 1, ClientVersionVerdict.Malformed)]
    public void Decides_from_the_header(string? header, int minimum, ClientVersionVerdict expected) =>
        Assert.Equal(expected, ClientVersion.Decide(header, minimum));

    [Fact]
    public async Task An_old_client_gets_426_before_anything_else_runs()
    {
        var client = _f.CreateClient();
        client.DefaultRequestHeaders.Add(ProtocolVersion.Header, "0");
        var response = await client.GetAsync("/healthz");
        Assert.Equal(ProtocolVersion.UpgradeRequiredStatusCode, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_current_client_passes()
    {
        var client = _f.CreateClient();
        client.DefaultRequestHeaders.Add(ProtocolVersion.Header, ProtocolVersion.Current.ToString());
        Assert.True((await client.GetAsync("/healthz")).IsSuccessStatusCode);
    }
}
