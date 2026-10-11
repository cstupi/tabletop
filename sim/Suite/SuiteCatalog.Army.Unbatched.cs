namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: standalone rules no spec batch named that the grammar expresses, and the command-group upgrades Banner and Musician
    /// (texts from Army Forge's common rules, GET /api/rules/common/2).</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) BloodBrothers => ("xnnqhh1775kvmz2r", "Blood Brothers");
        static (string Uid, string Army) KnightBrothers => ("w70ha3o85pa7nigq", "Knight Brothers");
        static (string Uid, string Army) WolfBrothers => ("yxjboa8oma9bbdck", "Wolf Brothers");

        public static IReadOnlyList<TestGame> Unbatched =>
        [
            Game(Orcs, "orc-marauders-bad-shot", Play.Shoots, ["Bad Shot"], Both("Bad Shot"), [],
                [Modifier("Bad Shot", "hit", -1, "\"This model gets -1 to hit rolls when shooting.\"")]),
            Game(BloodBrothers, "blood-brothers-bloodborn", Play.Shoots, ["Bloodborn"], Both("Bloodborn"), [],
                [ExtraAttacks("Bloodborn", By("Bloodborn"), [6], "\"For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon\"")]),
            Game(Inquisition, "human-inquisition-brutal-fighter", Play.Charges, ["Brutal Fighter"], All3("Brutal Fighter"), [],
                [ExtraHits("Brutal Fighter", By("Brutal Fighter", when: When.Charging), [6], "\"When in melee, unmodified results of 6 to hit deal 1 extra hit\"")]),
            Carried(KnightBrothers, "knight-brothers-knightborn", "Knightborn", [R(1, "Knightborn")], IgnoreWounds("Knightborn", 6, IgnoreOn6)),
            Game(Change, "change-disciples-unpredictable", Play.Shoots, ["Unpredictable"], Both("Unpredictable"), [], [Unpredictable("Unpredictable")]),
            Game(WolfBrothers, "wolf-brothers-wolfborn", Play.Shoots, ["Wolfborn"], Both("Wolfborn"), [], [RapidBlink("Wolfborn", 1)]),
            // "May only be taken once per army" is a list-building rule: the engine judges no list, and in play the rule asks nothing. A game
            // with a Unique unit that plays to its end (Checks.Ended) is all it has to show.
            Game(Hives, "alien-hives-unique", Play.Shoots, ["Unique"], [R(1, "Unique")], [], []) with
            {
                Evidence = new Dictionary<string, Func<IReadOnlyList<Tabletop.Protocol.GameEvent>, bool>> { ["Unique"] = _ => true },
            },
            // p.10: the fixed hits leave the unit at half or less, so it tests morale; every model carries the Banner, so a casualty cannot take it away.
            Morale(ChangeDaemons, "wormhole-daemons-of-change-banner", "Banner", [R(1, "Banner")], 1, "\"This model and its unit get +1 to morale test rolls.\""),
            Game(ChangeDaemons, "wormhole-daemons-of-change-musician", Play.Moves, ["Musician"], Each(4, "Musician"), [],
                [Allowance("Musician", null, "\"This model and its unit moves +1\" when using move actions.\"", ("advance", 7), ("rush", 13), ("charge", 13))]),
        ];
    }
}
