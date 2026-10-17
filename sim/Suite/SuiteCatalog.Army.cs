using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The army-book rules of grimdark-future-army-rules.part.json, grouped army:&lt;book uid&gt; by a book that prints them (a rule several books
/// print is played once, under one of them). A rule and its Boost are separate games where the dice decide the occasion. Every game is
/// built so its occasion does not depend on the seed: the roll-off is fixed so the first army (the rule's) deploys and acts first, the
/// script shoots and strikes back whenever asked, the core rule Immobile (p.14, "may only use Hold actions") keeps a side holding and
/// shooting, and the first roll of the kind a check reads is fixed to faces that hold the rule's number (DiceFix, ordinal 0).
/// </summary>
public static partial class SuiteCatalog
{
    private static partial IEnumerable<TestGame> Army() => [.. ArmyGames.All, .. ArmyGames.Patterns, .. ArmyGames.Grants, .. ArmyGames.HitAndAp, .. ArmyGames.Defense, .. ArmyGames.Movement, .. ArmyGames.GrantAuras, .. ArmyGames.NumberAuras, .. ArmyGames.Unbatched, .. ArmyGames.Batch2, .. ArmyGames.Batch3, .. ArmyGames.Batch4, .. ArmyGames.Batch5, .. ArmyGames.Batch6, .. ArmyGames.Batch7, .. ArmyGames.Batch8, .. ArmyGames.Batch9];

    private static partial class ArmyGames
    {
        static readonly (string Uid, string Army)
            Saurians = ("U6hwlur14RpnInZr", "Saurian Starhost"), Dynasty = ("vux1Y5vvULmaxZ8P", "Eternal Dynasty"),
            Sisters = ("7oi8zeiqfamiur21", "Blessed Sisters"), Orcs = ("1wj1ysgxpuuz9bc7", "Orc Marauders"),
            Cults = ("zz3kp5ry7ks6mxcx", "Soul-Snatcher Cults"), Custodians = ("ewbra8nv3nq3k27p", "Custodian Brothers"),
            DarkElves = ("q11la9v8h1heu9ja", "Dark Elf Raiders"), Havoc = ("7o6om21wxlvvy3hq", "Havoc Brothers"),
            Ratmen = ("hk70l4d471plza00", "Ratmen Clans"), HighElves = ("7i7blhft75q9zfdc", "High Elf Fleets"),
            Hives = ("w7qor7b2kuifcyvk", "Alien Hives"), Hdf = ("z65fgu0l29i4lnlu", "Human Defense Force"),
            Infected = ("dnthspt7c0klhmt8", "Infected Colonies"), MachineCults = ("7el7k3hgy5pb9o9i", "Machine Cults"),
            Goblins = ("k5T0f2XIfpmPO28h", "Goblin Reclaimers"), Jesters = ("nyh41t82jugcdq8m", "Elven Jesters"),
            Jackals = ("BKi_hJaJflN8ZorH", "Jackals"), Robots = ("4k5amkxoybdiqotm", "Robot Legions"),
            Dwarves = ("fk1mkbp8apvltu0z", "Dwarf Guilds"), Dao = ("z8205ez2boggzs22", "DAO Union"),
            Change = ("r6hr29338u4micfw", "Change Disciples"), Lust = ("drqw1iswxmuugp3d", "Lust Disciples"),
            Plague = ("jlray7cwf8mvw5sn", "Plague Disciples"), War = ("31xjrm9ivdimkjxp", "War Disciples");

        const string Sixes = "\"For each unmodified roll of 6 to hit ... may roll +1 attack with that weapon\"";
        const string FivesToo = "\"extra attacks on successful unmodified hit results of 5-6\"";
        const string ExtraHit = "\"unmodified results of 6 to hit deal 1 extra hit\"", ExtraHit56 = "\"extra hits on successful unmodified hit results of 5-6\"";
        const string ExtraWound = "\"take 1 extra wound for each unmodified result of 1 that they roll\"", ExtraWound12 = "\"take extra wounds on failed defense rolls of 1-2\"";
        const string IgnoreOn6 = "\"roll one die for each. On a 6+ it is ignored\"", IgnoreOn5 = "\"they ignore wounds on rolls of 5-6\"";
        const string SixesRerolled = "\"targets must re-roll unmodified Defense results of 6\"", FivesRerolled = "\"must re-roll successful unmodified defense results of 5-6\"";
        const string HitAndMorale = "\"+1 to morale test rolls triggered by shooting/melee\"";
        const string Plus2Plus2 = "\"Moves +2\" when using Advance, and +2\" when using Rush/Charge\"", Plus4Plus4 = "\"it moves +4\" on Advance and +4\" on Rush/Charge\"";

        public static IReadOnlyList<TestGame> All { get; } =
        [
            Game(Saurians, "saurian-starhost-primal", Play.Shoots, ["Primal"], [R(1, "Primal"), R(2, "Primal")], [],
                [ExtraAttacks("Primal", By("Primal", "Primal Boost"), [6], Sixes)]),
            Game(Saurians, "saurian-starhost-primal-boost", Play.Shoots, ["Primal Boost"], [R(1, "Primal"), R(1, "Primal Boost"), R(2, "Primal"), R(2, "Primal Boost")], [],
                [ExtraAttacks("Primal Boost", By("Primal Boost"), [5, 6], FivesToo)]),
            Game(Saurians, "saurian-starhost-evasive-teleport", Play.ShotWhileMoving, ["Evasive", "Teleport"],
                [R(1, "Evasive"), R(1, "Teleport"), R(2, "Evasive"), R(2, "Teleport"), R(3, "Evasive"), R(3, "Teleport")], [],
                [Modifier("Evasive", "hit", -1, "\"Enemies get -1 to hit rolls\""), Teleport()]),
            Game(Dynasty, "eternal-dynasty-clan-warrior", Play.Shoots, ["Clan Warrior"], [R(1, "Clan Warrior"), R(2, "Clan Warrior")], [],
                [ExtraAttacks("Clan Warrior", By("Clan Warrior", "Clan Warrior Boost"), [6], Sixes)]),
            Game(Dynasty, "eternal-dynasty-clan-warrior-boost", Play.Shoots, ["Clan Warrior Boost"], [R(1, "Clan Warrior"), R(1, "Clan Warrior Boost"), R(2, "Clan Warrior"), R(2, "Clan Warrior Boost")],
                [], [ExtraAttacks("Clan Warrior Boost", By("Clan Warrior Boost"), [5, 6], FivesToo)]),
            // Counter-Attack states no number: its fired check is the whole check. Only the opponents move, so every melee is a charge at it.
            Game(Dynasty, "eternal-dynasty-counter-attack", Play.Charged, ["Counter-Attack"], [R(1, "Counter-Attack"), R(2, "Counter-Attack"), R(3, "Counter-Attack")], [], [],
                enemyUnits: 4),
            Game(Sisters, "blessed-sisters-devout", Play.Shoots, ["Devout", "Disintegrate"], [R(1, "Devout"), R(2, "Devout")], [],
                [ExtraHits("Devout", By("Devout", "Devout Boost"), [6], ExtraHit),
                 ApGrant("Disintegrate", With("Disintegrate"), 2, "\"against units where most models have Defense 2+ to Defense 3+, this weapon gets AP(+2)\"")],
                weapon: [R(0, "Disintegrate")], enemyStats: Stats(4, 3)),
            Game(Sisters, "blessed-sisters-devout-boost", Play.Shoots, ["Devout Boost"], [R(1, "Devout"), R(1, "Devout Boost"), R(2, "Devout"), R(2, "Devout Boost")], [],
                [ExtraHits("Devout Boost", By("Devout Boost", when: When.Shooting), [5, 6], ExtraHit56)]),
            // Three 6s fixed onto the first hit roll before score-hits reads it (opr-sequences.md → Dice edits) deal the extra hits of the fixed faces.
            Game(Sisters, "blessed-sisters-devout-edited-dice", Play.Shoots, [], [R(1, "Devout"), R(2, "Devout")], [Fix("hit", 6, 6, 6)], [EditedExtraHits("Devout", ExtraHit)]),
            Game(Orcs, "orc-marauders-ferocious", Play.Shoots, ["Ferocious"], [R(1, "Ferocious"), R(2, "Ferocious")], [],
                [ExtraHits("Ferocious", By("Ferocious", "Ferocious Boost"), [6], ExtraHit)]),
            Game(Orcs, "orc-marauders-ferocious-boost", Play.Shoots, ["Ferocious Boost"], [R(1, "Ferocious"), R(1, "Ferocious Boost"), R(2, "Ferocious"), R(2, "Ferocious Boost")], [],
                [ExtraHits("Ferocious Boost", By("Ferocious Boost", when: When.Shooting), [5, 6], ExtraHit56)]),
            Game(Orcs, "orc-marauders-hit-and-run", Play.Charges, ["Hit & Run Fighter", "Piercing Assault"],
                [R(1, "Hit & Run Fighter"), R(1, "Piercing Assault"), R(2, "Hit & Run Fighter"), R(2, "Piercing Assault"), R(3, "Hit & Run Fighter"), R(3, "Piercing Assault")],
                [Fix("hit", 6)],
                [Reposition("Hit & Run Fighter", 3, "\"may move by up to 3\" after being in melee\""), ApGrant("Piercing Assault", By("Piercing Assault", when: When.Charging), 1, "\"This model gets AP(+1) when charging\"")]),
            Game(Cults, "soul-snatcher-cults-grounded", Play.Shot, ["Grounded Reinforcement"], [R(1, "Grounded Reinforcement")], [Fix("hit", 6, 6, 6)],
                [Modifier("Grounded Reinforcement", "defense", 1, "\"they get +1 to defense rolls\"")], board: Ruins),
            // Quick Shot states no number: its fired check is the whole check.
            Game(Cults, "soul-snatcher-cults-quick-shot", Play.Moves, ["Quick Shot"], [R(1, "Quick Shot"), R(2, "Quick Shot"), R(3, "Quick Shot"), R(4, "Quick Shot")], [], []),
            Game(Cults, "soul-snatcher-cults-slayers", Play.Shoots, ["Slayer"], [R(1, "Slayer"), R(2, "Slayer")], [],
                [ApGrant("Slayer", By("Slayer", when: When.Shooting), 2, "\"its weapons get AP(+2) if most models in the target have Tough(3) or higher\"")], enemy: [R(1, "Tough", 3), R(2, "Tough", 3)]),
            // GfMelee.ChargeTargets settles a defender-less melee interaction, and Slayer's charge row reaches its most_models opponent condition
            // there when round 1 offers its first activation to a unit that may charge (a holding unit, above, is never asked): the condition is false.
            Game(Cults, "soul-snatcher-cults-slayers-charging", Play.Charges, [], [R(1, "Slayer")], [], [], enemy: [R(1, "Tough", 3), R(2, "Tough", 3)]),
            Game(Custodians, "custodian-brothers-tear", Play.Shoots, ["Tear"], [], [],
                [ApGrant("Tear", With("Tear"), 4, "\"Against units where most models have Tough(9) or higher, this weapon gets AP(+4)\"")],
                weapon: [R(0, "Tear")], enemy: [R(1, "Tough", 9), R(2, "Tough", 9)], enemyModels: 1, units: 2),
            Game(Custodians, "custodian-brothers-guardian", Play.Shot, ["Guardian"], [R(1, "Guardian")], [Fix("hit", 6, 6, 6)],
                [ApGrant("Guardian", On("Guardian", "Guardian Boost", When.Shooting), 0, "\"those hits count as having AP(-1), to a min. of AP(0)\" (against AP(1))")], enemyWeapon: [R(0, "AP", 1)]),
            Game(Custodians, "custodian-brothers-guardian-boost", Play.Shot, ["Guardian Boost"], [R(1, "Guardian"), R(1, "Guardian Boost")], [Fix("hit", 6, 6, 6)],
                [ApGrant("Guardian Boost", On("Guardian Boost"), 0, "\"enemy hits always count as having AP(-1)\" (against AP(1))")], enemyWeapon: [R(0, "AP", 1)]),
            Game(DarkElves, "dark-elf-raiders-harassing", Play.Shoots, ["Harassing", "Harassing Boost"], [R(1, "Harassing"), R(2, "Harassing"), R(2, "Harassing Boost")], [],
                [Reposition("Harassing", 3, "\"may move by up to 3\" after shooting or being in melee\""), Reposition("Harassing Boost", 6, "\"they may move by up to 6\" from Harassing\"")]),
            Game(DarkElves, "dark-elf-raiders-lacerate", Play.Shoots, ["Lacerate", "Piercing Hunter"], [R(1, "Piercing Hunter"), R(2, "Piercing Hunter")], [],
                [Rerolls("Lacerate", With("Lacerate"), [6], successOnly: false, "\"the target must re-roll unmodified Defense results of 6\""),
                 ApGrant("Piercing Hunter", By("Piercing Hunter", when: When.Shooting), 1, "\"When this model shoots at enemies over 9\" away, its weapons get AP(+1)\"")], weapon: [R(0, "Lacerate")]),
            Game(DarkElves, "dark-elf-raiders-melee-evasion", Play.Charges, ["Melee Evasion"], [R(1, "Melee Evasion"), R(2, "Melee Evasion"), R(3, "Melee Evasion")],
                [Fix("hit", 1)], [Modifier("Melee Evasion", "hit", -1, "\"Enemies get -1 to hit rolls in melee\"", s => s.Nearest(MeleeSequence) is not null)]),
            Game(Havoc, "havoc-brothers-havocbound", Play.Shoots, ["Havocbound", "Havocbound Boost"], [R(1, "Havocbound"), R(2, "Havocbound"), R(2, "Havocbound Boost")],
                [], [ApGrant("Havocbound", By("Havocbound", "Havocbound Boost", When.Shooting), 1, "\"its weapons get AP(+1)\""), ApGrant("Havocbound Boost", By("Havocbound Boost"), 1, "\"it always gets AP(+1) from Havocbound\"")]),
            Carried(Havoc, "havoc-brothers-resistance", "Resistance", [R(1, "Resistance")], IgnoreWounds("Resistance", 6, IgnoreOn6)),
            Game(Ratmen, "ratmen-clans-scurry", Play.Moves, ["Scurry", "Scurry Boost"], [R(1, "Scurry"), R(2, "Scurry"), R(2, "Scurry Boost"), R(3, "Scurry"), R(4, "Scurry"), R(4, "Scurry Boost")], [],
                [Allowance("Scurry", "Scurry Boost", Plus2Plus2, ("advance", 8), ("rush", 14), ("charge", 14)),
                 Allowance("Scurry Boost", null, Plus4Plus4, ("advance", 10), ("rush", 16), ("charge", 16))]),
            Game(Ratmen, "ratmen-clans-hazardous", Play.Shoots, ["Hazardous"], [], [],
                [ApGrant("Hazardous", With("Hazardous"), 4, "\"Gets AP(4)\""), SelfWounds("Hazardous", With("Hazardous"), "\"this model's unit takes one wound on unmodified rolls of 1 to hit\"")], weapon: [R(0, "Hazardous")], units: 2),
            Game(HighElves, "high-elf-fleets-highborn", Play.Moves, ["Highborn", "Highborn Boost"], [R(1, "Highborn"), R(2, "Highborn"), R(2, "Highborn Boost"), R(3, "Highborn"), R(4, "Highborn"), R(4, "Highborn Boost")], [],
                [Allowance("Highborn", "Highborn Boost", Plus2Plus2, ("advance", 8), ("rush", 14), ("charge", 14)),
                 Allowance("Highborn Boost", null, Plus4Plus4, ("advance", 10), ("rush", 16), ("charge", 16))]),
            // Unwieldy states no number: its fired check is the whole check.
            Game(HighElves, "high-elf-fleets-unwieldy", Play.Charges, ["Unwieldy"], [], [], [], weapon: [R(0, "Unwieldy")], units: 6),
            Morale(Hives, "alien-hives-hive-bond", "Hive Bond", [R(1, "Hive Bond")], 1, HitAndMorale),
            Morale(Hives, "alien-hives-hive-bond-boost", "Hive Bond Boost", [R(1, "Hive Bond"), R(1, "Hive Bond Boost")], 2, "\"they get +2 to morale test rolls from Hive Bond\""),
            Morale(Hdf, "human-defense-force-hold-the-line", "Hold the Line", [R(1, "Hold the Line")], 1, HitAndMorale),
            Morale(Hdf, "human-defense-force-hold-the-line-boost", "Hold the Line Boost", [R(1, "Hold the Line"), R(1, "Hold the Line Boost")], 2, "\"they get +2 to morale test rolls from Hold the Line\""),
            Game(Hdf, "human-defense-force-crack", Play.Shoots, ["Crack"], [], [],
                [PerDieAp("Crack", With("Crack"), 2, "\"On unmodified results of 6 to hit, those hits get AP(+2)\"")], weapon: [R(0, "Crack")], units: 2),
            Game(Hdf, "human-defense-force-rapid-advance", Play.Moves, ["Rapid Advance"], [R(1, "Rapid Advance"), R(2, "Rapid Advance"), R(3, "Rapid Advance"), R(4, "Rapid Advance"), R(5, "Rapid Advance"), R(6, "Rapid Advance")], [],
                [Allowance("Rapid Advance", null, "\"This model moves +4\" when using Advance actions\"", ("advance", 10))]),
            Game(Infected, "infected-colonies-infected", Play.Shoots, ["Infected"], [R(1, "Infected"), R(2, "Infected")], [],
                [ExtraWounds("Infected", By("Infected", "Infected Boost"), [1], ExtraWound)]),
            Game(Infected, "infected-colonies-infected-boost", Play.Shoots, ["Infected Boost"], [R(1, "Infected"), R(1, "Infected Boost"), R(2, "Infected"), R(2, "Infected Boost")], [],
                [ExtraWounds("Infected Boost", By("Infected Boost", when: When.Shooting), [1, 2], ExtraWound12)]),
            Game(MachineCults, "machine-cults-fog", Play.Shot, ["Machine-Fog", "Machine-Fog Boost"], [R(1, "Machine-Fog"), R(2, "Machine-Fog"), R(2, "Machine-Fog Boost")], [],
                [Modifier("Machine-Fog", "hit", -1, "\"enemy units get -1 to hit rolls\""), Modifier("Machine-Fog Boost", "hit", -1, "\"enemies attacking them always get -1 to hit rolls\"")]),
            Game(MachineCults, "machine-cults-hit-and-run", Play.Shoots, ["Hit & Run Shooter"], [R(1, "Hit & Run Shooter"), R(2, "Hit & Run Shooter")], [],
                [Reposition("Hit & Run Shooter", 3, "\"may move by up to 3\" after shooting\"")]),
            Game(Goblins, "goblin-reclaimers-mischief", Play.Shoots, ["Mischievous", "Unpredictable Shooter"], [R(1, "Mischievous"), R(1, "Unpredictable Shooter"), R(2, "Mischievous"), R(2, "Unpredictable Shooter")],
                [],
                [Rerolls("Mischievous", By("Mischievous", "Mischievous Boost"), [6], successOnly: false, SixesRerolled), Unpredictable("Unpredictable Shooter")]),
            Game(Goblins, "goblin-reclaimers-mischief-boost", Play.Shoots, ["Mischievous Boost"], [R(1, "Mischievous"), R(1, "Mischievous Boost"), R(2, "Mischievous"), R(2, "Mischievous Boost")],
                [], [Rerolls("Mischievous Boost", By("Mischievous Boost", when: When.Shooting), [5, 6], successOnly: true, FivesRerolled)]),
            Game(Jesters, "elven-jesters-blink", Play.Shoots, ["Rapid Blink", "Rapid Blink Boost"], [R(1, "Rapid Blink"), R(2, "Rapid Blink"), R(2, "Rapid Blink Boost")],
                [], [RapidBlink("Rapid Blink", 1), RapidBlink("Rapid Blink Boost", 2)]),
            Game(Jesters, "elven-jesters-unpredictable", Play.Charges, ["Unpredictable Fighter"],
                [R(1, "Unpredictable Fighter"), R(2, "Unpredictable Fighter"), R(3, "Unpredictable Fighter")], [], [Unpredictable("Unpredictable Fighter")]),
            Game(Jackals, "jackals-scrapper", Play.Shoots, ["Scrapper", "Shatter"], [R(1, "Scrapper"), R(2, "Scrapper")], [],
                [Rerolls("Scrapper", By("Scrapper", "Scrapper Boost"), [6], successOnly: false, SixesRerolled),
                 ApGrant("Shatter", With("Shatter"), 2, "\"Against units where most models have Tough(3) or higher, this weapon gets AP(+2)\"")],
                weapon: [R(0, "Shatter")], enemy: [R(1, "Tough", 3), R(2, "Tough", 3)]),
            // The rule re-rolls only "successful unmodified defense results of 5-6": Shatter's AP(+2) acts against the Tough unit only, so an
            // unmodified 5 fails to block there and is not re-rolled, and blocks against the other unit and is.
            Game(Jackals, "jackals-scrapper-boost", Play.Shoots, ["Scrapper Boost"], [R(1, "Scrapper"), R(1, "Scrapper Boost"), R(2, "Scrapper"), R(2, "Scrapper Boost")], [],
                [Rerolls("Scrapper Boost", By("Scrapper Boost", when: When.Shooting), [5, 6], successOnly: true, FivesRerolled)], weapon: [R(0, "Shatter")], enemy: [R(1, "Tough", 3)], seed: 3),
            Carried(Robots, "robot-legions-self-repair", "Self-Repair", [R(1, "Self-Repair")], IgnoreWounds("Self-Repair", 6, IgnoreOn6)),
            Carried(Robots, "robot-legions-self-repair-boost", "Self-Repair Boost", [R(1, "Self-Repair"), R(1, "Self-Repair Boost")], IgnoreWounds("Self-Repair Boost", 5, IgnoreOn5)),
            Game(Robots, "robot-legions-destructive", Play.Shoots, ["Destructive"], [], [],
                [PerDieAp("Destructive", With("Destructive"), 4, "\"On unmodified results of 6 to hit, those hits get AP(+4)\"")], weapon: [R(0, "Destructive")], units: 2),
            Game(Dwarves, "dwarf-guilds-sturdy", Play.Shot, ["Sturdy", "Sturdy Boost"], [R(1, "Sturdy"), R(2, "Sturdy"), R(2, "Sturdy Boost")], [Fix("hit", 6, 6, 6)],
                [Modifier("Sturdy", "defense", 1, "\"they get +1 to defense rolls\""), Modifier("Sturdy Boost", "defense", 1, "\"they always get +1 to defense rolls from Sturdy\"")]),
            Game(Dwarves, "dwarf-guilds-shred", Play.Shoots, ["Shred"], [], [],
                [ExtraWounds("Shred", With("Shred"), [1], "\"On unmodified results of 1 to block hits, this weapon deals 1 extra wound\"")], weapon: [R(0, "Shred")], units: 2),
            Game(Dao, "dao-union-visors", Play.Shoots, ["Targeting Visor", "Targeting Visor Boost"], [R(1, "Targeting Visor"), R(2, "Targeting Visor"), R(2, "Targeting Visor Boost")], [],
                [Modifier("Targeting Visor", "hit", 1, "\"it gets +1 to hit rolls\""), Modifier("Targeting Visor Boost", "hit", 1, "\"it always gets +1 to hit rolls when shooting\"")]),
            Game(Dao, "dao-union-shielded", Play.Shot, ["Shielded"], [R(1, "Shielded")], [Fix("hit", 6, 6, 6)],
                [Modifier("Shielded", "defense", 1, "\"+1 to defense rolls against hits that are not from spells\"")]),
            Game(Change, "change-disciples-changebound", Play.Shot, ["Changebound", "Changebound Boost"], [R(1, "Changebound"), R(2, "Changebound"), R(2, "Changebound Boost")], [],
                [Modifier("Changebound", "hit", -1, "\"enemy units get -1 to hit rolls\""), Modifier("Changebound Boost", "hit", -1, "\"enemies attacking them always get -1 to hit rolls\"")]),
            Game(Change, "change-disciples-bane", Play.Shoots, ["Bane when Shooting"], [R(1, "Bane when Shooting"), R(2, "Bane when Shooting")], [],
                [Rerolls("Bane when Shooting", By("Bane when Shooting", when: When.Shooting), [6], successOnly: false, "\"This model gets Bane when shooting\": p.13 Bane, \"re-roll unmodified Defense results of 6\"")]),
            Game(Lust, "lust-disciples-lustbound", Play.Charges, ["Lustbound"], [R(1, "Lustbound"), R(2, "Lustbound"), R(3, "Lustbound"), R(4, "Lustbound")], [],
                [Allowance("Lustbound", "Lustbound Boost", "\"moves +2\" when using Charge actions\"", ("charge", 14))]),
            Game(Lust, "lust-disciples-lustbound-boost", Play.Charges, ["Lustbound Boost"],
                [R(1, "Lustbound"), R(1, "Lustbound Boost"), R(2, "Lustbound"), R(2, "Lustbound Boost"), R(3, "Lustbound"), R(3, "Lustbound Boost"), R(4, "Lustbound"), R(4, "Lustbound Boost")], [],
                [Allowance("Lustbound Boost", null, "\"moves +4\" when using Charge actions\"", ("charge", 16))]),
            Carried(Plague, "plague-disciples-plaguebound", "Plaguebound", [R(1, "Plaguebound")], IgnoreWounds("Plaguebound", 6, IgnoreOn6)),
            Carried(Plague, "plague-disciples-plaguebound-boost", "Plaguebound Boost", [R(1, "Plaguebound"), R(1, "Plaguebound Boost")], IgnoreWounds("Plaguebound Boost", 5, IgnoreOn5)),
            Game(Plague, "plague-disciples-rapid-rush", Play.Moves, ["Rapid Rush"], [R(1, "Rapid Rush"), R(2, "Rapid Rush"), R(3, "Rapid Rush"), R(4, "Rapid Rush"), R(5, "Rapid Rush"), R(6, "Rapid Rush")], [],
                [Allowance("Rapid Rush", null, "\"This model moves +6\" when using Rush actions\"", ("rush", 18))]),
            Game(War, "war-disciples-warbound", Play.Shoots, ["Warbound"], [R(1, "Warbound"), R(2, "Warbound")], [],
                [ExtraWounds("Warbound", By("Warbound", "Warbound Boost"), [1], ExtraWound)]),
            Game(War, "war-disciples-warbound-boost", Play.Shoots, ["Warbound Boost"], [R(1, "Warbound"), R(1, "Warbound Boost"), R(2, "Warbound"), R(2, "Warbound Boost")], [],
                [ExtraWounds("Warbound Boost", By("Warbound Boost", when: When.Shooting), [1, 2], ExtraWound12)]),
        ];

        /// <summary>
        /// Who acts. Shoots: the rule's side holds and shoots every activation, the opponent holds with blades only, so every hit roll is the
        /// rule's side's and every defense roll the target's. Shot: the reverse. ShotWhileMoving: Shot with the rule's side free to move.
        /// Charges: only the rule's side moves, both have blades, so every melee is its charge. Charged: the reverse. Moves: the rule's side
        /// moves and shoots freely at a holding opponent with blades only. Exchange: both sides hold and shoot.
        /// In Shoots and Shot both sides hold in their 12"-deep zones on a 36"
        /// table, so every shot crosses at least 12": a rule that acts "when it shoots ... enemies over 9" away" (the Boosts, Slayer, Piercing
        /// Hunter, Havocbound, Guardian) takes every shooting attack of its holder as its occasion (By/On with When.Shooting).
        /// </summary>
        enum Play { Shoots, Shot, ShotWhileMoving, Charges, Charged, Moves, Exchange }

        /// <summary>
        /// One army game. Carriers: models with their own rules (an Aura or a pick entry on one model). GunRange: the rule side's rifle range.
        /// Length: the table's length. More: further script answers.
        /// </summary>
        static TestGame Game((string Uid, string Army) book, string name, Play play, IReadOnlyList<string> covers, IReadOnlyList<ScenarioRule> rules,
            IReadOnlyList<DiceFix> dice, IReadOnlyList<GameCheck> checks, IReadOnlyList<ScenarioRule>? weapon = null, int models = 3,
            IReadOnlyList<ScenarioRule>? enemy = null, Dictionary<string, string>? enemyStats = null, int enemyUnits = 2, int? enemyModels = null,
            int enemyAttacks = 2, IReadOnlyList<ScenarioRule>? enemyWeapon = null, int? rounds = null, Func<ScenarioBuilder, ScenarioBuilder>? board = null, int? units = null, ulong seed = 1,
            IReadOnlyList<ScenarioModel>? carriers = null, int gunRange = 60, double? length = null, Action<ScriptBuilder>? more = null, string? enemyBook = null,
            IReadOnlyList<(int Unit, string Condition)>? conditions = null, int enemyRange = 60, IReadOnlyList<int>? setAside = null,
            IReadOnlyList<int>? enemySetAside = null, IReadOnlyDictionary<int, Tabletop.Geometry.Point2>? anchors = null,
            IReadOnlyDictionary<int, Tabletop.Geometry.Point2>? enemyAnchors = null)
        {
            var count = units ?? (rules.Count == 0 ? 1 : rules.Max(r => r.Unit));
            var (holds, enemyHolds) = (play is Play.Shoots or Play.Shot or Play.Charged or Play.Exchange, play is not Play.Charged);
            var (guns, enemyGuns) = (play is Play.Shoots or Play.Moves or Play.Exchange, play is Play.Shot or Play.ShotWhileMoving or Play.Exchange);
            IReadOnlyList<ScenarioRule> Still(IReadOnlyList<ScenarioRule> own, int count, bool still) =>
                still ? [.. own, .. Enumerable.Range(1, count).Select(u => R(u, "Immobile"))] : own;
            var close = play is Play.Charges or Play.Charged;
            // A shooting side rolls many dice at a target that lasts (Tough(3), five models a unit), so every face a rule reads comes up whatever the seed.
            var volley = play == Play.Shoots;
            enemy ??= volley ? [.. Enumerable.Range(1, enemyUnits).Select(u => R(u, "Tough", 3))] : [];
            var builder = new ScenarioBuilder(name, seed).Table(36, length ?? (close ? 26 : 36)).RoundCap(rounds ?? 4)
                .Seeking(Closing).Scripted(Script(play, book.Army, count, enemyUnits, more))
                .Army(book.Army, count, models, modelRules: Still(rules, count, holds), armyBook: book.Uid,
                    weapons: guns ? [Gun(volley ? 4 : 1, weapon ?? [], gunRange), Blade(1)] : [Blade(1, [.. weapon ?? []])], models: carriers, conditions: conditions,
                    setAside: setAside, anchors: anchors)
                .Army("Opponents", enemyUnits, enemyModels ?? (volley ? 5 : 3), modelRules: Still(enemy, enemyUnits, enemyHolds), unitStats: enemyStats, armyBook: enemyBook,
                    weapons: enemyGuns ? [Gun(enemyAttacks, enemyWeapon ?? [], enemyRange), Blade(1)] : [Blade(1)], setAside: enemySetAside, anchors: enemyAnchors);
            return new TestGame(name, ArmyPrefix + book.Uid, (board ?? (x => x))(builder).Build(), covers, [RollOff, .. dice], checks);
        }

        /// <summary>
        /// A wound-ignoring rule on three models, shot by one model with three attacks whose fixed hits and blocks deal three wounds. Where the
        /// policy's allocation gives one model more wounds than it can take while another model is left, the excess carries to that model:
        /// "roll one die for each" is read over every ignore die of the deal-wounds frame. Seed 4's allocation carries; about half of all seeds'
        /// allocations carry.
        /// </summary>
        static TestGame Carried((string Uid, string Army) book, string name, string entry, IReadOnlyList<ScenarioRule> rules, GameCheck check) =>
            Game(book, name, Play.Shot, [entry], rules, [Fix("hit", 6, 6, 6), Fix("defense", 1, 1, 1)], [check],
                enemyUnits: 1, enemyModels: 1, enemyAttacks: 3, rounds: 2, seed: 4);

        /// <summary>p.10: wounds that leave the unit at half or less of its starting size force a morale test. Fixed: two of three models fall to the first shooting.</summary>
        static TestGame Morale((string Uid, string Army) book, string name, string entry, IReadOnlyList<ScenarioRule> rules, int amount, string quote) =>
            Game(book, name, Play.Shot, [entry], rules, [Fix("hit", 6, 6, 1), Fix("defense", 1, 1)], [Modifier(entry, "morale", amount, quote)],
                enemyUnits: 1, enemyModels: 1, enemyAttacks: 3, rounds: 2);

        /// <summary>p.4 roll-off, one die per team in team order: the first army (the rule's) wins, deploys first and takes the first turn (p.6, p.7).</summary>
        static DiceFix RollOff => new("roll-off", 0, [6, 1]);
        static DiceFix Fix(string roll, params int[] faces) => new(roll, 0, faces);

        /// <summary>
        /// Every shoot_or_not and strike_back Ask answered yes (the option ids are the server's fixed ones); the moving side's activations are
        /// picked by name, so no seed decides which action a move or charge occasion comes from. Moves: each unit charges when it may, else the
        /// first half of the units Advance and the rest Rush. Charges: the rule's units charge when they may, else Advance; Charged: the
        /// opponent's do. Shoots, Shot and ShotWhileMoving leave activation to the policy (a holding side has one action, Hold).
        /// </summary>
        static AnswerScript Script(Play play, string army, int units, int enemyUnits, Action<ScriptBuilder>? more = null)
        {
            var script = AnswerScript.Build();
            for (var i = 0; i < 80; i++) script.At("shoot_or_not", i, new Chosen("shoot")).At("strike_back", i, new Chosen("strike"));
            var (mover, count) = play == Play.Charged ? ("Opponents", enemyUnits) : (army, units);
            Func<int, string>? otherwise = play switch
            {
                Play.Moves => k => k <= (count + 1) / 2 ? "Advance" : "Rush",
                Play.Charges or Play.Charged => _ => "Advance",
                _ => null,
            };
            if (otherwise is not null)
                script.Match(AskQuestions.Activate, (ask, _) => Enumerable.Range(1, count)
                    .Select(k => AskPicks.Activate(ask, $"{mover} unit {k}", "Charge") ?? AskPicks.Activate(ask, $"{mover} unit {k}", otherwise(k)))
                    .FirstOrDefault(a => a is not null));
            more?.Invoke(script);
            return script.Build();
        }

        static ScenarioWeapon Gun(int attacks, IReadOnlyList<ScenarioRule> rules, int range = 60) =>
            new("Rifle", new Dictionary<string, string> { ["Range"] = $"{range}\"", ["Attacks"] = $"{attacks}" }, rules);
        static ScenarioWeapon Blade(int attacks, IReadOnlyList<ScenarioRule>? rules = null) =>
            new("Blade", new Dictionary<string, string> { ["Attacks"] = $"{attacks}" }, rules ?? []);
        static ScenarioRule R(int unit, string name, int? parameter = null) => new(unit, name, parameter);
        static Dictionary<string, string> Stats(int quality, int defense) => new() { ["Quality"] = $"{quality}+", ["Defense"] = $"{defense}+" };

        /// <summary>Every model's move ranks its legal destinations by the nearest enemy model on the table, so the armies meet.</summary>
        static Point2? Closing(Guid modelId, PolicyView view)
        {
            if (view.PlacedOf(modelId) is not { } placed || view.TeamOf(view.ModelOf(modelId).UnitId) is not { } team) return null;
            var at = placed.Placement.Position.Horizontal;
            return view.Projection.Models.Values
                .Where(m => m.Location is OnTable && view.TeamOf(m.UnitId) != team)
                .Select(m => (Point2?)((OnTable)m.Location).Placement.Position.Horizontal)
                .MinBy(p => p!.Value.DistanceTo(at));
        }

        /// <summary>Low ruins (a cover area) across the first army's zone, so its units stand within 1" of terrain (Grounded Reinforcement).</summary>
        static ScenarioBuilder Ruins(ScenarioBuilder b) => b
            .Terrain("ruin", new Point3(18, 30, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(34, 10), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Cover, null)]);
    }
}
