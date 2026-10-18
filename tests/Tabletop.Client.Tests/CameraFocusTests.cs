using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

public class CameraFocusTests
{
    static readonly TableSize Size = new(72, 48);
    static void Near(Point3 e, Point3 a) { Assert.Equal(e.X, a.X, 9); Assert.Equal(e.Y, a.Y, 9); Assert.Equal(e.Z, a.Z, 9); }
    static void Near(Point2 e, Point2 a) => Near(new Point3(e.X, e.Y, 0), new Point3(a.X, a.Y, 0));

    /// <summary>Playing both sides: the camera goes to the acting team's units, or its deployment zone while they are all on the shelf.</summary>
    [Fact]
    public void A_teams_side_is_its_units_on_the_table_else_the_middle_of_its_zone()
    {
        var game = Tabletop.Protocol.Tests.Samples.Game; var applier = new EventApplier(Tabletop.Protocol.Tests.Samples.Board, game);
        Near(new(36, 42, 0), CameraFocus.TeamSide(applier.Projection, game, Tabletop.Protocol.Tests.Samples.Team1)!.Value);   // frontline/1: the far long edge
        Near(new(36, 6, 0), CameraFocus.TeamSide(applier.Projection, game, Tabletop.Protocol.Tests.Samples.Team2)!.Value);
        applier.Apply(new GameEvent(1, null, new ModelsMoved([Tabletop.Protocol.Tests.Samples.Deploy]), null));
        Near(new(12, 12, 0), CameraFocus.TeamSide(applier.Projection, game, Tabletop.Protocol.Tests.Samples.Team1)!.Value);
    }

    [Fact]
    public void Pan_moves_along_and_across_the_heading_at_a_speed_set_by_distance_and_stops_12_inches_past_the_table()
    {
        var north = new Point2(0, 1);
        Near(new(36, 32), CameraFocus.Pan(new(36, 24), north, 1, 0, 10, 1, Size));        // 10" away: 8"/s
        Near(new(44, 24), CameraFocus.Pan(new(36, 24), north, 0, 1, 10, 1, Size));        // D: across, to the right
        Near(new(84, 60), CameraFocus.Pan(new(80, 58), north, 1, 1, 100, 1, Size));
    }

    /// <summary>Review Focus 2.</summary>
    [Fact]
    public void Steer_is_silent_while_typing_and_q_e_turn_the_held_unit()
    {
        var held = new HashSet<Intent> { Intent.CameraForward, Intent.CameraLeft, Intent.TurnLeft };
        Assert.Equal((1, -1, 1), CameraFocus.Steer(held, holding: false, typing: false));
        Assert.Equal((1, -1, 0), CameraFocus.Steer(held, holding: true, typing: false));
        Assert.Equal((0, 0, 0), CameraFocus.Steer(held, holding: false, typing: true));
    }

    /// <summary>Review Focus 1.</summary>
    [Fact]
    public void Follow_waits_for_a_quiet_camera_and_an_empty_hand()
    {
        Assert.True(CameraFocus.Follows(true, 2.0, false)); Assert.False(CameraFocus.Follows(true, 1.9, false));
        Assert.False(CameraFocus.Follows(true, 10, true)); Assert.False(CameraFocus.Follows(false, 10, false));
    }

    [Fact]
    public void A_glide_eases_over_three_tenths_of_a_second_to_the_actor_of_the_last_action_frame()
    {
        Point3 a = new(0, 0, 0), b = new(10, 20, 2);
        Near(new(5, 10, 1), CameraFocus.Glide(a, b, 0.15)); Near(b, CameraFocus.Glide(a, b, 0.3)); Near(b, CameraFocus.Glide(a, b, 5));
        var p = TableUx.After(TableUx.Deploy);
        SequenceStarted Start(Guid unit, string? action) => new(Guid.CreateVersion7(), "s", new SequenceContext(unit, [], [], action));
        Near(new(12, 10, 0), CameraFocus.FollowTarget(TableUx.Events(Start(TableUx.Squad.Id, "Advance"), Start(TableUx.Orcs.Id, null)), p)!.Value);
        Assert.Null(CameraFocus.FollowTarget(TableUx.Events(Start(TableUx.Reserve.Id, "Advance")), p));   // nothing on the table
    }
}
