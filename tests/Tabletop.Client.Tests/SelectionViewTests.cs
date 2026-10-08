using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class SelectionViewTests
{
    static EventApplier Applier(params EventPayload[] history) { var a = new EventApplier(Samples.Board, Samples.Game); a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null))); return a; }
    static readonly ModelRelocation Placed = new(Samples.Model, new OnShelf(), new OnTable(new Placement(new Point3(10, 10, 0), new Yaw(0))));

    [Fact]
    public void A_selected_unit_names_itself_and_counts_its_models_on_the_table()
    {
        var shelved = Applier().Projection; var onTable = Applier(new ModelsMoved([Placed])).Projection;
        var sel = new Selection(Samples.Unit, []);
        Assert.Equal("Battle Brothers: 0 of 1 models on the table", SelectionView.Describe(new Names(Samples.Game, shelved), shelved, sel));
        Assert.Equal("Battle Brothers: 1 of 1 models on the table", SelectionView.Describe(new Names(Samples.Game, onTable), onTable, sel));
    }

    [Fact]
    public void A_shift_selected_model_names_the_model_and_nothing_selected_says_nothing()
    {
        var p = Applier(new ModelsMoved([Placed])).Projection; var names = new Names(Samples.Game, p);
        Assert.Equal("Battle Brothers (Battle Brother)", SelectionView.Describe(names, p, new Selection(Samples.Unit, [Samples.Model])));
        Assert.Null(SelectionView.Describe(names, p, null));
    }

    [Fact]
    public void A_selection_lights_the_listed_models_or_else_every_model_of_the_unit()
    {
        var p = Applier().Projection;
        Assert.Equal([Samples.Model], SelectionView.Models(p, new Selection(Samples.Unit, [])));
        Assert.Equal([Samples.Model], SelectionView.Models(p, new Selection(Samples.Unit, [Samples.Model])));
        Assert.Empty(SelectionView.Models(p, new Selection(Guid.CreateVersion7(), [])));      // terrain or a marker: no models
        Assert.Empty(SelectionView.Models(p, null));
    }
}
