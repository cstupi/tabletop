using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

/// <summary>
/// 3.6 hand part, batch C (the *born/*bound rules and their Enhanced variants): one game per mechanism the batch adds, grouped army:3.6:&lt;book uid&gt;.
/// An Enhanced rule suppresses its base on the holder and plays the stronger value (Enhanced Plaguebound ignores on 5+, and Plaguebound does not
/// roll beside it); Knightborn's wound-ignore roll holds only when shot from over 9".
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        static readonly (string Uid, string Army) Plague36 = ("jlray7cwf8mvw5sn", "Plague Disciples"), Knight36 = ("w70ha3o85pa7nigq", "Knight Brothers");

        const string KnightbornQuote = "\"If the wound was not from a spell, and the unit was shot/charged from over 9\"/6\" away, then it is ignored on a 6+ instead\"";

        public static IReadOnlyList<TestGame> Army36C { get; } =
        [
            In36(Also(Carried(Plague36, "plague-disciples-enhanced-plaguebound", "Enhanced Plaguebound", [R(1, "Plaguebound"), R(1, "Enhanced Plaguebound")],
                IgnoreWounds("Enhanced Plaguebound", 5, "\"they ignore wounds on rolls of 5-6 from Plaguebound (instead of only on 6+)\"")),
                BaseReplaced("Enhanced Plaguebound", "Plaguebound", "\"from Plaguebound (instead of only on 6+)\""))),
            // Both sides hold in their deployment zones, so every shot is from over 9".
            In36(Carried(Knight36, "knight-brothers-knightborn", "Knightborn", [R(1, "Knightborn")], IgnoreWounds("Knightborn", 6, KnightbornQuote))),
            // Carried's shot with the two units held 4.5" apart: no shot is from over 9", so no wound is rolled for. Knightborn's occasion is a wound dealt to it.
            In36(With(Game(Knight36, "knight-brothers-knightborn-within-9", Play.Shot, ["Knightborn"], [R(1, "Knightborn")], [Fix("hit", 6, 6, 6), Fix("defense", 1, 1, 1)],
                [NoIgnoreWithin("Knightborn", KnightbornQuote)], enemyUnits: 1, enemyModels: 1, enemyAttacks: 3, rounds: 2, seed: 4, length: 26,
                anchors: new Dictionary<int, Point2> { [1] = new(16, 15) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(16, 10.5) }),
                ("Knightborn", log => log.Any(e => e.Payload is StepCompleted { Step: "assign" })))),
        ];

        static TestGame Also(TestGame game, GameCheck check) => game with { Checks = [.. game.Checks, check] };
    }
}
