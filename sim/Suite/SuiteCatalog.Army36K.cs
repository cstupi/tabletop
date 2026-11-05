using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// 3.6 hand part, batch K: Fortified Growth's "to a min. of AP(0)", and Split's placement, its new unit holding no objective on the round it is placed.
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        /// <summary>The Split model's anchor; the marker stands 5" east of it, out of its 3", under the split unit's models placed 4.5" east of it.</summary>
        static readonly Point2 SplitAt = new(8, 32);

        const string FortifiedGrowth36 = "\"If most models in this unit have this rule,  place one token on it at the beginning of each round if it's on the table and isn't Shaken, up to a max. of four tokens. For every two tokens, enemies attacking this unit get AP(-1) to a min. of AP(0)\"";

        /// <summary>
        /// Fortified Growth against AP(1): the blocks of a unit holding fewer than two tokens roll under AP(1), with two or more under none, four tokens
        /// included (AP(1) less two is held at AP(0)). A run whose unit never blocks with four tokens leaves the minimum untested and fails.
        /// </summary>
        static GameCheck GrowthFloor(string quote) =>
            Exact("Fortified Growth", $"{quote} (against AP(1))", (run, _) =>
            {
                var blocks = (from x in Marked(run, "Fortified Growth")
                              from r in x.Step.Rolls.Where(r => r.Roll == "defense")
                              let held = x.Held.GetValueOrDefault(x.Frame.Context.TargetUnitIds.FirstOrDefault())
                              select (Held: held, Ap: r.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount))).ToList();
                var verdicts = blocks.Select(b => b.Ap == (b.Held < 2 ? -1 : 0) ? null : $"AP modifier {b.Ap:+0;-0} with {b.Held} tokens").ToList();
                if (blocks.Count > 0 && blocks.All(b => b.Held < 4)) verdicts.Add("no block with four tokens");
                return verdicts;
            });

        public static IReadOnlyList<TestGame> Army36K =>
        [
            // Tough(12) on every model, one enemy unit: the carriers stand to round 4, when the unit holds four tokens.
            Game(Books36.DaemonsOfPlague, "daemons-of-plague-fortified-growth", Play.Shot, ["Fortified Growth"], [R(1, "Tough", 12)], [],
                [GrowthFloor(FortifiedGrowth36), GrowsEachRound("Fortified Growth", FortifiedGrowth36)],
                units: 1, carriers: Most(1, "Fortified Growth"), enemyUnits: 1, enemyWeapon: [R(0, "AP", 1)]).In36(),
            // By construction: the Split unit is one model, the opponent one model with one three-attack rifle whose first hit die is fixed to 6 and the
            // block to 1, so its first shot destroys the unit; the new unit is placed 4.5" east of the last model, by the marker no other model stands near.
            Game(Books36.DaemonsOfChange, "daemons-of-change-split", Play.Shot, ["Split"], [R(1, "Split")], [Fix("hit", 6), Fix("defense", 1)],
                [Places("Split", "\"When this unit is fully destroyed, you may place a new unit of X fully within 6\" of it before removing the last model\""),
                 NoObjectivesOnPlacedRound("Split", "\"Units placed via Split can’t seize or contest objectives on the round they are placed\"")],
                units: 2, sizes: new Dictionary<int, int> { [1] = 1, [2] = 2 }, reserves: [new ScenarioReserve(2, "Split", 1)],
                enemyUnits: 1, enemyModels: 1, enemyAttacks: 3, anchors: new Dictionary<int, Point2> { [1] = SplitAt },
                board: b => b.Marker(MarkerKind.Objective, new Point3(SplitAt.X + 5, SplitAt.Y, 0), label: "Split"),
                more: s => s.Match("place", (ask, p) => ask.Legal is MoveRegion { Containment.ExitingId: { } at } m && p.Models[at].Location is OnTable { Placement: var c }
                    ? new Destinations(m.ModelIds.Select((id, i) => new ModelRelocation(id, new OnShelf(),
                        new OnTable(new Placement(new Point3(c.Position.X + 4.5, c.Position.Y + (i - 1) * 1.5, 0), new Yaw(0))))).ToList())
                    : null)).In36(),
        ];
    }
}
