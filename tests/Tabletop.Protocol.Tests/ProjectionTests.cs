namespace Tabletop.Protocol.Tests;

public class ProjectionTests
{
    [Fact]
    public void Initial_projection_shelves_every_model_and_zeroes_scores()
    {
        var p = Projection.Initial(Samples.Board, Samples.Game);
        Assert.Single(p.Units);
        Assert.Equal(Samples.P1, p.Units[Samples.Unit].ParticipantId);
        Assert.All(p.Models.Values, m => Assert.IsType<OnShelf>(m.Location));
        Assert.Equal(Samples.Unit, p.Models[Samples.Model].UnitId);
        Assert.Equal(0, p.Scores[Samples.Team1]);
        Assert.Equal(0, p.Scheduler.Round);
        Assert.Empty(p.Stack);
        Assert.Empty(p.OpenAsks);
        Assert.Equal(0, p.LastSeq);
    }
}
