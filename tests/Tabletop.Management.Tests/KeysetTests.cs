using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public class KeysetTests
{
    [Fact] public void Defaults_to_fifty() => Assert.Equal(new Keyset(50, null), Keyset.Parse(null, null));
    [Fact] public void Caps_at_two_hundred() => Assert.Equal(200, Keyset.Parse(999, null).Limit);
    [Fact] public void Rejects_non_positive_limits() => Assert.Throws<ArgumentOutOfRangeException>(() => Keyset.Parse(0, null));

    [Fact]
    public void Next_is_the_last_id_only_when_the_page_is_full()
    {
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToList();
        Assert.Equal(ids[2], Page<Guid>.Of(ids, 3, x => x).Next);
        Assert.Null(Page<Guid>.Of(ids, 4, x => x).Next);
    }
}
