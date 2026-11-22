using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>Setup's loading state over the table: a Random table or a loaded layout from the request until every new piece is drawn, and
/// any wave of new meshes that outlasts the delay.</summary>
public class TableLoadTests
{
    static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(150);
    static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);
    readonly object _old = new(), _new = new();

    [Fact]
    public void A_request_shows_after_the_delay_through_the_round_trip_and_the_meshes_then_clears_after_its_quiet_frames()
    {
        var load = new TableLoad(Delay, quietFrames: 2);
        load.Begin(_old, Ms(0));
        Assert.Null(load.Update(_old, 0, 0, Ms(100)));                                           // within the delay
        Assert.Equal(new LoadingLine(TableLoad.Text), load.Update(_old, 0, 0, Ms(200)));          // the server has not answered
        Assert.Equal(new LoadingLine($"{TableLoad.Text} 7 of 23", 7, 23), load.Update(_new, 7, 23, Ms(300)));
        Assert.NotNull(load.Update(_new, 0, 0, Ms(400)));                                       // every file in: the first draws
        Assert.NotNull(load.Update(_new, 0, 0, Ms(420)));
        Assert.Null(load.Update(_new, 0, 0, Ms(440)));
        Assert.Null(load.Update(_new, 0, 0, Ms(900)));
    }

    [Fact]
    public void A_quick_request_never_shows()
    {
        var load = new TableLoad(Delay, quietFrames: 2);
        load.Begin(_old, Ms(0));
        Assert.Null(load.Update(_old, 0, 0, Ms(30)));
        Assert.Null(load.Update(_new, 0, 1, Ms(60)));
        Assert.Null(load.Update(_new, 0, 0, Ms(90)));
        Assert.Null(load.Update(_new, 0, 0, Ms(110)));
        Assert.Null(load.Update(_new, 0, 0, Ms(130)));
        Assert.Null(load.Update(_new, 0, 0, Ms(400)));
    }

    [Fact]
    public void A_wave_of_new_meshes_without_a_request_shows_once_it_outlasts_the_delay()
    {
        var load = new TableLoad(Delay, quietFrames: 0);
        Assert.Null(load.Update(_old, 0, 0, Ms(0)));
        Assert.Null(load.Update(_old, 0, 2, Ms(10)));
        Assert.Null(load.Update(_old, 1, 2, Ms(100)));
        Assert.Equal(new LoadingLine($"{TableLoad.Text} 1 of 2", 1, 2), load.Update(_old, 1, 2, Ms(170)));
        Assert.Null(load.Update(_old, 0, 0, Ms(200)));
    }

    [Fact]
    public void A_failed_request_clears()
    {
        var load = new TableLoad(Delay, quietFrames: 0);
        load.Begin(_old, Ms(0));
        Assert.NotNull(load.Update(_old, 0, 0, Ms(200)));
        load.Cancel();
        Assert.Null(load.Update(_old, 0, 0, Ms(220)));
    }

    [Fact]
    public void A_wait_past_the_patience_gives_up()
    {
        var load = new TableLoad(Delay, quietFrames: 0, patience: TimeSpan.FromSeconds(30));
        load.Begin(_old, Ms(0));
        Assert.NotNull(load.Update(_old, 0, 0, TimeSpan.FromSeconds(29)));
        Assert.Null(load.Update(_old, 0, 0, TimeSpan.FromSeconds(30)));
        Assert.Null(load.Update(_old, 0, 0, TimeSpan.FromSeconds(31)));
    }
}
