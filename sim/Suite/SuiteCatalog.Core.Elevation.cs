using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: a model crossing above a model on a lower level (p.7, owner ruling: movement takes elevation into account).</summary>
internal static partial class CoreGames
{
    // 48" by 36": a 3" deck across the middle (y 14–22), a 1.4" square pit in it at (24, 18). Red unit 2 drops into the pit; Red unit 1
    // climbs onto the deck at DeckFrom and Rushes along it to DeckTo, its base over the pit's edge and so over Red unit 2's base in plan.
    static readonly Point2 Pit = new(24, 18), DeckFrom = new(18, 18.6), DeckTo = new(29.5, 18.6);
    const double DeckTop = 3, PitHalf = 0.7;

    /// <summary>
    /// p.7 "may never move through other models or units", with the owner's ruling that movement takes elevation into account: a body
    /// whose path is above another model's does not move through it, and p.7's 1" is measured between the bodies. Under strict, so the
    /// crossing is applied only when the server finds no breach.
    /// </summary>
    public static TestGame OverALowerModel()
    {
        const string name = "core-over-a-lower-model";
        static Guid? ModelOf(Projection p, string unit) => p.Units.Values.FirstOrDefault(u => u.Definition.Name == unit)?.Definition.Models[0].Id;
        static Point2? At(Projection p, Guid? model) => model is { } id && p.Models[id].Location is OnTable t ? t.Placement.Position.Horizontal : null;
        static bool There(Point2? at, Point2 target) => at is { } a && a.DistanceTo(target) < 1e-3;
        static AskAnswer? Hold(Ask ask) =>
            AskPicks.Activate(ask, "Red unit 1", "Hold") ?? AskPicks.Activate(ask, "Red unit 2", "Hold") ?? AskPicks.Activate(ask, "Blue unit 1", "Hold");

        var script = AnswerScript.Build();
        // Red unit 2 Rushes to the pit and Red unit 1 to the deck; once Red unit 2 is in the pit, Red unit 1 Rushes across. Everything else holds.
        script.Match("activate", (ask, p) =>
        {
            var (low, high) = (At(p, ModelOf(p, "Red unit 2")), At(p, ModelOf(p, "Red unit 1")));
            var lowRush = There(low, Pit) ? null : AskPicks.Activate(ask, "Red unit 2", "Rush");
            var highRush = There(high, DeckTo) || (There(high, DeckFrom) && !There(low, Pit)) ? null : AskPicks.Activate(ask, "Red unit 1", "Rush");
            return lowRush ?? highRush ?? Hold(ask);
        });
        script.Match("move", (ask, p) =>
        {
            if (ask.Legal is not MoveRegion { Containment: null } region || region.ModelIds.Count != 1) return null;
            var id = region.ModelIds[0];
            if (At(p, id) is not { } here) return null;
            Point2? target = AskPicks.UnitNameOf(p, id) switch
            {
                "Red unit 2" => Pit,
                "Red unit 1" => There(here, DeckFrom) ? DeckTo : DeckFrom,
                _ => null,
            };
            if (target is not { } t) return null;
            var d = t - here;
            var end = d.Length < 1e-9 ? here : here + d * (Math.Min(d.Length, region.Allowance - 0.01) / d.Length);
            var yaw = ((OnTable)p.Models[id].Location).Placement.Yaw;
            return new Destinations([new ModelRelocation(id, p.Models[id].Location, new OnTable(new Placement(new Point3(end.X, end.Y, 0), yaw)))]);
        });

        Volume Block(double x0, double x1, double y0, double y1) => new(VolumeKind.Solid, Footprint.Rect(x1 - x0, y1 - y0),
            new Point2((x0 + x1) / 2 - 24, (y0 + y1) / 2 - 18), new Yaw(0), 0, DeckTop, TerrainDeclarations.None, null);
        var (px0, px1, py0, py1) = (Pit.X - PitHalf, Pit.X + PitHalf, Pit.Y - PitHalf, Pit.Y + PitHalf);
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(5).Table(48, 36)
            .Army("Red", 2, 1, weapons: [])
            .Army("Blue", 1, 1, weapons: [])
            .Terrain("deck", new Point3(24, 18, 0), volumes: [Block(0, px0, 14, 22), Block(px1, 48, 14, 22), Block(px0, px1, 14, py0), Block(px0, px1, py1, 22)])
            .Scripted(script.Build())
            .Build();
        return Game(name, scenario,
        [
            Check("p.7: \"may never move through other models or units\": a model crossing above a model on a lower level does not move through it", log =>
            {
                Guid Model(string unit) => log.Run.Final.Units.Values.Single(u => u.Definition.Name == unit).Definition.Models[0].Id;
                var (high, low) = (Model("Red unit 1"), Model("Red unit 2"));
                var crossing = log.Moves.FirstOrDefault(m => m.Move.ModelId == high && m.Move is { From: OnTable f, To: OnTable t }
                    && f.Placement.Position.X < Pit.X - 1.26 && t.Placement.Position.X > Pit.X + 1.26);
                if (crossing.Move is not { From: OnTable from, To: OnTable to }) return "Red unit 1 never crossed the pit";
                if (log.LocationsBefore(crossing.Seq)[low] is not OnTable under || !There(under.Placement.Position.Horizontal, Pit) || under.Placement.Position.Z > 1e-6)
                    return "Red unit 2 was not on the ground in the pit";
                if (from.Placement.Position.Z < DeckTop - 1e-6 || to.Placement.Position.Z < DeckTop - 1e-6)
                    return $"Red unit 1 did not cross on the deck (Z {from.Placement.Position.Z} to {to.Placement.Position.Z})";
                var body = new ModelBody(log.Run.Final.Models[high].Slot.Base, log.Run.Final.Models[high].Slot.Height);
                var below = new PlacedModel(new ModelBody(log.Run.Final.Models[low].Slot.Base, log.Run.Final.Models[low].Slot.Height), under.Placement);
                return Collision.Crosses(new PlacedModel(body, from.Placement), to.Placement.Position.Horizontal, [below], [])
                    ? null : "the crossing's path does not pass over Red unit 2's base in plan";
            }),
        ]);
    }
}
