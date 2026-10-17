using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.GameServer.Session;

namespace Tabletop.GameServer.Tests;

public class TokenBucketTests
{
    [Fact]
    public void A_full_bucket_gives_its_burst_then_refills_at_the_rate()
    {
        var time = new ManualTime();
        var bucket = new TokenBucket(new FrameRate(20, 40), time);
        Assert.Equal(40, Enumerable.Range(0, 100).Count(_ => bucket.TryTake()));
        Assert.False(bucket.TryTake());
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(2, Enumerable.Range(0, 10).Count(_ => bucket.TryTake()));
        time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(40, Enumerable.Range(0, 100).Count(_ => bucket.TryTake()));
    }

    [Fact]
    public void An_empty_bucket_names_the_wait()
    {
        var time = new ManualTime();
        var bucket = new TokenBucket(new FrameRate(10, 1), time);
        Assert.True(bucket.TryTake());
        Assert.Equal(TimeSpan.FromMilliseconds(100), bucket.Wait());
    }
}
