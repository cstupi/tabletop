using Tabletop.Content;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>
    /// The 3.6 weapon-rule games: the 3.6 hand part's weapon and attack rules, grouped army:3.6:&lt;book uid&gt; by a 3.6 book that prints them. The rule's
    /// side shoots first and every round (Play.Shoots); a target state or terrain a rule reads is placed before the first answer.
    /// </summary>
    private static partial class ArmyGames
    {
        static readonly (string Uid, string Army)
            War36 = ("31xjrm9ivdimkjxp", "War Disciples"), Guerrillas36 = ("5wbcv465hacdwvkb", "Rebel Guerrillas"),
            LustDaemons36 = ("9qvy1oufoangt2gl", "Daemons of Lust"), MachineCults36 = ("7el7k3hgy5pb9o9i", "Machine Cults"),
            Dao36 = ("z8205ez2boggzs22", "DAO Union"), HighElves36 = ("7i7blhft75q9zfdc", "High Elf Fleets"),
            Hives36 = ("w7qor7b2kuifcyvk", "Alien Hives"), Sisters36 = ("7oi8zeiqfamiur21", "Blessed Sisters");

        /// <summary>The game under 3.6, in its book's 3.6 army group.</summary>
        static TestGame In36(TestGame game, (string Uid, string Army) book) =>
            game with { Group = ArmyGroup(RuleSets.Gf360, book.Uid), Edition = RuleSets.Gf360 };

        /// <summary>One shooting at one Shaken enemy unit (set before the first answer): the rule's unit acts first, and the round cap of one ends the game
        /// before the target can rally. Five models of four attacks, the first eight hit dice fixed to 6, so the target blocks at least eight hits.</summary>
        static TestGame AtShaken((string Uid, string Army) book, string name, string entry, GameCheck check)
        {
            var game = Game(book, name, Play.Shoots, [entry], [], [Fix("hit", 6, 6, 6, 6, 6, 6, 6, 6)], [check], weapon: [R(0, entry)], models: 5, units: 1, enemyUnits: 1, rounds: 1);
            var armies = game.Scenario.Armies;
            return In36(game with { Scenario = game.Scenario with { Armies = [armies[0], armies[1] with { Conditions = [(1, "shaken")] }] } }, book);
        }

        const string Fives = "\"the target must re-roll successful unmodified Defense results of 5-6\"";

        public static IReadOnlyList<TestGame> Army36A =>
        [
            AtShaken(War36, "war-disciples-purge", "Purge",
                ApGrant("Purge", With("Purge"), 2, "\"When attacking units that are Shaken or Fatigued, this weapon gets AP(+2)\"")),
            AtShaken(Guerrillas36, "rebel-guerrillas-reap", "Reap",
                Rerolls("Reap", With("Reap"), [5, 6], successOnly: true, $"\"When attacking units that are Shaken or Fatigued\", {Fives}")),
            // The volley's targets are Tough(3): "units where most models have Tough(3) or higher".
            In36(Game(LustDaemons36, "daemons-of-lust-obliterate", Play.Shoots, ["Obliterate"], [], [],
                [Rerolls("Obliterate", With("Obliterate"), [5, 6], successOnly: true, $"\"when attacking units where most models have Tough(3) or higher\", {Fives}")],
                weapon: [R(0, "Obliterate")]), LustDaemons36),
            In36(Game(Hives36, "alien-hives-fragment", Play.Shoots, ["Fragment"], [], [],
                [Rerolls("Fragment", With("Fragment"), [5, 6], successOnly: true, $"\"when attacking units where most models have Defense 5+ to Defense 6+\", {Fives}")],
                weapon: [R(0, "Fragment")], enemyStats: Stats(4, 5)), Hives36),
            In36(Game(Dao36, "dao-union-puncture", Play.Shoots, ["Puncture"], [], [],
                [ApGrant("Puncture", With("Puncture"), 2, "\"when attacking units where most models have Defense 2+ to Defense 3+, this weapon gets AP(+2)\"")],
                weapon: [R(0, "Puncture")], enemyStats: Stats(4, 3)), Dao36),
            In36(Game(MachineCults36, "machine-cults-demolish", Play.Shoots, ["Demolish"], [], [],
                [ApGrant("Demolish", With("Demolish"), 1, "\"when attacking units with most models within 1\" of terrain, this weapon gets AP(+1)\""),
                 NoCover("Demolish", With("Demolish"), "\"Ignores Cover\"")],
                weapon: [R(0, "Demolish")], board: EnemyRuins), MachineCults36),
            In36(Game(HighElves36, "high-elf-fleets-fracture", Play.Shoots, ["Fracture"], [], [],
                [PerDieAp("Fracture", With("Fracture"), 2, "\"For each unmodified result of 6 to hit, those hits get AP(+2)\"")],
                weapon: [R(0, "Fracture")]), HighElves36),
            // Shoots holds every activation (Immobile), and the volley's targets are Tough(3).
            In36(Game(Dao36, "dao-union-slayer", Play.Shoots, ["Slayer"], [R(1, "Slayer"), R(2, "Slayer")], [],
                [ApGrant("Slayer", By("Slayer", when: When.Shooting), 2, "\"When using Hold actions and shooting ... this model's weapons get AP(+2) if most models in the target have Tough(3) or higher\"")]), Dao36),
            // Shoots crosses at least 12" with every shot.
            In36(Game(Sisters36, "blessed-sisters-piercing-marksman", Play.Shoots, ["Piercing Marksman"], [R(1, "Piercing Marksman"), R(2, "Piercing Marksman")], [],
                [ApGrant("Piercing Marksman", By("Piercing Marksman", when: When.Shooting), 1, "\"When shooting at enemies over 9\" away, this model's weapons get AP(+1)\"")]), Sisters36),
        ];
    }
}
