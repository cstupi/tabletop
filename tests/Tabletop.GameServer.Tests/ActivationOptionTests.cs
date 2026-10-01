using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class ActivationOptionTests
{
    [Fact]
    public void Id_round_trips_unit_and_action()
    {
        var unit = Guid.CreateVersion7();
        var id = ActivationOption.Id(unit, "Advance");
        Assert.Equal(unit.ToString("N") + "/Advance", id);
        Assert.True(ActivationOption.TryParse(id, out var u, out var a));
        Assert.Equal((unit, "Advance"), (u, a));
    }

    [Theory] [InlineData("")] [InlineData("nope")] [InlineData("zzz/Advance")] [InlineData("0193b0f0d1a27c6e9f0e1234567890ab/")]
    public void Malformed_ids_do_not_parse(string id) => Assert.False(ActivationOption.TryParse(id, out _, out _));

    [Fact]
    public void Option_highlights_the_unit_and_labels_with_its_name()
    {
        var p = Projection.Initial(Samples.Board, Samples.Game);
        var unit = p.Units[Samples.Unit];
        var option = ActivationOption.For(unit, "Rush");
        Assert.Equal([Samples.Unit], option.Pieces);
        Assert.Equal($"{unit.Definition.Name}: Rush", option.Label);
        Assert.Equal(ActivationOption.Id(Samples.Unit, "Rush"), option.Id);
    }
}
