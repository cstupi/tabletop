using Tabletop.Protocol;
using Tabletop.Sim.Lifecycle;

namespace Tabletop.Sim.Tests.Lifecycle;

public class LifecycleRunnerTests
{
    [Fact]
    public void A_v7_id_reads_back_its_time_and_is_stale_only_past_the_sweep_age()
    {
        var now = DateTimeOffset.UtcNow;
        var hourAgo = Guid.CreateVersion7(now - LifecycleRunner.SweepAge - TimeSpan.FromMinutes(1));
        var fresh = Guid.CreateVersion7(now - TimeSpan.FromMinutes(5));
        Assert.Equal((now - LifecycleRunner.SweepAge - TimeSpan.FromMinutes(1)).ToUnixTimeMilliseconds(), LifecycleRunner.CreatedAt(hourAgo)!.Value.ToUnixTimeMilliseconds());
        Assert.True(LifecycleRunner.Stale(hourAgo, now));
        Assert.False(LifecycleRunner.Stale(fresh, now));
        Assert.False(LifecycleRunner.Stale(Guid.NewGuid(), now));       // a v4 id carries no time: never swept
    }

    [Fact]
    public void A_game_name_holds_the_run_and_shape_within_managements_64_characters()
    {
        var run = Guid.CreateVersion7();
        Assert.All(LifecycleShapes.All, s =>
        {
            var name = LifecycleRunner.GameName(run, s.Name);
            Assert.StartsWith(LifecycleRunner.GamePrefix, name);
            Assert.InRange(name.Length, 1, 64);
        });
    }

    [Fact]
    public void Shapes_select_by_name_and_an_unknown_one_is_a_usage_error()
    {
        Assert.Equal(LifecycleShapes.All, LifecycleShapes.Select([]));
        Assert.Equal(["self-play", "late-joiner"], LifecycleShapes.Select(["late-joiner", "self-play"]).Select(s => s.Name));
        Assert.Throws<UsageException>(() => LifecycleShapes.Select(["nope"]));
    }

    [Fact]
    public void A_shape_counts_its_teams_and_accounts_with_the_late_seat()
    {
        var late = LifecycleShapes.Select(["late-joiner"])[0];
        Assert.Equal(2, late.Teams);
        Assert.Equal([0, 1], late.Accounts);
        var team = LifecycleShapes.Select(["team-game"])[0];
        Assert.Equal(2, team.Teams);
        Assert.Equal([0, 1, 2, 3], team.Accounts);
    }

    [Fact]
    public void Four_teams_seats_eight_accounts_two_a_team_with_armies_from_four_books()
    {
        var shape = LifecycleShapes.Select(["four-teams"])[0];
        Assert.Equal(4, shape.Teams);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], shape.Accounts);
        Assert.Equal(Enforcement.Strict, shape.Enforcement);
        Assert.All(Enumerable.Range(1, 4), team => Assert.Equal(2, shape.Seats.Count(s => s.Team == team)));
        Assert.All(Enumerable.Range(0, 4), book => Assert.Equal(2, shape.Seats.Count(s => s.Book == book)));
        // The first seat of each team is its account's own pick; the second is the creator's placing, after every team is held.
        Assert.Equal([1, 2, 3, 4], shape.Seats.Take(4).Select(s => s.Team));
        Assert.True(LifecycleRunner.Players.Count >= shape.Accounts.Max() + 1);
    }

    [Fact]
    public void The_verb_parses_only_and_list()
    {
        Assert.Equal(new[] { "a", "b", "c" }, LifecycleArguments.Parse(["--only", "a,b", "--only", "c"]).Only);
        Assert.True(LifecycleArguments.Parse(["--list"]).List);
        Assert.Throws<UsageException>(() => LifecycleArguments.Parse(["--bogus"]));
        Assert.Throws<UsageException>(() => LifecycleArguments.Parse(["--only"]));
    }

    [Fact]
    public void The_watch_uri_is_the_game_socket_with_watch_set()
    {
        var endpoints = new LifecycleEndpoints(new Uri("http://127.0.0.1:5100"), new Uri("ws://127.0.0.1:5200/ws"));
        Assert.Equal("ws://127.0.0.1:5200/ws?watch=1", endpoints.WatchUri.ToString());
    }
}
