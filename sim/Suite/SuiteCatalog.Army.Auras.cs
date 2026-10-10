using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: the written-out Auras ("This model and its unit get …"). Model 1 of each rule unit carries the Aura; the checks read the
    /// attacks made while a carrier stands.</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) BattleBrothers => ("78qp9l5alslt6yj8", "Battle Brothers");
        static TestGame Melee((string Uid, string Army) book, string name, string aura, GameCheck check, IReadOnlyList<ScenarioRule>? enemy = null) =>
            Game(book, name, Play.Charges, [aura], [], [], [check], carriers: Carriers(aura, 3), units: 3, enemy: enemy);
        static TestGame Shooting((string Uid, string Army) book, string name, string aura, GameCheck check, IReadOnlyList<ScenarioRule>? enemy = null,
            Func<Tabletop.Sim.Scenarios.ScenarioBuilder, Tabletop.Sim.Scenarios.ScenarioBuilder>? board = null, Play play = Play.Shoots) =>
            Game(book, name, play, [aura], [], [], [check], carriers: Carriers(aura, 2), units: 2, enemy: enemy, board: board);

        public static IReadOnlyList<TestGame> GrantAuras =>
        [
            Melee(BattleBrothers, "battle-brothers-bane-in-melee-aura", "Bane in Melee Aura",
                Rerolls("Bane in Melee Aura", Carrying("Bane in Melee Aura", When.Charging), [6], successOnly: false, "\"This model and its unit get Bane in melee.\": p.13 Bane, \"re-roll unmodified Defense results of 6\"")),
            Shooting(BattleBrothers, "battle-brothers-bane-when-shooting-aura", "Bane when Shooting Aura",
                Rerolls("Bane when Shooting Aura", Carrying("Bane when Shooting Aura", When.Shooting), [6], successOnly: false, "\"This model and its unit get Bane when shooting.\": p.13 Bane, \"re-roll unmodified Defense results of 6\"")),
            Shooting(Dynasty, "eternal-dynasty-ignores-cover-aura", "Ignores Cover Aura",
                NoCover("Ignores Cover Aura", Carrying("Ignores Cover Aura", When.Shooting), "\"This model and its unit get Ignores Cover when shooting.\""), board: EnemyRuins),
            Shooting(Dwarves, "dwarf-guilds-ignores-cover-when-shooting-aura", "Ignores Cover when Shooting Aura",
                NoCover("Ignores Cover when Shooting Aura", Carrying("Ignores Cover when Shooting Aura", When.Shooting), "\"This model and its unit get Ignores Cover when shooting.\""), board: EnemyRuins),
            // Moves: the first half of the units Advance and shoot, so p.14 Indirect's "-1 to hit rolls when shooting after moving" is read.
            Shooting(ChangeDaemons, "wormhole-daemons-of-change-indirect-when-shooting-aura", "Indirect when Shooting Aura",
                GrantedModifier("Indirect when Shooting Aura", (run, a) => Carrying("Indirect when Shooting Aura", When.Shooting)(run, a) && a.Hit?.Nearest("advance") is not null,
                    "Indirect", "hit", -1, "\"This model and its unit get Indirect when shooting.\": p.14 Indirect, \"Gets -1 to hit rolls when shooting after moving\""), play: Play.Moves),
            Melee(Guerrillas, "rebel-guerrillas-rending-in-melee-aura", "Rending in Melee Aura",
                PerDieAp("Rending in Melee Aura", Carrying("Rending in Melee Aura", When.Charging), 4, "\"This model and its unit get Rending in melee.\": p.14 Rending, \"those hits get AP(+4)\"")),
            Shooting(MachineCults, "machine-cults-rending-when-shooting-aura", "Rending when Shooting Aura",
                PerDieAp("Rending when Shooting Aura", Carrying("Rending when Shooting Aura", When.Shooting), 4, "\"This model and its unit get Rending when shooting.\": p.14 Rending, \"those hits get AP(+4)\"")),
            Melee(Sisters, "blessed-sisters-shred-in-melee-aura", "Shred in Melee Aura",
                ExtraWounds("Shred in Melee Aura", Carrying("Shred in Melee Aura", When.Charging), [1], "\"This model and its unit get Shred in melee.\": Shred, \"this weapon deals 1 extra wound\"")),
            Shooting(Custodians, "custodian-brothers-shred-when-shooting-aura", "Shred when Shooting Aura",
                ExtraWounds("Shred when Shooting Aura", Carrying("Shred when Shooting Aura", When.Shooting), [1], "\"This model and its unit get Shred when shooting.\": Shred, \"this weapon deals 1 extra wound\"")),
            Game(Infected, "infected-colonies-thrust-in-melee-aura", Play.Charges, ["Thrust in Melee Aura"], [], [],
                [GrantedModifier("Thrust in Melee Aura", Carrying("Thrust in Melee Aura", When.Charging), "Thrust", "hit", 1, "\"This model and its unit get Thrust in melee.\": p.14 Thrust, \"When charging, gets +1 to hit rolls\""),
                 ApGrant("Thrust in Melee Aura", Carrying("Thrust in Melee Aura", When.Charging), 1, "\"This model and its unit get Thrust in melee.\": p.14 Thrust, \"and AP(+1) in melee\"")],
                carriers: Carriers("Thrust in Melee Aura", 3), units: 3),
            Melee(Custodians, "custodian-brothers-unstoppable-in-melee-aura", "Unstoppable in Melee Aura",
                NoPenalty("Unstoppable in Melee Aura", Carrying("Unstoppable in Melee Aura", When.Charging), "\"This model and its unit get Unstoppable in melee.\": p.15 Unstoppable"),
                enemy: Each(2, "Melee Evasion")),
            // Stealth on the opponents (shot from over 9") is the negative modifier Unstoppable ignores; Tough(3) keeps them standing.
            Shooting(Robots, "robot-legions-unstoppable-when-shooting-aura", "Unstoppable when Shooting Aura",
                NoPenalty("Unstoppable when Shooting Aura", Carrying("Unstoppable when Shooting Aura", When.Shooting), "\"This model and its unit get Unstoppable when shooting.\": p.15 Unstoppable"),
                enemy: [.. Each(2, "Stealth"), R(1, "Tough", 3), R(2, "Tough", 3)]),
        ];
    }
}
