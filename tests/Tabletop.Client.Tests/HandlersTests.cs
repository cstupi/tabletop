using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class HandlersTests
{
    [Fact]
    public void A_throwing_subscriber_skips_neither_the_others_nor_what_follows_and_its_exception_still_surfaces()
    {
        var calls = new List<string>();
        Action<int>? handlers = null;
        handlers += _ => calls.Add("a");
        handlers += _ => throw new InvalidOperationException("boom");
        handlers += n => calls.Add($"c{n}");
        var e = Assert.Throws<InvalidOperationException>(() => Handlers.Raise(handlers, h => ((Action<int>)h)(7), () => calls.Add("after")));
        Assert.Equal("boom", e.Message);
        Assert.Equal(["a", "c7", "after"], calls);
    }
}
