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
    public void The_cache_recomputes_only_when_the_unit_its_placements_or_its_threat_change()
    {
        var a = TableUx.Applier(TableUx.Deploy); var cache = new ThreatRingCache();
        var first = cache.Of(a.Projection, TableUx.Squad.Id, SquadThreat());
        Assert.Same(first, cache.Of(a.Projection, TableUx.Squad.Id, SquadThreat()));                       // an equal threat from a later ModelFacts
        var slower = cache.Of(a.Projection, TableUx.Squad.Id, SquadThreat(8, 4));
        Assert.NotSame(first, slower);
        Assert.Equal(8.0, slower[0].Inches);
        a.ApplyAll([new GameEvent(2, TableUx.P1, new ModelsMoved([new ModelRelocation(TableUx.Squad.Models[2].Id, new OnTable(new Placement(new Point3(14, 10, 0), new Yaw(0))),
            new OnTable(new Placement(new Point3(20, 10, 0), new Yaw(0))))]), null)]);
        Assert.NotSame(slower, cache.Of(a.Projection, TableUx.Squad.Id, SquadThreat(8, 4)));
    }

    [Fact]
    public void Only_a_unit_of_another_team_shows_its_threat_and_a_watcher_sees_every_units()
    {
        var p = TableUx.After(TableUx.Deploy);
        var game = TableUx.Game;
        var mine = game.Participants.Single(x => x.Id == TableUx.P1).TeamId!.Value;
        Assert.True(ThreatRings.OfAnotherTeam(game, p, [mine], TableUx.Orcs.Id));
        Assert.False(ThreatRings.OfAnotherTeam(game, p, [mine], TableUx.Squad.Id));
        Assert.True(ThreatRings.OfAnotherTeam(game, p, [], TableUx.Squad.Id));
        Assert.False(ThreatRings.OfAnotherTeam(game, p, [mine], Guid.CreateVersion7()));
    }
}
