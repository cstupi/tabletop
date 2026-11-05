using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>
    /// The army:3.6:&lt;uid&gt; games for the 3.6 hand part's entries whose text 3.6 keeps (7.4: unchanged, quote or case only) and for its renamed
    /// entries. A kept rule's 3.5.1 game plays again under 3.6 on the 3.6 book that prints it, with its exact checks as they are; a renamed rule
    /// gets its game under its 3.6 name, quoting the 3.6 text. The rules 3.6 rewrites have their games in SuiteCatalog.Army36&lt;batch&gt;.cs.
    /// </summary>
    private static partial class ArmyGames
    {
        /// <summary>
        /// The 3.5.1 army games whose rules 3.6 keeps word for word (or with quote and case changes only), by name, each with the 3.6 book it
        /// plays under when that is not its 3.5 one (the 3.5 book no longer prints the rule) and then its 3.6 name.
        /// </summary>
        static readonly (string Name, string? Uid, string? Rename)[] Kept36 =
        [
            ("saurian-starhost-primal", null, null), ("eternal-dynasty-clan-warrior", null, null), ("soul-snatcher-cults-quick-shot", null, null),
            ("high-elf-fleets-unwieldy", null, null), ("human-defense-force-rapid-advance", null, null), ("goblin-reclaimers-mischief", null, null),
            ("elven-jesters-unpredictable", null, null), ("dwarf-guilds-shred", null, null), ("plague-disciples-rapid-rush", null, null),
            ("pattern-aura-stealth", null, null), ("pattern-buff-stealth", null, null), ("pattern-mark-quick-shot", null, null),
            ("pattern-debuff-defense", null, null), ("dao-union-good-shot", null, null), ("alien-hives-precise", null, null),
            ("ratmen-clans-predator-shooter", null, null), ("alien-hives-rapid-charge", null, null), ("dwarf-guilds-swift", null, null),
            ("blessed-sisters-fast-aura", null, null), ("orc-marauders-precision-charge-aura", null, null), ("eternal-dynasty-precision-fighter-aura", null, null),
            ("human-inquisition-precision-shooter-aura", null, null), ("alien-hives-rapid-charge-aura", null, null), ("orc-marauders-bad-shot", null, null),
            ("blood-brothers-bloodborn", null, null), ("change-disciples-unpredictable", null, null), ("wormhole-daemons-of-change-banner", null, null),
            ("wormhole-daemons-of-change-musician", null, null), ("blessed-sisters-precision-shooter-buff", null, null),
            ("wormhole-daemons-of-plague-precision-attacks-buff", null, null), ("blessed-sisters-precision-fighter-buff", null, null),
            ("soul-snatcher-cults-precision-shooting-mark", null, null), ("dark-elf-raiders-precision-fighting-mark", null, null),
            ("alien-hives-precision-debuff", null, null), ("robot-legions-casting-buff", null, null), ("soul-snatcher-cults-speed-buff", null, null),
            ("orc-marauders-mend", null, null), ("robot-legions-ambushing-piercing-shot", null, null), ("robot-legions-repel-ambushers", null, null),
            ("saurian-starhost-ambush-beacon", "vux1Y5vvULmaxZ8P", "eternal-dynasty-ambush-beacon"), ("dark-brothers-rapid-ambush", null, null),
            ("battle-brothers-re-deployment", null, null), ("elven-jesters-ambush-re-deployment", null, null), ("human-inquisition-surprise-attack", null, null),
            ("human-defense-force-coordinate", null, null), ("titan-lords-delayed-action", null, null), ("goblin-reclaimers-instinctive", null, null),
            ("infected-colonies-deathstrike", null, null), ("blood-prime-brothers-retaliate", null, null), ("ratmen-clans-self-destruct", null, null),
            ("high-elf-fleets-caster-group", null, null), ("wormhole-daemons-of-change-spell-accumulator", null, null),
            ("blessed-sisters-spell-conduit", null, null), ("robot-legions-reanimation", null, null), ("saurian-starhost-armor", null, null),
            ("saurian-starhost-heavy-impact", null, null), ("dark-elf-raiders-agile", null, null), ("jackals-morale-debuff", null, null),
            ("plague-disciples-difficult-terrain-debuff", "fmuqw5lr5l6dq0mq", "daemons-of-plague-difficult-terrain-debuff"),
            ("wormhole-daemons-of-war-fatigue-debuff", null, null), ("robot-legions-infiltrate", null, null),
            ("infected-colonies-no-retreat", null, null),
        ];

        /// <summary>
        /// The kept games whose check names quote text 3.6 prints with other quote marks, case or page: the 3.6 twin's check names carry the 3.6
        /// text (Shred is a 3.6.0 core rule, p.15).
        /// </summary>
        static readonly IReadOnlyDictionary<string, (string Old, string New)[]> Requote36 = new Dictionary<string, (string Old, string New)[]>(StringComparer.Ordinal)
        {
            ["jackals-morale-debuff"] = [("-1 to morale test rolls", "-1 to Morale Test rolls")],
            ["dark-elf-raiders-agile"] = [("+2” when using", "+2\" when using")],
            ["plague-disciples-difficult-terrain-debuff"] = [("Difficult Terrain once\": p.11 Difficult", "Difficult terrain once\": 3.6.0 p.12 Difficult")],
            ["wormhole-daemons-of-war-fatigue-debuff"] = [("must take a morale test. If failed, it becomes fatigued.", "must take a Morale Test. If failed, it becomes Fatigued.")],
            ["robot-legions-infiltrate"] = [("over 3” away", "over 3\" away")],
            ["pattern-debuff-defense"] = [("-1 to defense rolls once", "-1 to Defense rolls once")],
            ["dwarf-guilds-shred"] = [("Shred: \"On unmodified results of 1 to block hits", "Shred: 3.6.0 p.15 \"For each unmodified result of 1 to block hits")],
        };

        /// <summary>A 3.5.1 game under 3.6 on a 3.6 book: its group and its first army's book are the 3.6 book's; scenario, dice and checks stay.</summary>
        static TestGame Kept((string Name, string? Uid, string? Rename) kept)
        {
            var game = Army351.Single(g => g.Name == kept.Name);
            var uid = kept.Uid ?? BookOf(game.Group);
            if (!Books36.ByUid.ContainsKey(uid)) throw new InvalidOperationException($"'{kept.Name}': no 3.6 book {uid}");
            var name = kept.Rename ?? kept.Name;
            var requote = Requote36.GetValueOrDefault(kept.Name) ?? [];
            return game with
            {
                Name = name, Group = ArmyGroup(RuleSets.Gf360, uid), Edition = RuleSets.Gf360,
                Checks = [.. game.Checks.Select(c => c with { Name = requote.Aggregate(c.Name, (n, r) => n.Replace(r.Old, r.New, StringComparison.Ordinal)) })],
                Scenario = game.Scenario with { Name = name, Armies = [game.Scenario.Armies[0] with { ArmyBook = uid }, .. game.Scenario.Armies.Skip(1)] },
            };
        }

        /// <summary>The 3.5.1 army games, the source of the kept ones.</summary>
        static IEnumerable<TestGame> Army351 => [.. All, .. Patterns, .. Grants, .. HitAndAp, .. Defense, .. Movement, .. GrantAuras, .. NumberAuras, .. Unbatched,
            .. Batch2, .. Batch3, .. Batch4, .. Batch5, .. Batch6, .. Batch7, .. Batch8, .. Batch9];

        public static IReadOnlyList<TestGame> Army36 =>
        [
            .. Kept36.Select(Kept),
            // Kept rules whose 3.5.1 game also played a rule 3.6 rewrites (Evasive, Shatter): on their own.
            Game(Books36.SaurianStarhosts, "saurian-starhosts-teleport", Play.ShotWhileMoving, ["Teleport"],
                [R(1, "Teleport"), R(2, "Teleport"), R(3, "Teleport")], [], [Teleport()]).In36(),
            Game(Books36.Jackals, "jackals-scrapper", Play.Shoots, ["Scrapper"], [R(1, "Scrapper"), R(2, "Scrapper")], [],
                [Rerolls("Scrapper", By("Scrapper"), [6], successOnly: false, SixesRerolled)]).In36(),
            // Renamed in 3.6 (7.4), each on a 3.6 book that prints the new name.
            Game(Books36.RatmenClans, "ratmen-clans-anti-cover", Play.Shoots, ["Anti-Cover"], [], [],
                [NoCover("Anti-Cover", With("Anti-Cover"), "\"This weapon ignores Cover.\"")], weapon: [R(0, "Anti-Cover")], units: 2, board: EnemyRuins).In36(),
            Game(Books36.DwarfGuilds, "dwarf-guilds-anti-cover-when-shooting", Play.Shoots, ["Anti-Cover when Shooting"],
                [R(1, "Anti-Cover when Shooting"), R(2, "Anti-Cover when Shooting")], [],
                [NoCover("Anti-Cover when Shooting", By("Anti-Cover when Shooting", when: When.Shooting), "\"This model gets Anti-Cover on its weapons when shooting.\"")], board: EnemyRuins).In36(),
            Shooting(Books36.EternalDynasty, "eternal-dynasty-anti-cover-aura", "Anti-Cover Aura",
                NoCover("Anti-Cover Aura", Carrying("Anti-Cover Aura", When.Shooting), "\"This model and its unit get Anti-Cover on its weapons when shooting.\""), board: EnemyRuins).In36(),
            Shooting(Books36.DwarfGuilds, "dwarf-guilds-anti-cover-when-shooting-aura", "Anti-Cover when Shooting Aura",
                NoCover("Anti-Cover when Shooting Aura", Carrying("Anti-Cover when Shooting Aura", When.Shooting), "\"This model and its unit get Anti-Cover on its weapons when shooting.\""), board: EnemyRuins).In36(),
            // The opponents carry Regeneration, which Anti-Regen leaves unrolled.
            Game(Books36.DaemonsOfChange, "daemons-of-change-anti-regen", Play.Shoots, ["Anti-Regen"], [], [Fix("defense", 1, 1, 1)],
                [NoRoll("Anti-Regen", With("Anti-Regen"), "Regeneration", "\"This weapon ignores Regeneration.\"")],
                weapon: [R(0, "Anti-Regen")], units: 2, enemy: [R(1, "Tough", 3), R(2, "Tough", 3), R(1, "Regeneration"), R(2, "Regeneration")]).In36(),
            Morale(Books36.BattleBrothers, "battle-brothers-bravery-aura", "Bravery Aura", [R(1, "Bravery Aura")], 1, "\"This model and its unit get +1 to Morale Test rolls.\"").In36(),
            // p.10: the fixed hits leave the unit at half or less, so it tests morale after the Buff its first activation gave it.
            Pick(Books36.RebelGuerrillas, "rebel-guerrillas-bravery-buff", Play.Shot, "Bravery Buff",
                [Modifier("Bravery Buff", "morale", 1, BuffText + "+1 to Morale Test rolls once\"")],
                dice: [Fix("hit", 6, 6, 1), Fix("defense", 1, 1)], enemyModels: 1, enemyAttacks: 3, rounds: 2, length: null).In36(),
            ExtendedBuffRange(Books36.RebelGuerrillas, "rebel-guerrillas-extended-buff-range", "Bravery Buff").In36(),
            Through(Game(Books36.MachineCults, "machine-cults-crossing-strike", Play.ShotWhileMoving, ["Crossing Strike"], [R(1, "Flying"), R(1, "Tough", 6), R(2, "Flying"), R(2, "Tough", 6)], [],
                [Crossing("Crossing Strike", "\"when this model moves through enemy units, pick one of them and attack it with this weapon as if it was shooting\"")],
                weapon: [R(0, "Crossing Strike")], length: 30, more: s => RushesThrough(s, "Machine Cults", 2))),
            Through(Game(Books36.Jackals, "jackals-crossing-attack", Play.ShotWhileMoving, ["Crossing Attack"],
                [R(1, "Crossing Attack", 3), R(1, "Flying"), R(1, "Tough", 6), R(2, "Crossing Attack", 3), R(2, "Flying"), R(2, "Tough", 6)], [],
                [Crossing("Crossing Attack", "\"when this model moves through enemy units, pick one of them\""),
                 WoundDice("Crossing Attack", 3, "\"roll X dice. For each 6+ the target takes one wound\" (X 3)")],
                length: 30, more: s => RushesThrough(s, "Jackals", 2))),
            Game(Books36.RatmenClans, "ratmen-clans-piercing-charge", Play.Charges, ["Piercing Charge"],
                [R(1, "Piercing Charge"), R(2, "Piercing Charge"), R(3, "Piercing Charge")], [Fix("hit", 6)],
                [ApGrant("Piercing Charge", By("Piercing Charge", when: When.Charging), 1, "\"This model gets AP(+1) on its weapons when charging\"")]).In36(),
            // Only the opponents move, so every melee is a charge at it, and every counter Ask is answered strike.
            Game(Books36.VoidspaceStalkers, "voidspace-stalkers-counter-in-melee", Play.Charged, ["Counter in Melee"],
                [R(1, "Counter in Melee"), R(2, "Counter in Melee"), R(3, "Counter in Melee")], [], [], enemyUnits: 4,
                more: s => s.Match("counter", (ask, _) => ask.Legal is Options o && o.Choices.Any(c => c.Id == "strike") ? new Chosen("strike") : null)).In36(),
            // 3.6.0 p.12 makes the dangerous terrain test the unit's: the 3.5.1 game's per-model dice no longer hold.
            Pick(Books36.WarDisciples, "war-disciples-dangerous-terrain-debuff", Play.Shoots, "Dangerous Terrain Debuff",
                [DangerousOnce("Dangerous Terrain Debuff", DebuffText + "counts as being in Dangerous terrain once\": 3.6.0 p.12 Dangerous, \"the entire unit must take a Dangerous Terrain Test\"",
                    "dangerous-terrain-36")]).In36(),
            // "May only be taken once per army" is a list-building rule, and "counts as having Hero" is a grant the engine reads off the model
            // (GfNames.IsHero), which no step lists: a game with a Unique Hero model that plays to its end is what it has to show.
            (Game(Books36.RatmenClans, "ratmen-clans-unique-hero", Play.Shoots, ["Unique Hero"], [R(1, "Unique Hero")], [], []) with
            {
                Evidence = new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>> { ["Unique Hero"] = _ => true },
            }).In36(),
        ];

        /// <summary>
        /// A crossing game by construction: the rule's units (Tough(6), so the holding opponents' shots leave them standing) Rush every activation
        /// across a 30" table (Flying, p.13: "may move through units"), each model making for a point 4" beyond the nearest enemy model, so its path
        /// crosses that model's unit; every crossing_target Ask picks the first enemy offered.
        /// </summary>
        static TestGame Through(TestGame game) => Through351(game).In36();

        /// <summary>Through under the game's own edition (the 3.5.1 crossing games).</summary>
        static TestGame Through351(TestGame game) => game with { Scenario = game.Scenario with { Goal = Beyond } };

        static void RushesThrough(ScriptBuilder s, string army, int units) => s
            .Match(AskQuestions.Activate, (ask, _) => Enumerable.Range(1, units).Select(k => AskPicks.Activate(ask, $"{army} unit {k}", "Rush")).FirstOrDefault(a => a is not null))
            .Match("crossing_target", (ask, _) => ask.Legal is PieceSelection p && p.Eligible.Count > 0 ? new SelectedPieces([p.Eligible[0]]) : null);

        static Tabletop.Geometry.Point2? Beyond(Guid modelId, PolicyView view)
        {
            if (view.PlacedOf(modelId) is not { } placed || Closing(modelId, view) is not { } enemy) return null;
            var at = placed.Placement.Position.Horizontal;
            var way = enemy - at;
            return way.Length < 0.001 ? enemy : enemy + way * (4 / way.Length);
        }
    }
}
