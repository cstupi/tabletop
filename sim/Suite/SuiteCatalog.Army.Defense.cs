namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: rules of the unit being attacked. Every game is Shot: the opponents shoot from at least 12" away.</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) PlagueDaemons => ("fmuqw5lr5l6dq0mq", "Wormhole Daemons of Plague");
        static (string Uid, string Army) Inquisition => ("jpc0kyil0juwy602", "Human Inquisition");

        public static IReadOnlyList<TestGame> Defense =>
        [
            Game(Hives, "alien-hives-fortified", Play.Shot, ["Fortified"], [R(1, "Fortified")], [Fix("hit", 6, 6, 6)],
                [ApGrant("Fortified", On("Fortified", when: When.Shooting), 0, "\"those hits count as having AP(-1), to a min. of AP(0)\" (against AP(1))")],
                enemyWeapon: [R(0, "AP", 1)]),
            // Ruins cover the rule side's zone, so most of its models stand within 1" of terrain.
            Game(MachineCults, "machine-cults-grounded-stealth", Play.Shot, ["Grounded Stealth"], [R(1, "Grounded Stealth")], [],
                [Modifier("Grounded Stealth", "hit", -1, "\"enemies get -1 to hit rolls when attacking them\"")], board: Ruins),
            Game(PlagueDaemons, "wormhole-daemons-of-plague-screened", Play.Shot, ["Screened"], Both("Screened"), [],
                [Modifier("Screened", "hit", -1, "\"When units where all models have this rule are shot or charged from over 9\" away, enemy units get -1 to hit rolls\"")]),
            Game(Sisters, "blessed-sisters-guarded", Play.Shot, ["Guarded"], Both("Guarded"), [Fix("hit", 6, 6, 6)],
                [Modifier("Guarded", "defense", 1, "\"When units where all models have this rule are shot or charged from over 9\" away, they get +1 to defense rolls\"")]),
            Carried(Inquisition, "human-inquisition-protected", "Protected", [R(1, "Protected")], IgnoreWounds("Protected", 6, IgnoreOn6)),
        ];
    }
}
