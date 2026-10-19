using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: moves measured along the path the base takes, round terrain (p.5, p.11).</summary>
internal static partial class CoreGames
{
    // 48" by 36". Red unit 1 stands at WoodFrom with a 2" wood (x 13–15, y 9–11) on its straight line to WoodTo, 11" away; round the wood it is
    // about 11.6". Red unit 2 stands at BlockFrom with a 2" by 4" impassable block (x 11–13, y 24–28) on its line to BlockTo, 7" away; round the
    // block it is about 9.4". Both Rush (12") under strict, so each move is applied only when the server finds no breach.
    static readonly Point2 WoodFrom = new(8, 10), WoodTo = new(19, 10), BlockFrom = new(8, 26), BlockTo = new(15, 26);

    /// <summary>
    /// p.5 "When measuring how far a model moves always measure so that no part of its base moves further than the total distance": the path may
    /// bend, and its length is what counts. p.11 "If any model in a unit moves in or through difficult terrain at any point of its move, then all
    /// models in the unit may not move more than 6"": a Rush that goes round a wood never moves in it and is not held to 6". p.11 "Units may not
    /// move through impassable terrain": a model reaches a spot behind an impassable block by going round it.
    /// </summary>
    public static TestGame RoundTheTerrain()
    {
        const string name = "core-round-the-terrain";
        static Guid? ModelOf(Projection p, string unit) => p.Units.Values.FirstOrDefault(u => u.Definition.Name == unit)?.Definition.Models[0].Id;
        static Point2? At(Projection p, Guid? model) => model is { } id && p.Models[id].Location is OnTable t ? t.Placement.Position.Horizontal : null;
        static bool There(Point2? at, Point2 target) => at is { } a && a.DistanceTo(target) < 1e-3;

        var script = AnswerScript.Build();
        script.Match("activate", (ask, p) =>
            (There(At(p, ModelOf(p, "Red unit 1")), WoodTo) ? null : AskPicks.Activate(ask, "Red unit 1", "Rush"))
            ?? (There(At(p, ModelOf(p, "Red unit 2")), BlockTo) ? null : AskPicks.Activate(ask, "Red unit 2", "Rush"))
            ?? AskPicks.Activate(ask, "Red unit 1", "Hold") ?? AskPicks.Activate(ask, "Red unit 2", "Hold") ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"));
        script.Match("move", (ask, p) =>
        {
            if (ask.Legal is not MoveRegion { Containment: null } region || region.ModelIds.Count != 1) return null;
            var id = region.ModelIds[0];
            Point2? target = AskPicks.UnitNameOf(p, id) switch { "Red unit 1" => WoodTo, "Red unit 2" => BlockTo, _ => null };
            if (target is not { } t) return null;
            var yaw = ((OnTable)p.Models[id].Location).Placement.Yaw;
            return new Destinations([new ModelRelocation(id, p.Models[id].Location, new OnTable(new Placement(new Point3(t.X, t.Y, 0), yaw)))]);
        });

        static Volume Piece(double width, double length, TerrainDeclarations d, VolumeKind kind) =>
            new(kind, Footprint.Rect(width, length), new Point2(0, 0), new Yaw(0), 0, 3, d, null);
        var (Wood, Block) = (Piece(2, 2, TerrainDeclarations.Difficult, VolumeKind.Area), Piece(2, 4, TerrainDeclarations.Impassable, VolumeKind.Solid));
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(5).Table(48, 36).Zones(ZoneStyles.GroundWar)   // Red deploys in x 0–24, Blue in x 24–48
            .Army("Red", 2, 1, weapons: [], anchors: new Dictionary<int, Point2> { [1] = WoodFrom, [2] = BlockFrom })
            .Army("Blue", 1, 1, weapons: [], anchors: new Dictionary<int, Point2> { [1] = new(44, 18) })
            .Terrain("wood", new Point3(14, 10, 0), volumes: [Wood])
            .Terrain("block", new Point3(12, 26, 0), volumes: [Block])
            .Scripted(script.Build())
            .Build();

        Func<CoreLog, string?> Reached(string unit, Point2 from, Point2 to, Volume piece, Point3 at, double over) => log =>
        {
            var model = log.Run.Final.Units.Values.Single(u => u.Definition.Name == unit).Definition.Models[0].Id;
            var move = log.Moves.FirstOrDefault(m => m.Move.ModelId == model && m.Move is { From: OnTable f, To: OnTable t }
                && f.Placement.Position.Horizontal.DistanceTo(from) < 1e-3 && t.Placement.Position.Horizontal.DistanceTo(to) < 1e-3);
            if (move.Move is not { From: OnTable start }) return $"{unit} never moved from {from} to {to}";
            var body = new PlacedModel(new ModelBody(log.Run.Final.Models[model].Slot.Base, log.Run.Final.Models[model].Slot.Height), start.Placement);
            if (Movement.VolumesCrossed(body, to, [new WorldVolume(Guid.Empty, piece, new TerrainTransform(at, new Yaw(0), 1))]).Count == 0) return $"{unit}'s straight line does not cross the terrain";
            return from.DistanceTo(to) > over ? null : $"{unit} moved only {from.DistanceTo(to):0.00}\"";
        };
        return Game(name, scenario,
        [
            Check("p.5 and p.11: a Rush whose straight line crosses a wood, going round it, is not held to 6\" (\"in or through difficult terrain\")",
                Reached("Red unit 1", WoodFrom, WoodTo, Wood, new Point3(14, 10, 0), 6)),
            Check("p.5 and p.11: a move goes round an impassable block (\"Units may not move through impassable terrain\") to reach the spot behind it",
                Reached("Red unit 2", BlockFrom, BlockTo, Block, new Point3(12, 26, 0), 0)),
        ]);
    }
}
