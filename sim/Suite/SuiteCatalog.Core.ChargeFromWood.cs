using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: a charge offered only where the difficult cap lets it reach (3.5.1 p.11, 3.6.0 p.12; p.9 "a clear path to reach it").</summary>
internal static partial class CoreGames
{
    // 36" by 24": a wood 8" square over x 6–14, y 8–16. Red unit 1 (Fast) stands deep in it at (10, 13) and Red unit 3 (Strider) at (10, 9), Red unit 2 on
    // open ground at (12, 20.5). Blue unit 1 at (20, 12) is 8.7" to base contact from Red unit 1, 9.2" from Red unit 3 and 10.4" from Red unit 2: within
    // every charge distance, over the 6" cap. Every unit Holds in the one round.
    const string WoodNote = "No Charge: moving in or through Difficult terrain limits the move to 6\", which reaches no enemy unit";

    /// <summary>
    /// 3.5.1 p.11, 3.6.0 p.12: "If any model in a unit moves in or through Difficult terrain at any point of its move, then all models in the unit may not
    /// move more than 6” for that movement." p.9: a target is valid when a model "is within charge distance of one model from the target unit, and has a
    /// clear path to reach it". p.14 (3.6.0 p.15) Strider: "May ignore the effects of Difficult terrain".
    /// </summary>
    public static TestGame ChargeFromWood()
    {
        const string name = "core-charge-from-wood";
        var script = AnswerScript.Build()
            .Match(AskQuestions.Activate, (ask, _) => AskPicks.Activate(ask, "Red unit 1", "Hold") ?? AskPicks.Activate(ask, "Red unit 2", "Hold")
                ?? AskPicks.Activate(ask, "Red unit 3", "Hold") ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"))
            .Build();
        var wood = new Volume(VolumeKind.Area, Footprint.Rect(8, 8), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Difficult, null);
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(1).Table(36, 24).Zones(ZoneStyles.GroundWar).Scripted(script)   // Red deploys in x 0–18, Blue in x 18–36
            .Army("Red", 3, 1, weapons: [HandWeapon()], unitRules: [new(1, "Fast"), new(3, "Strider")],
                anchors: new Dictionary<int, Point2> { [1] = new(10, 13), [2] = new(12, 20.5), [3] = new(10, 9) })
            .Army("Blue", 1, 1, weapons: [HandWeapon()], anchors: new Dictionary<int, Point2> { [1] = new(20, 12) })
            .Terrain("wood", new Point3(10, 12, 0), volumes: [wood])
            .Build();

        // The first activate Ask that offers the unit: its options, before any unit has moved (every unit Holds).
        static IReadOnlyList<AskOption>? Offered(CoreLog log, string unit) =>
            log.Events.Select(e => e.Payload).OfType<AskOpened>().Select(o => o.Ask).Where(a => a.Question == AskQuestions.Activate)
                .Select(a => a.Legal is Options o ? o.Choices.Where(c => c.Label.StartsWith(unit + ":", StringComparison.Ordinal)).ToList() : [])
                .FirstOrDefault(c => c.Count > 0);
        Func<CoreLog, string?> Charge(string unit, bool offered) => log =>
        {
            if (Offered(log, unit) is not { } options) return $"{unit} was never offered an activation";
            var charge = options.Any(o => o.Label == $"{unit}: Charge");
            if (charge != offered) return $"{unit} was {(charge ? "" : "not ")}offered Charge";
            return offered || options.All(o => o.Note == WoodNote) ? null : $"{unit}'s options carried the note \"{options[0].Note}\"";
        };
        return Game(name, scenario,
        [
            Check("3.5.1 p.11, 3.6.0 p.12: \"all models in the unit may not move more than 6” for that movement\": a Fast unit in a wood 8.7\" from the enemy is not offered Charge",
                Charge("Red unit 1", false)),
            Check("p.9: \"within charge distance of one model from the target unit, and has a clear path to reach it\": a unit on open ground 10.4\" away is offered Charge",
                Charge("Red unit 2", true)),
            Check("p.14 (3.6.0 p.15) Strider: \"May ignore the effects of Difficult terrain\": a Strider unit in the wood 9.2\" away is offered Charge",
                Charge("Red unit 3", true)),
        ]);
    }
}
