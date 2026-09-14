namespace Tabletop.Geometry.Tests;

public class SmokeTests
{
    [Fact]
    public void Assembly_loads() => Assert.Equal("Tabletop.Geometry", typeof(Point3).Assembly.GetName().Name);
}
