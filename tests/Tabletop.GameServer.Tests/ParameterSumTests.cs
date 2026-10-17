using Tabletop.GameServer.Effects;

namespace Tabletop.GameServer.Tests;

public class ParameterSumTests
{
    [Fact]
    public void Parameters_sum_without_overflow_and_clamp() =>
        Assert.Equal((ParameterSum.Limit, -ParameterSum.Limit, 7), (ParameterSum.Of([int.MaxValue, int.MaxValue]), ParameterSum.Of([int.MinValue, -5]), ParameterSum.Of([3, null, 4])));
}
