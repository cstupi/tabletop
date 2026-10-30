using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// 3.6 hand part, batch K: the entries batch J left without a game.
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        static readonly (string Uid, string Army) PlagueDaemons36 = ("fmuqw5lr5l6dq0mq", "Daemons of Plague");

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
            In36(Game(PlagueDaemons36, "daemons-of-plague-fortified-growth", Play.Shot, ["Fortified Growth"], [R(1, "Tough", 12)], [],
                [GrowthFloor(FortifiedGrowth36), GrowsEachRound("Fortified Growth", FortifiedGrowth36)],
                units: 1, carriers: Most(1, "Fortified Growth"), enemyUnits: 1, enemyWeapon: [R(0, "AP", 1)])),
        ];
    }
}
