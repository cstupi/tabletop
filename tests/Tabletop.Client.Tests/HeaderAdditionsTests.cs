using Tabletop.Client.Core; using Tabletop.Content; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class HeaderAdditionsTests
{
    [Fact]
    public void Not_activated_counts_units_on_the_table_with_no_action_and_objectives_carry_their_controller()
    {
        var p = TableUx.After(TableUx.Deploy, new ActionRecorded(TableUx.Squad.Id, "Advance"));
        Assert.Equal((0, 1), (HeaderView.NotActivated(p, TableUx.Game)[TableUx.T1], HeaderView.NotActivated(p, TableUx.Game)[TableUx.T2]));   // Reserve is on the shelf
        Assert.Equal(1, HeaderView.NotActivated(TableUx.After(TableUx.Deploy), TableUx.Game)[TableUx.T1]);
        var objective = TableUx.Board.Table.Markers.Single(m => m.Kind == MarkerKind.Objective).Id;
        Assert.Equal(new (Guid, Guid?)[] { (objective, null) }, HeaderView.Objectives(p));
        Assert.Equal(new (Guid, Guid?)[] { (objective, TableUx.T2) }, HeaderView.Objectives(TableUx.After(new ObjectiveControlSet(objective, TableUx.T2))));
    }

    [Fact]
    public void Not_activated_counts_a_unit_inside_a_transport_on_the_table_and_not_one_inside_a_shelved_one()
    {
        var aboard = new ModelsMoved([new ModelRelocation(TableUx.ModelOf(TableUx.Reserve), new OnShelf(), new Inside(TableUx.ModelOf(TableUx.Squad)))]);
        Assert.Equal(2, HeaderView.NotActivated(TableUx.After(TableUx.Deploy, aboard), TableUx.Game)[TableUx.T1]);
        Assert.Equal(0, HeaderView.NotActivated(TableUx.After(aboard), TableUx.Game)[TableUx.T1]);   // the Squad still on the shelf
    }

    [Fact]
    public void Another_team_s_activation_is_named_on_the_action_line()
    {
        var p = TableUx.After(TableUx.Deploy, new SequenceStarted(Guid.CreateVersion7(), "charge", new SequenceContext(TableUx.Orcs.Id, [], [], "Charge")));
        var n = new Names(TableUx.Game, p);
        Assert.Equal("Team 2 activating: Orcs · Charge", HeaderView.ActivationLine(p, TableUx.Game, n, TableUx.T1));
        Assert.Equal("Orcs · Charge", HeaderView.ActivationLine(p, TableUx.Game, n, TableUx.T2));
        Assert.Equal("Orcs · Charge", HeaderView.ActivationLine(p, TableUx.Game, n, TableUx.T1, HeaderFit.Compact));   // the acting tile's ▶ says whose
        Assert.Null(HeaderView.ActivationLine(TableUx.After(TableUx.Deploy), TableUx.Game, n, TableUx.T1));
    }
}
