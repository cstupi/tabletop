using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: the move Ask names the terrain dangerous to the move, by edition and action, for the clients' banded areas.</summary>
internal static partial class CoreGames
{
    /// <summary>
    /// 48" by 36", Ground War zones. Red unit 1 Advances and Red unit 2 Rushes 1" east, far from a minefield (Dangerous) in one corner and ruins
    /// (DangerousOnRushCharge) in another. p.11: "If a model moves in or through dangerous terrain ... then it must take a dangerous terrain test";
    /// 3.5.1 p.12: "Ruins - Cover + Dangerous when using rush/charge"; 3.6.0 p.12 Unstable: "If any model in a unit moves over 6” when moving into
    /// or through unstable terrain, then it counts as Dangerous terrain". Each move Ask names the volumes dangerous to that move.
    /// </summary>
    public static TestGame MoveDangers()
    {
        const string name = "core-move-dangers";
        var script = AnswerScript.Build()
            .Match(AskQuestions.Activate, (ask, _) => AskPicks.Activate(ask, "Red unit 1", "Advance") ?? AskPicks.Activate(ask, "Red unit 2", "Rush")
                ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"))
            .Match("move", (ask, p) => ask.Legal is MoveRegion { ContactWith: null, Containment: null } region
                && AskPicks.UnitNameOf(p, region.ModelIds[0]) is "Red unit 1" or "Red unit 2"
                ? new Destinations([.. region.ModelIds.Select(id => Scripts36.MoveBy(p, id, 1, 0))]) : null)
            .Build();
        static Volume Piece(TerrainDeclarations d) => new(VolumeKind.Area, Footprint.Rect(3, 3), new Point2(0, 0), new Yaw(0), 0, 2, d, null);
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(1).Table(48, 36).Zones(ZoneStyles.GroundWar).Scripted(script)
            .Army("Red", 2, 1, weapons: [HandWeapon()], anchors: new Dictionary<int, Point2> { [1] = new(10, 20), [2] = new(10, 12) })
            .Army("Blue", 1, 1, weapons: [HandWeapon()], anchors: new Dictionary<int, Point2> { [1] = new(44, 18) })
            .Terrain("minefield", new Point3(3, 33, 0), volumes: [Piece(TerrainDeclarations.Dangerous)])
            .Terrain("ruins", new Point3(45, 3, 0), volumes: [Piece(TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge)])
            .Build();
        return Game(name, scenario,
        [
            Check("p.11, 3.5.1 p.12, 3.6.0 p.12: the move Ask names the dangerous terrain of the move (3.5.1: ruins on a Rush only; 3.6.0: ruins as Unstable over 6\" on any move)", log =>
            {
                var terrain = log.Run.Final.Board.Table.Terrain.SelectMany(t => t.Volumes).ToList();
                var mine = terrain.Single(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Dangerous)).Id;
                var ruin = terrain.Single(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.DangerousOnRushCharge)).Id;
                var v36 = log.Run.Game.Edition == RuleSets.Gf360;
                var asks = log.Events.Select(e => e.Payload).OfType<AskOpened>().Select(o => o.Ask)
                    .Where(a => a.Legal is MoveRegion && log.Frame(a.FrameId)?.Sequence is "advance" or "rush").ToList();
                var seen = asks.Select(a => log.Frame(a.FrameId)!.Sequence).ToHashSet();
                if (!seen.SetEquals(["advance", "rush"])) return $"move Asks seen in {string.Join(", ", seen)}; an Advance and a Rush are needed";
                return Require(asks, "no move Ask", a =>
                {
                    var region = (MoveRegion)a.Legal;
                    var rush = log.Frame(a.FrameId)!.Sequence == "rush";
                    Guid[] dangerous = v36 || !rush ? [mine] : [mine, ruin];
                    Guid[]? unstable = v36 ? [ruin] : null;
                    double? over = v36 ? 6 : null;
                    var ok = region.DangerousVolumeIds?.ToHashSet().SetEquals(dangerous) == true
                        && (unstable is null ? region.UnstableVolumeIds is null : region.UnstableVolumeIds?.ToHashSet().SetEquals(unstable) == true)
                        && region.UnstableOver == over;
                    return ok ? null : $"{(rush ? "Rush" : "Advance")}: dangerous {region.DangerousVolumeIds?.Count ?? 0}, unstable {region.UnstableVolumeIds?.Count ?? 0}, over {region.UnstableOver?.ToString() ?? "none"}";
                });
            }),
        ]);
    }
}
