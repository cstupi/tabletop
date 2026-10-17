using System.Net;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class GameRowActionsTests
{
    static readonly Guid Me = Guid.CreateVersion7();
    static GameSummary Row(Guid by, GameStatus status) => new(Guid.CreateVersion7(), "g", status, by, DateTimeOffset.UnixEpoch, 2);

    [Fact]
    public void The_creator_ends_a_game_not_yet_ended_and_deletes_any_of_theirs()
    {
        Assert.Equal((true, true), GameRowActions.Of(Row(Me, GameStatus.Playing), Me) is var a ? (a.CanEnd, a.CanDelete) : default);
        Assert.Equal((false, true), GameRowActions.Of(Row(Me, GameStatus.Ended), Me) is var b ? (b.CanEnd, b.CanDelete) : default);
        Assert.Equal((false, false), GameRowActions.Of(Row(Guid.CreateVersion7(), GameStatus.Lobby), Me) is var c ? (c.CanEnd, c.CanDelete) : default);
        Assert.Equal((false, false), GameRowActions.Of(null, Me) is var d ? (d.CanEnd, d.CanDelete) : default);
    }

    [Fact]
    public async Task End_posts_and_delete_deletes()
    {
        var f = new FakeManagement(); var c = new ManagementClient(new HttpClient(f), new Uri("http://localhost:5100"));
        var id = Guid.CreateVersion7();
        f.Answer(HttpStatusCode.NoContent).Answer(HttpStatusCode.NoContent);
        await c.EndGame(id); await c.DeleteGame(id);
        Assert.Equal((HttpMethod.Post, $"/v1/games/{id}/end"), (f.Requests[0].Method, f.Requests[0].RequestUri!.AbsolutePath));
        Assert.Equal((HttpMethod.Delete, $"/v1/games/{id}"), (f.Requests[1].Method, f.Requests[1].RequestUri!.AbsolutePath));
    }
}
