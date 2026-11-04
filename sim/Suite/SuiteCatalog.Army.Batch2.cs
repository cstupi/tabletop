using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>
    /// Batch 2 (army-book rules spec → the pick): the written-out Buffs, Marks and Debuffs and the before-attacking actives. Model 1 of unit 1
    /// carries the entry; the script takes every use offered (a Buff on its own unit), and the checks read what the grant or the use did.
    /// </summary>
    private static partial class ArmyGames
    {
        static TestGame Pick((string Uid, string Army) book, string name, Play play, string entry, IReadOnlyList<GameCheck> checks, double? length = 26,
            IReadOnlyList<DiceFix>? dice = null, IReadOnlyList<ScenarioRule>? enemy = null, int enemyUnits = 1, int? enemyModels = null, int enemyAttacks = 2,
            IReadOnlyList<ScenarioRule>? enemyWeapon = null, IReadOnlyList<ScenarioRule>? carrier = null, IReadOnlyList<ScenarioRule>? rules = null,
            int gunRange = 60, Action<ScriptBuilder>? more = null, string? enemyBook = null, int? rounds = null, ulong seed = 1, int units = 1, int buffUnit = 1, bool grants = true,
            double? gap = null, IReadOnlyDictionary<int, Tabletop.Geometry.Point2>? anchors = null, IReadOnlyDictionary<int, Tabletop.Geometry.Point2>? enemyAnchors = null)
        {
            var facing = Facing(play, length, gap);
            (anchors, enemyAnchors) = (anchors ?? facing.Item1, enemyAnchors ?? facing.Item2);
            return Game(book, name, play, [entry], rules ?? [], dice ?? [], grants ? [.. checks, Spent(entry, $"\"{entry}\"")] : checks, carriers: [new ScenarioModel(1, 1, [R(1, entry), .. carrier ?? []])],
                units: units, enemyUnits: enemyUnits, enemyModels: enemyModels, enemyAttacks: enemyAttacks, enemy: enemy, enemyWeapon: enemyWeapon, length: length,
                gunRange: gunRange, enemyBook: enemyBook, rounds: rounds, seed: seed, anchors: anchors, enemyAnchors: enemyAnchors,
                more: s => { UsePicks(book.Army, buffUnit, entry)(s); more?.Invoke(s); });
        }

        /// <summary>
        /// Where a Shoots or Shot game's units 1 deploy (anchors): face to face across the table's middle, gap inches apart (default the length
        /// less 20", 6" on the 26" table), so a pick "within 18"" reaches the enemy whatever the seed's placement. Other plays deploy where
        /// the policy puts them.
        /// </summary>
        static (IReadOnlyDictionary<int, Tabletop.Geometry.Point2>?, IReadOnlyDictionary<int, Tabletop.Geometry.Point2>?) Facing(Play play, double? length, double? gap)
        {
            if (play is not (Play.Shoots or Play.Shot)) return (null, null);
            var l = length ?? 36;
            var half = Math.Min(gap ?? l - 20, l - 2) / 2;
            return (new Dictionary<int, Tabletop.Geometry.Point2> { [1] = new(18, l / 2 + half) }, new Dictionary<int, Tabletop.Geometry.Point2> { [1] = new(18, l / 2 - half) });
        }

        /// <summary>The attacks made by (or, with onTarget, at) a unit that held a grant of the entry as the attack began.</summary>
        static Occasion Granted(string entry, bool onTarget = false) => (run, a) =>
            PatternLog.For(run).Attacks.TryGetValue(a.Frame.FrameId, out var s)
            && (onTarget ? s.TargetGrants : s.AttackerGrants ?? []).Any(g => g.EntryName == entry);

        const string BuffText = "\"pick one friendly unit within 12\", which gets ";
        const string MarkText = "\"pick one enemy unit within 18\" in line of sight, which friendly units get";
        const string DebuffText = "\"pick one enemy unit within 18\" in line of sight, which ";

        public static IReadOnlyList<TestGame> Batch2 =>
        [
            Pick(Sisters, "blessed-sisters-precision-shooter-buff", Play.Shoots, "Precision Shooter Buff",
                [Modifier("Precision Shooter Buff", "hit", 1, BuffText + "+1 to hit rolls when shooting once\"")]),
            Pick(PlagueDaemons, "wormhole-daemons-of-plague-precision-attacks-buff", Play.Shoots, "Precision Attacks Buff",
                [Modifier("Precision Attacks Buff", "hit", 1, BuffText + "+1 to hit rolls when attacking once\"")]),
            Pick(Sisters, "blessed-sisters-precision-fighter-buff", Play.Charges, "Precision Fighter Buff",
                [Modifier("Precision Fighter Buff", "hit", 1, BuffText + "+1 to hit rolls in melee once\"", s => s.Nearest(MeleeSequence) is not null)]),
            Pick(Cults, "soul-snatcher-cults-precision-shooting-mark", Play.Shoots, "Precision Shooting Mark",
                [Modifier("Precision Shooting Mark", "hit", 1, MarkText + " +1 to hit rolls when shooting against once\"")]),
            Pick(DarkElves, "dark-elf-raiders-precision-fighting-mark", Play.Charges, "Precision Fighting Mark",
                [Modifier("Precision Fighting Mark", "hit", 1, MarkText + " +1 to hit rolls in melee against once\"", s => s.Nearest(MeleeSequence) is not null)]),
            Pick(Hives, "alien-hives-precision-debuff", Play.Shot, "Precision Debuff",
                [Modifier("Precision Debuff", "hit", -1, DebuffText + "gets -1 to hit rolls when attacking once\"")]),
            // p.10: the fixed hits leave the unit at half or less, so it tests morale after the Buff its first activation gave it.
            Pick(Guerrillas, "rebel-guerrillas-courage-buff", Play.Shot, "Courage Buff",
                [Modifier("Courage Buff", "morale", 1, BuffText + "+1 to morale test rolls once\"")],
                dice: [Fix("hit", 6, 6, 1), Fix("defense", 1, 1)], enemyModels: 1, enemyAttacks: 3, rounds: 2, length: null),
            // One unit of two models: the fixed hits and blocks of the first shooting kill one, leaving it at half (p.10), and it tests morale.
            // The units deploy 15" apart: within the pick's 18", beyond the 12" a charge needs, so unit 1 Advances and shoots.
            Pick(Jackals, "jackals-morale-debuff", Play.Moves, "Morale Debuff",
                [Modifier("Morale Debuff", "morale", -1, DebuffText + "gets -1 to morale test rolls once\"")], enemyModels: 2,
                dice: [Fix("hit", 6, 6, 6), Fix("defense", 1, 6, 6)],
                anchors: new Dictionary<int, Tabletop.Geometry.Point2> { [1] = new(18, 20) }, enemyAnchors: new Dictionary<int, Tabletop.Geometry.Point2> { [1] = new(18, 5) }),
            // The carrier is a caster of its own book: it Buffs itself, then the script casts.
            Pick(Robots, "robot-legions-casting-buff", Play.Shoots, "Casting Buff",
                [Modifier("Casting Buff", "casting", 1, "\"pick one friendly model within 12\" with Caster, which gets +1 to casting rolls once\"")],
                carrier: [R(1, "Caster", 2)], more: Casts),
            Pick(ChangeDaemons, "wormhole-daemons-of-change-casting-debuff", Play.Shot, "Casting Debuff",
                [Modifier("Casting Debuff", "casting", -1, DebuffText.Replace("which ", "with Caster, which ") + "gets -1 to casting rolls once\"")],
                enemy: [R(1, "Caster", 2)], enemyBook: ChangeDaemons.Uid, more: Casts),
            // A 12" gun from 12"-deep zones on a 30" table: a target beyond 12" is in range only under the +6".
            Overreach(Pick(Cults, "soul-snatcher-cults-increased-shooting-range-buff", Play.Shoots, "Increased Shooting Range Buff",
                [RangeBonus("Increased Shooting Range Buff", Granted("Increased Shooting Range Buff"), 12, 6, BuffText + "+6\" range when shooting once\"")],
                gunRange: 12, length: 30, gap: 16, enemyUnits: 2), 2, new(2, 1)),
            Overreach(Pick(Dynasty, "eternal-dynasty-increased-shooting-range-mark", Play.Shoots, "Increased Shooting Range Mark",
                [RangeBonus("Increased Shooting Range Mark", Granted("Increased Shooting Range Mark", onTarget: true), 12, 6, MarkText + " +6\" range when shooting against once\"")],
                gunRange: 12, length: 30, gap: 16, enemyUnits: 2), 2, new(2, 1)),
            Pick(Cults, "soul-snatcher-cults-speed-buff", Play.Moves, "Speed Buff",
                [GrantedAllowance("Speed Buff", BuffText + "moves +2\" when using Advance actions and +4\" when using Rush/Charge actions once\"", ("advance", 8), ("rush", 16), ("charge", 16))],
                units: 2, buffUnit: 2),
            Pick(Dwarves, "dwarf-guilds-speed-debuff", Play.Charged, "Speed Debuff",
                [GrantedAllowance("Speed Debuff", DebuffText + "moves -2\" when using Advance actions and -4\" when using Rush/Charge actions once\"", ("advance", 4), ("rush", 8), ("charge", 8))]),
            Pick(Plague, "plague-disciples-difficult-terrain-debuff", Play.Charged, "Difficult Terrain Debuff",
                [GrantedAllowance("Difficult Terrain Debuff", DebuffText + "counts as being in Difficult Terrain once\": p.11 Difficult, \"may not move more than 6\"\"", ("advance", 6), ("rush", 6), ("charge", 6))]),
            Pick(War, "war-disciples-dangerous-terrain-debuff", Play.Shoots, "Dangerous Terrain Debuff",
                [DangerousOnce("Dangerous Terrain Debuff", DebuffText + "counts as being in Dangerous Terrain once\": p.11 Dangerous, a test \"when activated in it\"")]),
            Pick(MachineCults, "machine-cults-piercing-debuff", Play.Shot, "Piercing Debuff",
                [BlocksUnder("Piercing Debuff", Granted("Piercing Debuff"), 0, DebuffText + "loses AP(+1) when attacking once\" (of AP(1))")], enemyWeapon: [R(0, "AP", 1)]),
            Pick(Dao, "dao-union-piercing-shooting-mark", Play.Shoots, "Piercing Shooting Mark",
                [BlocksUnder("Piercing Shooting Mark", Granted("Piercing Shooting Mark", onTarget: true), 1, MarkText + "s AP(+1) when shooting against once\"")]),
            Pick(Saurians, "saurian-starhost-bane-mark", Play.Shoots, "Bane Mark",
                [Rerolls("Bane Mark", Granted("Bane Mark", onTarget: true), [6], successOnly: false, MarkText + "s Bane when attacking against once\": p.13 Bane, \"re-roll unmodified Defense results of 6\"")]),
            Pick(Custodians, "custodian-brothers-shred-mark", Play.Shoots, "Shred Mark",
                [ExtraWounds("Shred Mark", Granted("Shred Mark", onTarget: true), [1], MarkText + "s Shred when attacking against once\": Shred, \"this weapon deals 1 extra wound\"")]),
            Pick(HighElves, "high-elf-fleets-unwieldy-debuff", Play.Charged, "Unwieldy Debuff",
                [StrikesLast("Unwieldy Debuff", DebuffText + "gets Unwieldy in melee once\": Unwieldy, \"Strikes last when charging\"")]),
            Pick(WarDaemons, "wormhole-daemons-of-war-fatigue-debuff", Play.Shoots, "Fatigue Debuff",
                [TestThen("Fatigue Debuff", DebuffText + "must take a morale test. If failed, it becomes fatigued.\"", condition: "fatigued")], grants: false),
            Pick(LustDaemons, "wormhole-daemons-of-lust-mind-control", Play.Shoots, "Mind Control",
                [TestThen("Mind Control", DebuffText + "must take a morale test. If failed you may move it by up to 6\" in a straight line\"", move: 6)], grants: false),
            // Tough(3) models shot every round; the script heals the most wounded model in reach.
            Pick(Orcs, "orc-marauders-mend", Play.Shot, "Mend",
                [Healed("Mend", "\"pick one friendly model within 3\" with Tough, and remove D3 wounds from it\": p.4, a D3 is \"a D6 and halve the result, rounding up\"")],
                rules: [R(1, "Tough", 3)], carrier: [R(1, "Tough", 3)], enemyAttacks: 4, length: null, more: MostWounded("Mend"), grants: false),
            Pick(Saurians, "saurian-starhost-breath-attack", Play.Charges, "Breath Attack",
                [HitsPerSuccess("Breath Attack", 1, 2, 1, "\"roll one die. On a 2+ one enemy unit within 6\" in line of sight takes 1 hit with Blast(3) and AP(1)\"", ap: 1)], grants: false),
            Pick(ChangeDaemons, "wormhole-daemons-of-change-storm-of-change", Play.Charges, "Storm of Change",
                [HitsPerSuccess("Storm of Change", 3, 2, 3, "\"roll 3 dice. For each 2+ one enemy unit within 12\" takes 3 hits with Shred\"")], grants: false),
            Pick(LustDaemons, "wormhole-daemons-of-lust-storm-of-lust", Play.Charges, "Storm of Lust",
                [HitsPerSuccess("Storm of Lust", 3, 2, 3, "\"roll 3 dice. For each 2+ one enemy unit within 12\" takes 3 hits with Surge (roll one die per hit to see if it triggers)\"")], grants: false),
            Pick(PlagueDaemons, "wormhole-daemons-of-plague-storm-of-plague", Play.Charges, "Storm of Plague",
                [HitsPerSuccess("Storm of Plague", 3, 2, 3, "\"roll 3 dice. For each 2+ one enemy unit within 12\" takes 3 hits with Bane\"")], grants: false),
            Pick(WarDaemons, "wormhole-daemons-of-war-storm-of-war", Play.Charges, "Storm of War",
                [HitsPerSuccess("Storm of War", 3, 2, 3, "\"roll 3 dice. For each 2+ one enemy unit within 12\" takes 3 hits with AP(1)\"", ap: 1)], grants: false),
            Game(BloodPrime, "blood-prime-brothers-primeborn", Play.Shot, ["Primeborn"], [R(1, "Primeborn")], [Fix("hit", 6, 6, 6)],
                [ApGrant("Primeborn", On("Primeborn", when: When.Shooting), 0, "\"those hits count as having AP(-1), to a min. of AP(0)\" (against AP(1))")], enemyWeapon: [R(0, "AP", 1)]),
        ];

        /// <summary>The entry's rule_target Ask answered with its most wounded eligible model (read off the projection the Ask came with).</summary>
        static Action<ScriptBuilder> MostWounded(string entry) => s => s.Match("rule_target", (ask, projection) =>
            ask.Prompt.StartsWith(entry + ":", StringComparison.Ordinal) && ask.Legal is PieceSelection p && p.Eligible.Where(projection.Models.ContainsKey).MaxBy(m => projection.Models[m].Wounds) is { } m
                ? new SelectedPieces([m]) : null);

        /// <summary>Every react Ask that offers a cast is answered with it (after any use, which UsePicks answers first).</summary>
        static void Casts(ScriptBuilder s) => s.Match(AskQuestions.React, (ask, _) => AskPicks.Option(ask, c => c.Id == "cast"));
    }
}
