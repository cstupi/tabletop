using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// 3.6 hand part, batch B (unit, defensive and utility rules): one game per mechanism the batch adds, grouped army:3.6:&lt;book uid&gt;.
/// Counter in Melee Aura gives Counter in Melee to the unit with no holding model, so every model's weapons strike first; Spell Resistance
/// splits its ignore roll by the wound's attack kind.
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        static readonly (string Uid, string Army) Dao36 = ("z8205ez2boggzs22", "DAO Union"), Change36 = ("r6hr29338u4micfw", "Change Disciples");

        public static IReadOnlyList<TestGame> Army36B { get; } =
        [
            // Only the opponents move, so every melee is a charge at the Aura's units; one model of each carries it, and every counter Ask is answered strike.
            In36(Game(Dao36, "dao-union-counter-in-melee-aura", Play.Charged, ["Counter in Melee Aura"], [], [], [CounterInMeleeForTheUnit("Counter in Melee Aura")],
                units: 3, enemyUnits: 4, carriers: [.. Enumerable.Range(1, 3).Select(u => new ScenarioModel(u, 1, [R(u, "Counter in Melee Aura")]))],
                more: s => { for (var i = 0; i < 40; i++) s.At("counter", i, new Chosen("strike")); })),
            In36(Carried(Change36, "change-disciples-spell-resistance", "Spell Resistance", [R(1, "Spell Resistance")],
                IgnoreWounds("Spell Resistance", 6, "\"If the wound was not from a spell, then it is ignored on a 6+ instead\""))),
        ];

        /// <summary>The game under 3.6.0, in its 3.6 book group.</summary>
        static TestGame In36(TestGame game) => game with { Edition = RuleSets.Gf360 };
    }
}
