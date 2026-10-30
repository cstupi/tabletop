using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 4 (army-book rules spec → Authored rules 4): rules that act as a unit activates, at its activation's end, or on who may activate.</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) TitanLords => ("3j10zage1lddt6sr", "Titan Lords");
        static (string Uid, string Army) WatchBrothers => ("rvvb3kdn2x2pqkki", "Watch Brothers");

        const string ChooseText = "\"When this unit is activated, pick one effect: until the end of the activation, when shooting or charging enemies over 9\" away, all models with this rule in this unit either get AP(+1) or get +1 to hit rolls\"";

        /// <summary>Every activate Ask is answered with an option whose Note or id passes, where it offers one.</summary>
        static Action<ScriptBuilder> Activates(Func<AskOption, bool> pass) => s => s.Match(AskQuestions.Activate, (ask, _) => AskPicks.Option(ask, pass));

        /// <summary>Two activations of one unit in a round: the scheduler's doing, which no step lists.</summary>
        static bool TwiceInARound(IReadOnlyList<GameEvent> log)
        {
            var seen = new HashSet<Guid>();
            foreach (var p in log.Select(e => e.Payload))
            {
                if (p is RoundStarted) seen.Clear();
                if (p is ActionRecorded a && !seen.Add(a.UnitId)) return true;
            }
            return false;
        }

        static TestGame Again((string Uid, string Army) book, string entry) =>
            Game(book, $"{book.Army.ToLowerInvariant().Replace(' ', '-')}-{entry.ToLowerInvariant().Replace(' ', '-')}", Play.Charges, [entry], [R(1, entry)], [Fix("hit", 6), Fix("defense", 1)],
                [SecondActivationClears(entry, "\"Once per game … it may be activated even if it had already activated this round (stops being fatigued when activated for the second time)\"")],
                units: 1, more: Activates(c => c.Note == "Activated again (once per game)"), enemyModels: 1, length: 26, anchors: AgainAnchor, enemyAnchors: AgainEnemyAnchor)
                with { Evidence = new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>> { [entry] = TwiceInARound } };

        /// <summary>The unit starts 4.5" from enemy unit 1 (one model a unit), and its charge's first hit die is fixed to 6 and the block to 1, so the charge destroys it: the first
        /// activation ends in melee won, no morale test, and the unit Fatigued (p.9) before it is activated again.</summary>
        static readonly Dictionary<int, Tabletop.Geometry.Point2> AgainAnchor = new() { [1] = new(16, 15) }, AgainEnemyAnchor = new() { [1] = new(16, 10.5) };

        public static IReadOnlyList<TestGame> Batch4 =>
        [
            Pick(ChangeDaemons, "wormhole-daemons-of-change-bounding", Play.Moves, "Bounding",
                [PlacedWithinD3Plus1("Bounding", "\"When this unit is activated, you may place all models with this rule in it anywhere fully within D3+1\" of their position\"")], grants: false),
            Pick(Custodians, "custodian-brothers-dash", Play.Moves, "Dash",
                [PlacedWithinD3Plus1("Dash", "\"Once per round, at the end of this unit's activation, you may place all models with this rule in it anywhere fully within D3+1\" of their position\"")], grants: false),
            Game(Guerrillas, "rebel-guerrillas-hit-and-run", Play.Shoots, ["Hit & Run"], [R(1, "Hit & Run"), R(2, "Hit & Run")], [],
                [Reposition("Hit & Run", 3, "\"may move by up to 3\" after shooting or being in melee\"")]),
            Game(Hdf, "human-defense-force-coordinate", Play.Moves, ["Coordinate"], [R(1, "Coordinate")], [],
                [CoordinateActivates("\"another friendly unit within 12\" that hasn't activated yet may be activated immediately\"")], units: 3,
                more: s => s.Match("coordinate", (ask, _) => AskPicks.Option(ask, _ => true))),
            Game(TitanLords, "titan-lords-delayed-action", Play.Shoots, ["Delayed Action"], [R(1, "Delayed Action")], [],
                [Delays("\"Once per round, if your opponent has more units left to activate than you, then this model's unit may pass its turn instead of activating\"")],
                units: 1, enemyUnits: 3, more: Activates(c => c.Id.EndsWith("/Delay", StringComparison.Ordinal)))
                with { Evidence = new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>> { ["Delayed Action"] = log => log.Any(e => e.Payload is AskClosed c && c.Answers.Any(a => a.Answer is Chosen { OptionId: var id } && id.EndsWith("/Delay", StringComparison.Ordinal))) } },
            Again(Inquisition, "Inquisitorial Agent"),
            Again(DarkElves, "Martial Prowess"),
            Game(Goblins, "goblin-reclaimers-instinctive", Play.Shoots, ["Instinctive"], [R(1, "Instinctive"), R(2, "Instinctive")], [],
                [Modifier("Instinctive", "hit", 1, "\"it must immediately attack the closest valid target and gets +1 to hit rolls for that attack\""),
                 ClosestOnly("Instinctive", "\"it must immediately attack the closest valid target\"")]),
            // Model 2 of the carrier's unit is the Artillery model in reach.
            Game(BattleBrothers, "battle-brothers-re-position-artillery", Play.Shoots, ["Re-Position Artillery"], [], [],
                [PickMove("Re-Position Artillery", 9, "Artillery", "\"pick one friendly model within 6\" with Artillery, which may immediately move by up to 9\"\"")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Re-Position Artillery")]), new ScenarioModel(1, 2, [R(1, "Artillery")])], units: 1,
                more: UsePicks(BattleBrothers.Army, 1, "Re-Position Artillery")),
            Game(Orcs, "orc-marauders-speed-feat", Play.Moves, ["Speed Feat"], [R(1, "Speed Feat"), R(2, "Speed Feat")], [],
                [ChosenAllowance("Speed Feat", "Speed Feat", 1, "\"Once per game … they move +2\" when using Advance actions and +4\" when using Rush/Charge actions\"", ("advance", 8), ("rush", 16), ("charge", 16))],
                more: s => { UsePicks(Orcs.Army, 1, "Speed Feat")(s); Choose("Speed Feat", "Speed Feat")(s); }),
            Game(War, "war-disciples-versatile-attack", Play.Shoots, ["Versatile Attack"], [R(1, "Versatile Attack"), R(2, "Versatile Attack")], [],
                [Modifier("Versatile Attack", "hit", 1, ChooseText)], more: s => { UsePicks(War.Army, 1, "Versatile Attack")(s); Choose("Versatile Attack", "+1 to hit")(s); }),
            Game(WatchBrothers, "watch-brothers-watchborn", Play.Shoots, ["Watchborn"], [R(1, "Watchborn"), R(2, "Watchborn")], [],
                [Modifier("Watchborn", "hit", 1, ChooseText)], more: s => { UsePicks(WatchBrothers.Army, 1, "Watchborn")(s); Choose("Watchborn", "+1 to hit")(s); }),
            Game(Havoc, "havoc-brothers-versatile-defense", Play.Shot, ["Versatile Defense"], [R(1, "Versatile Defense")], [],
                [Modifier("Versatile Defense", "hit", -1, "\"when shot or charged from over 9\" away, the unit either gets +1 to defense rolls, or enemy units get -1 to hit rolls against it\"")],
                more: s => { UsePicks(Havoc.Army, 1, "Versatile Defense")(s); Choose("Versatile Defense", "-1 to hit")(s); }),
            Game(BattleBrothers, "battle-brothers-versatile-reach", Play.Charges, ["Versatile Reach"], [R(1, "Versatile Reach"), R(2, "Versatile Reach")], [],
                [ChosenAllowance("Versatile Reach", "Versatile Reach: +2\" charge", null, "\"pick one effect: until the end of the activation all models with this rule in it either get +4\" range when shooting, or move +2\" when charging\"", ("charge", 14))],
                more: s => { UsePicks(BattleBrothers.Army, 1, "Versatile Reach")(s); Choose("Versatile Reach", "charge")(s); }),
        ];
    }
}
