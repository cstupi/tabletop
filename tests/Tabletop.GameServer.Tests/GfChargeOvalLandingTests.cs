using Tabletop.Content;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// p.7 "physically impossible locations" through a charge's end (GfMoves.ChargeLanding): five riders on 60 × 35 mm ovals abreast 1.58" apart (a
/// dev game's Flayer Cavalry, which the browser draws as 60 mm circles that look overlapped), charging a squad and dropped within the grace, short
/// of it and in contact, end with no base on another's: the riders' own landings, the targets' and every other model's (Overlap.Bases, the
/// server's move_overlap test), in both editions.
/// </summary>
public class GfChargeOvalLandingTests
{
    static readonly Base Oval = Base.Oval(2.362204724409449, 1.3779527559055118);
    const double Abreast = 1.578;

    static Sim Charging(string rules, out Unit riders, out Unit squad)
    {
        var cavalry = Unit("Flayer Cavalry", "3+", "4+", [], 5, [], Melee("Stalker Axe", 1));
        riders = cavalry with { Models = cavalry.Models.Select(m => m with { Base = Oval, Height = 4.43 }).ToList() };
        squad = Unit("Orc Warriors", "4+", "5+", [], 10, [], Melee("Choppa", 1));
        var sim = Sim.Gf(rules, State([riders], [squad], enforcement: Enforcement.Strict), 1);
        for (var i = 0; i < 5; i++) Place(sim, riders.Models[i].Id, 30 + i * Abreast, 20);
        for (var i = 0; i < 10; i++) Place(sim, squad.Models[i].Id, 29 + (i % 5) * 1.6, 30 + (i / 5) * 1.6);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(riders.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([squad.Id]));
        return sim;
    }

    static void Place(Sim sim, Guid model, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);

    static ModelRelocation At(Sim sim, Guid model, double x, double y) =>
        new(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));

    static List<(Guid Id, PlacedModel At)> OnTable(Sim sim) =>
        sim.State.Projection.Models.Values.Where(m => m.Location is OnTable).Select(m => (m.Id, Battlefield.Placed(sim.State, m.Id)!.Value)).ToList();

    public static TheoryData<string, double> Drops()
    {
        var data = new TheoryData<string, double>();
        foreach (var rules in new[] { RuleSets.Gf351, RuleSets.Gf360 })
            foreach (var shortBy in new[] { 0.0, 0.2, 0.45, 1.5 })
                data.Add(rules, shortBy);
        return data;
    }

    [Theory]
    [MemberData(nameof(Drops))]
    public void A_charge_of_ovals_abreast_ends_with_no_base_on_another(string rules, double shortBy)
    {
        var sim = Charging(rules, out var riders, out var squad);
        // Straight ahead: the front of each oval (half its 2.36" length past the centre) shortBy from the squad's front rank (circles of 1.26" at y 30).
        var y = 30 - 0.63 - 1.181 - shortBy;
        var answer = new Destinations(riders.Models.Select((m, i) => At(sim, m.Id, 30 + i * Abreast, y)).ToList());
        sim.Answer(Samples.Team1, answer);
        var placed = OnTable(sim);
        foreach (var (a, i) in placed.Select((p, i) => (p, i)))
            foreach (var b in placed.Skip(i + 1))
                Assert.False(Overlap.Bases(a.At, b.At), $"{sim.Model(a.Id).Slot.Name} overlaps {sim.Model(b.Id).Slot.Name}");
        // The bases the browser draws as circles of their longer side: neighbours abreast are 1.58" apart, under the 2.36" those circles need.
        var front = riders.Models.Select(m => Battlefield.Placed(sim.State, m.Id)!.Value).ToList();
        Assert.Contains(front.Zip(front.Skip(1)), p => p.First.Placement.Position.Horizontal.DistanceTo(p.Second.Placement.Position.Horizontal) < Oval.A);
        Assert.Contains(riders.Models, m => squad.Models.Any(t => Distance.EdgeToEdge(Battlefield.Placed(sim.State, m.Id)!.Value, Battlefield.Placed(sim.State, t.Id)!.Value) <= 1e-6));
    }
}
