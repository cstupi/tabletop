using Tabletop.Geometry;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// 3.6 hand part, batch J (unit, defensive and utility rules): one army:3.6:&lt;book uid&gt; game per entry, each on a 3.6 book that prints it.
/// A rule 3.6 moves from "all models" to "most models" is carried by two of each unit's three models (`Most`), so it acts only under the
/// 3.6 aggregation; a rule whose "shoots/charges enemies over 9"/6" away" band is new gets a second game shooting from 9" or less, where it
/// must not act.
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        /// <summary>The entry on models 1 and 2 of each of the units' three: "most models" have it, not all.</summary>
        static IReadOnlyList<ScenarioModel> Most(int units, string entry, params ScenarioRule[] also) =>
            [.. Enumerable.Range(1, units).SelectMany(u => new[] { 1, 2 }.Select(m => new ScenarioModel(u, m, [R(u, entry), .. also.Select(a => a with { Unit = u })])))];

        /// <summary>The two units start Shaken; the rally rule is on most of their models.</summary>
        static TestGame Rallying36((string Uid, string Army) book, string entry, string quote) =>
            Game(book, $"{Slug(book.Army)}-{Slug(entry)}", Play.Shot, [entry], [], [], [Rallies(entry, quote)], units: 2,
                carriers: Most(2, entry), conditions: [(1, "shaken"), (2, "shaken")]).In36();

        const string Again36 = "\"Once per game, if all models in this unit have this rule, it may be activated even if it had already activated this round (stops being Fatigued when activated again)\"";

        static TestGame SecondTime36((string Uid, string Army) book, string entry) =>
            (Game(book, $"{Slug(book.Army)}-{Slug(entry)}", Play.Charges, [entry], [R(1, entry)], [Fix("hit", 6), Fix("defense", 1)], [SecondActivationClears(entry, Again36)],
                units: 1, more: Activates(c => c.Note == "Activated again (once per game)"), enemyModels: 1, length: 26, anchors: AgainAnchor, enemyAnchors: AgainEnemyAnchor)
                with { Evidence = new Dictionary<string, Func<IReadOnlyList<Tabletop.Protocol.GameEvent>, bool>> { [entry] = TwiceInARound } }).In36();

        /// <summary>
        /// A shooting band's lower side by construction: one unit of the rule's side 4.5" from one opposing unit, both holding, the first three hit dice
        /// fixed to 6; every shot is from 9" or less.
        /// </summary>
        static TestGame Near((string Uid, string Army) book, string name, string entry, GameCheck check, IReadOnlyList<DiceFix>? dice = null) =>
            Game(book, name, Play.Shoots, [], [R(1, entry)], dice ?? [Fix("hit", 6, 6, 6)], [check], units: 1, enemyUnits: 1, length: 26,
                anchors: new Dictionary<int, Point2> { [1] = new(16, 15) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(16, 10.5) }).In36();

        const string Devout36 = "\"When this model shoots/charges enemies over 9\"/6\" away, unmodified result of 6 to hit deal 1 extra hit\"";
        const string Infected36Text = "\"When this model shoots/charges enemies over 9\"/6\" away, unmodified results of 1 to block hits from weapons deal 1 extra wound\"";
        const string Growth36 = "\"place one token on it at the beginning of each round if it's on the table and isn't Shaken, up to a max. of four tokens. For every two tokens";

        /// <summary>
        /// The majority games keep their carriers standing: a unit that loses a carrier keeps the rule on one of two models, no longer "most" (the
        /// engine reads it so), so the defending units are Tough(12) facing one enemy unit: under 3.5.1 the model without the rule
        /// would otherwise die, after which "all models" holds there too.
        /// </summary>
        public static IReadOnlyList<TestGame> Army36J => [.. Army36J1, .. Army36J2];

        static IReadOnlyList<TestGame> Army36J1 =>
        [
            // Both sides hold and shoot; the first hit roll (the rule side's, which acts first) is fixed to three 6s and the one-model target's block to 1s,
            // so the shooting unit destroys a unit at once and the enemy's later shots meet its token.
            Game(Books36.DaemonsOfWar, "daemons-of-war-defensive-frenzy", Play.Exchange, ["Defensive Frenzy"], [], [Fix("hit", 6, 6, 6), Fix("defense", 1, 1, 1)],
                [PerMarker("Defensive Frenzy", "defense", 1, onTarget: true, "\"If most models in this unit have this rule, place one token on this unit when it fully destroys an enemy unit … For each token this unit gets +1 to Defense rolls\"")],
                units: 2, carriers: Most(2, "Defensive Frenzy"), enemyUnits: 4, enemyModels: 1).In36(),
            Game(Books36.HumanInquisition, "human-inquisition-defensive-growth", Play.Shot, ["Defensive Growth"], [R(1, "Tough", 12)], [],
                [PerMarker("Defensive Growth", "defense", 1, onTarget: true, Growth36 + ", this unit gets +1 to Defense rolls\"", perTwo: true)],
                units: 1, carriers: Most(1, "Defensive Growth"), enemyUnits: 1).In36(),
            // Shoots: every shot crosses at least 12".
            Game(Books36.BlessedSisters, "blessed-sisters-devout", Play.Shoots, ["Devout"], [R(1, "Devout"), R(2, "Devout")], [],
                [ExtraHits("Devout", Within(By("Devout", when: When.Shooting), 9, within: false), [6], Devout36)]).In36(),
            Near(Books36.BlessedSisters, "blessed-sisters-devout-within-9", "Devout",
                ExtraHits("Devout", Within(By("Devout", when: When.Shooting), 9), [], Devout36 + " (shot from 9\" or less: none)")),
            Game(Books36.OrcMarauders, "orc-marauders-ferocious", Play.Shoots, ["Ferocious"], [R(1, "Ferocious"), R(2, "Ferocious")], [],
                [ExtraHits("Ferocious", Within(By("Ferocious", when: When.Shooting), 9, within: false), [6], Devout36)]).In36(),
            Near(Books36.OrcMarauders, "orc-marauders-ferocious-within-9", "Ferocious",
                ExtraHits("Ferocious", Within(By("Ferocious", when: When.Shooting), 9), [], Devout36 + " (shot from 9\" or less: none)")),
            Game(Books36.InfectedColonies, "infected-colonies-infected", Play.Shoots, ["Infected"], [R(1, "Infected"), R(2, "Infected")], [],
                [ExtraWounds("Infected", Within(By("Infected", when: When.Shooting), 9, within: false), [1], Infected36Text)]).In36(),
            Near(Books36.InfectedColonies, "infected-colonies-infected-within-9", "Infected",
                ExtraWounds("Infected", Within(By("Infected", when: When.Shooting), 9), [], Infected36Text + " (shot from 9\" or less: none)"),
                [Fix("hit", 6, 6, 6), Fix("defense", 1, 1, 1)]),
            // The volley's targets are Tough(3) and carry Regeneration, which Disintegrate leaves unrolled; their Defense stays 4+.
            Game(Books36.CustodianBrothers, "custodian-brothers-disintegrate", Play.Shoots, ["Disintegrate"], [], [],
                [ApGrant("Disintegrate", With("Disintegrate"), 2, "\"when attacking units where most models have Tough(3) or higher, this weapon gets AP(+2)\""),
                 NoRoll("Disintegrate", With("Disintegrate"), "Regeneration", "\"Ignores Regeneration\"")],
                weapon: [R(0, "Disintegrate")], units: 2, enemy: [R(1, "Tough", 3), R(2, "Tough", 3), R(1, "Regeneration"), R(2, "Regeneration")]).In36(),
            // A 40" table: the zones stand 16" apart, so every shot is from over 12".
            Game(Books36.HumanDefenseForce, "human-defense-force-entrenched", Play.Shot, ["Entrenched"], [], [],
                [Modifier("Entrenched", "hit", -1, "\"If most models in this unit have this rule, as long as they haven't moved during this round, enemy units shooting at them from over 12” away get -1 to hit rolls\"")],
                units: 2, carriers: Most(2, "Entrenched"), length: 40).In36(),
            Game(Books36.SaurianStarhosts, "saurian-starhosts-evasive", Play.Shot, ["Evasive"], [], [],
                [Modifier("Evasive", "hit", -1, "\"Enemies get -1 to hit rolls when attacking units where most models have this rule\"")], units: 2, carriers: Most(2, "Evasive")).In36(),
            Game(Books36.HumanDefenseForce, "human-defense-force-fortified", Play.Shot, ["Fortified"], [R(1, "Tough", 12)], [Fix("hit", 6, 6, 6)],
                [ApGrant("Fortified", On("Fortified", when: When.Shooting), 0, "\"When units where most models have this rule take hits, those hits count as having AP(-1), to a min. of AP(0)\" (against AP(1))")],
                units: 1, carriers: Most(1, "Fortified"), enemyUnits: 1, enemyWeapon: [R(0, "AP", 1)]).In36(),
            // Ruins across the rule side's zone: its models stand within 1" of terrain.
            Game(Books36.SoulSnatcherCults, "soul-snatcher-cults-grounded-defense", Play.Shot, ["Grounded Defense"], [], [Fix("hit", 6, 6, 6)],
                [Modifier("Grounded Defense", "defense", 1, "\"If a unit where most models have this rule has most of them within 1\" of terrain, they get +1 to Defense rolls\"")],
                units: 1, carriers: Most(1, "Grounded Defense"), board: Ruins).In36(),
            Game(Books36.DwarfGuilds, "dwarf-guilds-guarded", Play.Shot, ["Guarded"], [R(1, "Tough", 12), R(2, "Tough", 12)], [Fix("hit", 6, 6, 6)],
                [Modifier("Guarded", "defense", 1, "\"If most models in this unit have this rule, they get +1 to Defense rolls when shot/charged from over 9\"/6\" away\"")],
                units: 2, carriers: Most(2, "Guarded"), enemyUnits: 1).In36(),
            Game(Books36.CustodianBrothers, "custodian-brothers-guardian", Play.Shot, ["Guardian"], [R(1, "Tough", 12)], [Fix("hit", 6, 6, 6)],
                [ApGrant("Guardian", On("Guardian", when: When.Shooting), 0, "\"When units where most models have this rule take hits, those hits count as having AP(-1), to a min. of AP(0)\" (against AP(1))")],
                units: 1, carriers: Most(1, "Guardian"), enemyUnits: 1, enemyWeapon: [R(0, "AP", 1)]).In36(),
            Game(Books36.RebelGuerrillas, "rebel-guerrillas-guerrilla", Play.Shoots, ["Guerrilla"], [R(1, "Guerrilla"), R(2, "Guerrilla")], [],
                [Reposition("Guerrilla", 3, "\"Once per round, this model may move by up to 3\" after shooting or being in melee\"")]).In36(),
            Game(Books36.DarkElfRaiders, "dark-elf-raiders-harassing", Play.Shoots, ["Harassing"], [R(1, "Harassing"), R(2, "Harassing")], [],
                [Reposition("Harassing", 3, "\"Once per round, this model may move by up to 3\" after shooting or being in melee\"")]).In36(),
            Rallying36(Books36.TitanLords, "Honor Code", "\"If a unit where most models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being Shaken.\""),
            SecondTime36(Books36.HumanInquisition, "Inquisitorial Agent"),
            SecondTime36(Books36.DarkElfRaiders, "Martial Prowess"),
        ];
    }
}
