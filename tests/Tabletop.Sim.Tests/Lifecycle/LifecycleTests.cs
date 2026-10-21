using Tabletop.Sim.Lifecycle;

namespace Tabletop.Sim.Tests.Lifecycle;

/// <summary>
/// Every lifecycle shape against the running stack (docker compose up -d; both servers in dev mode), as `sim lifecycle` plays it: management
/// over HTTP, the game over the socket, nothing written to Postgres directly. Each shape deletes the game and armies it made.
/// </summary>
[Trait("Category", "E2E")]
public class LifecycleTests
{
    public static TheoryData<string> Shapes() => new(LifecycleShapes.All.Select(s => s.Name));

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task Each_shape_plays_from_creation_to_deletion_by_the_clients_path(string shape)
    {
        var results = await new LifecycleRunner(LifecycleEndpoints.FromEnvironment()).RunAsync(LifecycleShapes.Select([shape]), null, CancellationToken.None);
        var result = Assert.Single(results);
        Assert.True(result.Passed, string.Join("\n", result.Failures));
    }
}
