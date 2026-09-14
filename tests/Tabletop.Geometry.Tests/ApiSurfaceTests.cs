namespace Tabletop.Geometry.Tests;

public class ApiSurfaceTests
{
    static readonly PlacedModel A = new(new ModelBody(Base.Circle(1.26), 1.5), new Placement(new Point3(0, 0, 0), new Yaw(0)));
    static readonly PlacedModel B = new(new ModelBody(Base.Circle(1.26), 1.5), new Placement(new Point3(6, 0, 0), new Yaw(0)));
    static readonly WorldVolume[] None = [];

    [Fact] public void Distance_is_declared() => Assert.Throws<NotImplementedException>(() => Distance.EdgeToEdge(A, B));
    [Fact] public void LineOfSight_is_declared() => Assert.Throws<NotImplementedException>(() => LineOfSight.Check(A, B, None));
    [Fact] public void Settling_is_declared() => Assert.Throws<NotImplementedException>(() => Settling.SurfaceHeightAt(new Point2(0, 0), None));
    [Fact] public void Containment_is_declared() => Assert.Throws<NotImplementedException>(() => Containment.VolumesContaining(A, None));
    [Fact] public void Overlap_is_declared() => Assert.Throws<NotImplementedException>(() => Overlap.Bases(A, B));
}
