using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: moving off steep terrain is open ground (3.6.0 p.12 Steep; 3.5.1 p.12 Hills).</summary>
internal static partial class CoreGames
{
    // 36" by 24": a steep hill 6" by 20", 3" tall, over x 6–12, y 2–22. Red unit 1 and Red unit 2 (one 32 mm model each) stand on its east edge,
    // their centres 0.4" past it at x 12.4 and their bases on its top. Red unit 1 Rushes 11" east off it; Red unit 2 charges Blue unit 1 at
    // (24, 18), 10.34" from the hill to base contact. Under strict enforcement a move over a 6" cap is refused, and the game would stick.
    static readonly Point2 RimRushFrom = new(12.4, 6), RimRushTo = new(23.4, 6), RimChargeFrom = new(12.4, 18), RimTarget = new(24, 18);

    /// <summary>
    /// 3.6.0 p.12 Steep: "Units moving onto steep terrain count it as being in Difficult terrain, but units moving off it count it as open
    /// terrain." 3.5.1 p.12 Hills: "Difficult when moving up". A model standing on a hill by its base edge moves off it: a Rush and a Charge
    /// from the hill's edge are held to their own 12", not to 6".
    /// </summary>
    public static TestGame OffTheHill()
    {
        const string name = "core-off-the-hill";
        var landing = new Point2(RimTarget.X - Scripts36.Touch, RimTarget.Y);
        var script = AnswerScript.Build()
            .Match(AskQuestions.Activate, (ask, _) => AskPicks.Activate(ask, "Red unit 1", "Rush") ?? AskPicks.Activate(ask, "Red unit 2", "Charge")
                ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"))
            .Match("charge_target", (ask, p) => AskPicks.Pieces(ask, p, "Blue unit 1"))
            .Match("move", (ask, p) => ask.Legal is MoveRegion { ContactWith: null, Containment: null } region && Scripts36.UnitOf(p, region) == "Red unit 1"
                ? new Destinations([Scripts36.MoveTo(p, region.ModelIds[0], RimRushTo)])
                : Scripts36.Charging(new Dictionary<string, Point2> { ["Red unit 2"] = landing })(ask, p))
            .Build();
        var hill = new Volume(VolumeKind.Solid, Footprint.Rect(6, 20), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Steep, null);
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(1).Table(36, 24).Zones(ZoneStyles.GroundWar).Scripted(script)   // Red deploys in x 0–18, Blue in x 18–36
            .Army("Red", 2, 1, weapons: [HandWeapon()], anchors: new Dictionary<int, Point2> { [1] = RimRushFrom, [2] = RimChargeFrom })
            .Army("Blue", 1, 1, weapons: [HandWeapon()], anchors: new Dictionary<int, Point2> { [1] = RimTarget })
            .Terrain("hill", new Point3(9, 12, 0), volumes: [hill])
            .Build();

        Func<CoreLog, string?> Moved(string unit, Point2 from, Point2 to) => log =>
        {
            var model = log.Run.Final.Units.Values.Single(u => u.Definition.Name == unit).Definition.Models[0].Id;
            var move = log.Moves.FirstOrDefault(m => m.Move.ModelId == model && m.Move is { From: OnTable f, To: OnTable t }
                && f.Placement.Position.Horizontal.DistanceTo(from) < 1e-3 && t.Placement.Position.Horizontal.DistanceTo(to) < 0.01);
            if (move.Move is not { From: OnTable start, To: OnTable end }) return $"{unit} never moved from {from} to {to}";
            if (start.Placement.Position.Z < 3 - 1e-6) return $"{unit} started at height {start.Placement.Position.Z:0.00}, not on the hill";
            var length = from.DistanceTo(end.Placement.Position.Horizontal);
            return length > 6 ? null : $"{unit} moved only {length:0.00}\"";
        };
        return Game(name, scenario,
        [
            Check("3.6.0 p.12 Steep: \"units moving off it count it as open terrain\" (3.5.1 p.12 Hills: \"Difficult when moving up\"): a Rush off a hill from its edge goes 11\"",
                Moved("Red unit 1", RimRushFrom, RimRushTo)),
            Check("3.6.0 p.12 Steep: \"units moving off it count it as open terrain\" (3.5.1 p.12 Hills: \"Difficult when moving up\"): a Charge off a hill from its edge goes 10.34\" into base contact",
                Moved("Red unit 2", RimChargeFrom, landing)),
        ]);
    }
}
