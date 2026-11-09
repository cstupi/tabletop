using Tabletop.Client.Core; using Tabletop.Geometry; using Tabletop.Protocol;
namespace Tabletop.Client.Tests;

/// <summary>Threat range plan: the hovered enemy unit's reach, from the server's UnitThreat, over RangeOutline.</summary>
public class ThreatRingsTests
{
    static UnitThreat SquadThreat(double? charge = 12, double? move = 6) => new(charge, move, new Dictionary<Guid, double>
        { [TableUx.Squad.Models[0].Id] = 24, [TableUx.Squad.Models[1].Id] = 24, [TableUx.Squad.Models[2].Id] = 12 });

    [Fact]
    public void A_charge_ring_round_every_model_and_a_shoot_ring_per_reach_round_its_models()
    {
        var p = TableUx.After(TableUx.Deploy);
        var rings = ThreatRings.Of(p, TableUx.Squad.Id, SquadThreat());
        Assert.Equal(new[] { (12.0, "Charge 12\""), (18.0, "Shoot 18\""), (30.0, "Shoot 30\"") }, rings.Select(r => (r.Inches, r.Label)));
        Point2[] all = [new(10, 10), new(12, 10), new(14, 10)], rifles = [new(10, 10), new(12, 10)];
        Assert.All(rings[0].Segments.SelectMany(s => new[] { s.A, s.B }), q => Assert.Equal(12.5, all.Min(c => c.DistanceTo(q)), 3));        // from the base edge
        Assert.All(rings[1].Segments, s => Assert.Equal(18.5, s.A.DistanceTo(new Point2(14, 10)), 3));                                        // the pistol: 6 + 12
        Assert.All(rings[2].Segments.SelectMany(s => new[] { s.A, s.B }), q => Assert.Equal(30.5, rifles.Min(c => c.DistanceTo(q)), 3));     // the rifles: 6 + 24
    }

    [Fact]
    public void No_charge_and_no_shot_draw_nothing_and_a_unit_without_ranges_draws_only_its_charge()
    {
        var p = TableUx.After(TableUx.Deploy);
        Assert.Empty(ThreatRings.Of(p, TableUx.Squad.Id, SquadThreat(null, null)));
        Assert.Equal(["Shoot 24\"", "Shoot 12\""], ThreatRings.Of(p, TableUx.Squad.Id, SquadThreat(null, 0)).Select(r => r.Label).Reverse());   // standing: the ranges alone
        var orcs = ThreatRings.Of(p, TableUx.Orcs.Id, new UnitThreat(12, 6, new Dictionary<Guid, double>()));
        Assert.Equal(["Charge 12\""], orcs.Select(r => r.Label));
        Assert.Equal(RangeRings.Points, orcs[0].Segments.Count);
    }

    [Fact]
    public void A_builder_reads_the_projection_when_made_and_builds_the_same_rings_later()
    {
        var a = TableUx.Applier(TableUx.Deploy); var game = TableUx.Game;
        var unit = ThreatRings.Builder(a.Projection, TableUx.Squad.Id, SquadThreat());
        var all = ThreatRings.AllBuilder(game, a.Projection, TableUx.T2, Both);
        var range = RangeRings.Builder(a.Projection, TableUx.Squad.Id);
        var (unitNow, allNow, rangeNow) = (ThreatRings.Of(a.Projection, TableUx.Squad.Id, SquadThreat()), ThreatRings.All(game, a.Projection, TableUx.T2, Both), RangeRings.Of(a.Projection, TableUx.Squad.Id));
        a.ApplyAll([new GameEvent(2, TableUx.P1, new ModelsMoved([new ModelRelocation(TableUx.Squad.Models[2].Id, new OnTable(new Placement(new Point3(14, 10, 0), new Yaw(0))),
            new OnTable(new Placement(new Point3(20, 10, 0), new Yaw(0))))]), null)]);
        Assert.Equal(unitNow.SelectMany(r => r.Segments), unit().SelectMany(r => r.Segments));
        Assert.Equal(allNow.SelectMany(t => t.Rings).SelectMany(r => r.Segments), all().SelectMany(t => t.Rings).SelectMany(r => r.Segments));
        Assert.Equal(rangeNow.SelectMany(r => r.Segments), range().SelectMany(r => r.Segments));
    }

    static readonly UnitThreat OrcThreat = new(16, 6, new Dictionary<Guid, double>());
    static IReadOnlyDictionary<Guid, UnitThreat> Both => new Dictionary<Guid, UnitThreat> { [TableUx.Squad.Id] = SquadThreat(), [TableUx.Orcs.Id] = OrcThreat };

    [Fact]
    public void Another_team_is_one_other_than_the_team_acted_for_and_a_watcher_sees_every_units()
    {
        var p = TableUx.After(TableUx.Deploy);
        var game = TableUx.Game;
        Assert.True(ThreatRings.OfAnotherTeam(game, p, TableUx.T1, TableUx.Orcs.Id));
        Assert.False(ThreatRings.OfAnotherTeam(game, p, TableUx.T1, TableUx.Squad.Id));
        Assert.True(ThreatRings.OfAnotherTeam(game, p, TableUx.T2, TableUx.Squad.Id));     // solo: acting for team 2, team 1 is the enemy
        Assert.False(ThreatRings.OfAnotherTeam(game, p, TableUx.T2, TableUx.Orcs.Id));
        Assert.True(ThreatRings.OfAnotherTeam(game, p, null, TableUx.Squad.Id));
        Assert.False(ThreatRings.OfAnotherTeam(game, p, TableUx.T1, Guid.CreateVersion7()));
    }

    [Fact]
    public void One_account_on_both_teams_sees_the_threats_of_the_team_it_does_not_act_for()
    {
        var p = TableUx.After(TableUx.Deploy);
        var account = Guid.CreateVersion7();
        var solo = TableUx.Game with { Participants = [.. TableUx.Game.Participants.Select(x => x with { AccountId = account })] };
        var mine = Seats.Teams(solo, TableUx.P1);
        Assert.Equal([TableUx.T1, TableUx.T2], mine);
        var acting = Seats.Acting(p, mine);
        Assert.Equal([TableUx.Orcs.Id], ThreatRings.Shown(solo, p, acting, TableUx.Orcs.Id, TableUx.Squad.Id, Both));
    }

    [Fact]
    public void The_selected_unit_and_the_hovered_one_both_show_each_once_and_only_of_another_team_with_a_threat()
    {
        var p = TableUx.After(TableUx.Deploy);
        var game = TableUx.Game;
        Assert.Equal([TableUx.Orcs.Id], ThreatRings.Shown(game, p, TableUx.T1, TableUx.Orcs.Id, null, Both));                   // selected stays
        Assert.Equal([TableUx.Orcs.Id], ThreatRings.Shown(game, p, TableUx.T1, TableUx.Orcs.Id, TableUx.Orcs.Id, Both));        // once
        Assert.Empty(ThreatRings.Shown(game, p, TableUx.T1, TableUx.Squad.Id, null, Both));                                     // mine
        Assert.Empty(ThreatRings.Shown(game, p, TableUx.T1, TableUx.Orcs.Id, null, new Dictionary<Guid, UnitThreat>()));        // no threat
        Assert.Equal([TableUx.Squad.Id, TableUx.Orcs.Id], ThreatRings.Shown(game, p, null, TableUx.Squad.Id, TableUx.Orcs.Id, Both));   // a watcher: selected, then hovered
        Assert.Empty(ThreatRings.Shown(game, p, TableUx.T1, Guid.CreateVersion7(), null, Both));                                // a terrain piece selected
    }

    [Fact]
    public void All_is_per_enemy_team_one_charge_area_and_one_shoot_area_each_the_union_of_its_units()
    {
        var p = TableUx.After(TableUx.Deploy);
        var game = TableUx.Game;
        var mineSeen = ThreatRings.All(game, p, TableUx.T2, Both);
        var team = Assert.Single(mineSeen);
        Assert.Equal((TableUx.T1, 1), (team.TeamId, team.Ordinal));
        Assert.Equal([(RingKind.Charge, "Charge 12\""), (RingKind.Shoot, "Shoot 18–30\"")], team.Rings.Select(r => (r.Kind, r.Label)));
        Point2[] all = [new(10, 10), new(12, 10), new(14, 10)];
        Assert.All(team.Rings[1].Segments.SelectMany(s => new[] { s.A, s.B }), q => Assert.True(all.Min(c => c.DistanceTo(q)) >= 18.5 - 1e-6));   // the pistol's 18 inside the rifles' 30
        Assert.Equal(["Charge 16\""], Assert.Single(ThreatRings.All(game, p, TableUx.T1, Both)).Rings.Select(r => r.Label));
        Assert.Equal(2, ThreatRings.All(game, p, null, Both).Count);                                                                 // a watcher: every team
        Assert.Empty(ThreatRings.All(game, p, TableUx.T1, new Dictionary<Guid, UnitThreat> { [TableUx.Orcs.Id] = new(null, null, new Dictionary<Guid, double>()) }));
    }

    [Fact]
    public void The_selected_enemy_stays_pinned_while_a_unit_of_mine_or_terrain_is_selected_and_an_empty_selection_clears_it()
    {
        var p = TableUx.After(TableUx.Deploy);
        var game = TableUx.Game;
        var pinned = ThreatRings.Pinned(game, p, TableUx.T1, TableUx.Orcs.Id, null);
        Assert.Equal(TableUx.Orcs.Id, pinned);
        Assert.Equal(TableUx.Orcs.Id, ThreatRings.Pinned(game, p, TableUx.T1, TableUx.Reserve.Id, pinned));         // picked to deploy: on the shelf
        Assert.Equal(TableUx.Orcs.Id, ThreatRings.Pinned(game, p, TableUx.T1, TableUx.Squad.Id, pinned));           // a unit of mine on the table
        Assert.Equal(TableUx.Orcs.Id, ThreatRings.Pinned(game, p, TableUx.T1, Guid.CreateVersion7(), pinned));      // a terrain piece
        Assert.Null(ThreatRings.Pinned(game, p, TableUx.T1, null, pinned));
        Assert.Null(ThreatRings.Pinned(game, p, TableUx.T1, TableUx.Squad.Id, null));
    }

    [Fact]
    public void While_one_account_places_a_unit_in_a_deploy_ask_the_other_teams_threat_draws()
    {
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.Deploy, "Deploy one unit", [TableUx.T1], new PieceSelection([TableUx.Reserve.Id], 1, 1), false, false, null, null);
        var p = TableUx.After(TableUx.Deploy, new AskOpened(ask));
        Assert.Equal(0, p.Scheduler.Round);
        var account = Guid.CreateVersion7();
        var solo = TableUx.Game with { Participants = [.. TableUx.Game.Participants.Select(x => x with { AccountId = account })] };
        var acting = Seats.Acting(p, Seats.Teams(solo, TableUx.P1));
        Assert.Equal(TableUx.T1, acting);                                                                             // the deploy Ask's team
        var pinned = ThreatRings.Pinned(solo, p, acting, TableUx.Orcs.Id, null);
        pinned = ThreatRings.Pinned(solo, p, acting, TableUx.Reserve.Id, pinned);                                    // the unit being placed
        Assert.Equal([TableUx.Orcs.Id], ThreatRings.Shown(solo, p, acting, pinned, null, Both));
        Assert.Equal(["Charge 16\""], ThreatRings.Of(p, TableUx.Orcs.Id, OrcThreat).Select(r => r.Label));
        Assert.Equal(TableUx.T2, Assert.Single(ThreatRings.All(solo, p, acting, Both)).TeamId);                        // T: the other team's areas
    }
}
