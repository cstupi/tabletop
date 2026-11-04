namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: move bonuses (p.7: "Advance - Moves 6", "Rush - Moves 12", "Charge - Moves 12"), a range bonus, Swift, and Guerrilla's move.</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) Guerrillas => ("5wbcv465hacdwvkb", "Rebel Guerrillas");

        public static IReadOnlyList<TestGame> Movement =>
        [
            Game(DarkElves, "dark-elf-raiders-agile", Play.Moves, ["Agile"], Each(4, "Agile"), [],
                [Allowance("Agile", null, "\"Moves +1\" when using Advance, and +2” when using Rush/Charge\"", ("advance", 7), ("rush", 14), ("charge", 14))]),
            Game(Hives, "alien-hives-rapid-charge", Play.Charges, ["Rapid Charge"], Each(4, "Rapid Charge"), [],
                [Allowance("Rapid Charge", null, "\"This model moves +4\" when using Charge actions\"", ("charge", 16))]),
            // A 12" rifle: the armies stand at least 12" apart, so every shot that lands is one the +6" reached.
            AnchoredH(Game(Hives, "alien-hives-increased-shooting-range", Play.Shoots, ["Increased Shooting Range"], Each(2, "Increased Shooting Range"), [],
                [RangeBonus("Increased Shooting Range", By("Increased Shooting Range", when: When.Shooting), 12, 6, "\"This model gets +6\" range when shooting.\"")], gunRange: 12), 25, 10),
            // p.14 Slow, "move -2" when using Advance, and -4" when using Rush/Charge", on every unit; Swift leaves p.7's distances.
            Game(Dwarves, "dwarf-guilds-swift", Play.Moves, ["Swift"], Each(4, "Slow", "Swift"), [],
                [Allowance("Swift", null, "\"This model may ignore the Slow rule.\"", ("advance", 6), ("rush", 12), ("charge", 12))]),
            Game(Guerrillas, "rebel-guerrillas-guerrilla", Play.Shoots, ["Guerrilla", "Guerrilla Boost"], [R(1, "Guerrilla"), R(2, "Guerrilla"), R(2, "Guerrilla Boost")], [],
                [Reposition("Guerrilla", 3, "\"may move by up to 3\" after shooting or being in melee\""), Reposition("Guerrilla Boost", 6, "\"they may move by up to 6\" from Guerrilla\"")]),
        ];
    }
}
