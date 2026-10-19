using System.Net;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class GameRowActionsTests
{
    static readonly Guid Me = Guid.CreateVersion7();
    static GameSummary Row(Guid by, GameStatus status, bool joined = false) => new(Guid.CreateVersion7(), "g", status, by, DateTimeOffset.UnixEpoch, 2, joined);

    [Fact]
    public void The_creator_ends_a_game_not_yet_ended_and_deletes_any_of_theirs()
    {
        Assert.Equal((true, true), GameRowActions.Of(Row(Me, GameStatus.Playing), Me) is var a ? (a.CanEnd, a.CanDelete) : default);
        Assert.Equal((false, true), GameRowActions.Of(Row(Me, GameStatus.Ended), Me) is var b ? (b.CanEnd, b.CanDelete) : default);
        Assert.Equal((false, false), GameRowActions.Of(Row(Guid.CreateVersion7(), GameStatus.Lobby), Me) is var c ? (c.CanEnd, c.CanDelete) : default);
        Assert.Equal((false, false), GameRowActions.Of(null, Me) is var d ? (d.CanEnd, d.CanDelete) : default);
    }

    [Fact]
    public void Replay_is_offered_on_a_game_this_account_is_in_once_it_has_a_log()
    {
        Assert.True(GameRowActions.Of(Row(Guid.CreateVersion7(), GameStatus.Ended, joined: true), Me).CanReplay);
        Assert.True(GameRowActions.Of(Row(Me, GameStatus.Playing, joined: true), Me).CanReplay);
        Assert.False(GameRowActions.Of(Row(Me, GameStatus.Setup, joined: true), Me).CanReplay);
        Assert.False(GameRowActions.Of(Row(Guid.CreateVersion7(), GameStatus.Ended), Me).CanReplay);
        Assert.False(GameRowActions.Of(null, Me).CanReplay);
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
