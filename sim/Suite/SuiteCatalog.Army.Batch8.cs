using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 8 (army-book rules spec → Authored rules 8): the rules the grammar additions play.</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) DarkBrothers => ("xp5zwh73lg1uaym4", "Dark Brothers");

        /// <summary>Every defense roll of a unit holding the entry is against the stated number.</summary>
        static GameCheck DefenseAt(string entry, int value, string quote) =>
            Exact(entry, quote, (run, log) =>
                from s in log.Steps.Where(s => s.Step.Step == "roll-to-block" && Holds(run, s.Frame.Context.TargetUnitIds.FirstOrDefault(), entry))
                from r in s.Rolls("defense")
                select r.Threshold == value ? null : $"defense at {r.Threshold}+");

        /// <summary>A charge at a unit holding the entry: its first move Ask allows the stated distance (p.7's 12" less the rule's, to its minimum).</summary>
        static GameCheck ChargeAt(string entry, double inches, string quote) =>
            Exact(entry, $"{quote}: charge {inches}\"", (run, log) =>
                from a in log.Asks
                where a.Ask.Question == "move" && a.Frame is { Sequence: "charge" } f
                let target = log.Steps.FirstOrDefault(s => s.Frame.FrameId == a.Frame!.FrameId && s.Step.Step == "charge-target")?.Step.Answer as SelectedPieces
                where target is { PieceIds: [var t, ..] } && Holds(run, t, entry)
                group a by a.Frame!.FrameId into frame
                select frame.First().Ask.Legal is MoveRegion m && m.Allowance == inches ? null : $"charge allowed {(frame.First().Ask.Legal as MoveRegion)?.Allowance}\"");

        /// <summary>A shot at a unit holding the entry is made from within the stated range, as the attack began.</summary>
        static GameCheck ShotWithin(string entry, double inches, string quote) =>
            Exact(entry, $"{quote}: shot from within {inches}\"", (run, log) =>
                from a in log.Attacks
                where a.Shooting && Holds(run, a.Target, entry) && a.Attacker is not null && PatternLog.For(run).Attacks.ContainsKey(a.Frame.FrameId)
                let apart = UnitApart(run, PatternLog.For(run).Attacks[a.Frame.FrameId].At, a.Attacker!.Value, a.Target!.Value)
                select apart <= inches + 0.01 ? null : $"shot from {apart:0.##}\"");

        /// <summary>Heavy Impact: the blocks of Impact's hits (melee's weapon-less block) of a charger holding it are under AP(1).</summary>
        static GameCheck ImpactAp(string quote) =>
            Exact("Heavy Impact", quote, (run, log) =>
                from s in log.Steps.Where(s => s.Step.Step == "roll-to-block" && s.Frame.Sequence == "resolve-hits" && s.Ancestors.FirstOrDefault()?.Sequence == MeleeSequence
                    && s.Frame.Context.WeaponIds.Count == 0 && Holds(run, s.Frame.Context.ActorUnitId, "Heavy Impact"))
                from r in s.Rolls("defense")
                select r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -1) ? null : "an Impact block without AP(1)");

        /// <summary>No Retreat: a failed test it turns into a pass rolls one die per wound left to the unit, and wounds on each 1-3 with no roll to ignore.</summary>
        static GameCheck Holds(string quote) =>
            Exact("No Retreat", quote, (run, log) =>
                from s in log.Steps.Where(s => s.Frame.Sequence == "morale-test" && s.Step.Step == "consequence" && s.Value("no_retreat") == 1 && s.Rolls("No Retreat").Any())
                let roll = s.Rolls("No Retreat").Single()
                let wounds = roll.Dice.Count(d => d.Raw <= 3)
                select s.Value("passed") != 1 ? "the test did not count as passed" : s.Value("wounds") != wounds ? $"{s.Value("wounds")} wounds from {Faces(roll.Dice)}"
                    : log.Steps.Any(x => x.Frame.Sequence == "deal-wounds" && x.Ancestors.FirstOrDefault()?.FrameId == s.Frame.FrameId && x.Step.Rolls.Count > 0) ? "a wound was rolled to ignore" : null);

        /// <summary>Quick Readjustment: no hit roll of a holder's Indirect weapons after moving carries Indirect's -1.</summary>
        static GameCheck NoMovedPenalty(string quote) =>
            Exact("Quick Readjustment", quote, (run, log) =>
                from a in log.Attacks
                where a.Shooting && a.Hit is not null && Holds(run, a.Attacker, "Quick Readjustment") && a.Hit.Nearest("advance") is not null
                from r in a.HitRolls("hit")
                select r.Modifiers.Any(m => m.Source == "Indirect") ? "a hit roll under Indirect's -1" : null);

        public static IReadOnlyList<TestGame> Batch8 =>
        [
            Game(Saurians, "saurian-starhost-armor", Play.Shot, ["Armor"], [R(1, "Armor", 3)], [], [DefenseAt("Armor", 3, "\"Counts as having Defense X+\" (X 3)")]),
            Game(DarkBrothers, "dark-brothers-darkborn", Play.Charged, ["Darkborn"], [R(1, "Darkborn"), R(2, "Darkborn"), R(3, "Darkborn")], [],
                [ChargeAt("Darkborn", 10, "\"-2\" movement to a min. of 6\" when trying to charge units where all models have this rule\"")], enemyUnits: 4),
            Game(BattleBrothers, "battle-brothers-melee-shrouding", Play.Charged, ["Melee Shrouding"], [R(1, "Melee Shrouding"), R(2, "Melee Shrouding"), R(3, "Melee Shrouding")], [],
                [ChargeAt("Melee Shrouding", 9, "\"Enemies get -3\" movement to a min. of 6\" when trying to charge units where all models have this rule\"")], enemyUnits: 4),
            // Unit 1 stands 14" from the opponents (AnchoredH), inside the shrouded guns' 18"; unit 2 about 22" from opponent unit 2, inside the 24" guns
            // and beyond 18", and the opponents shoot it first whenever it is a legal target.
            Aiming(AnchorAt(AnchoredH(Game(Orcs, "orc-marauders-ranged-shrouding", Play.Shot, ["Ranged Shrouding"], [R(1, "Ranged Shrouding"), R(2, "Ranged Shrouding")], [],
                [ShotWithin("Ranged Shrouding", 18, "\"Enemies get -6\" range to a min. of 6\" when trying to shoot units where all models have this rule\" (24\" guns)")],
                units: 2, enemyRange: 24), 25, 11), 0, 2, new(30, 33)), 1, $"{Orcs.Army} unit 2"),
            Game(Hdf, "human-defense-force-entrenched", Play.Shot, ["Entrenched"], [R(1, "Entrenched"), R(2, "Entrenched")], [],
                [Modifier("Entrenched", "hit", -2, "\"As long as a unit where all models have this rule hasn't moved during the round, enemy units shooting at it from over 9\" away get -2 to hit rolls\"")], units: 2),
            Game(Robots, "robot-legions-mobile-artillery", Play.Shoots, ["Mobile Artillery"], [R(1, "Mobile Artillery"), R(2, "Mobile Artillery")], [],
                [Modifier("Mobile Artillery", "hit", 1, "\"When this model uses a Hold action and shoots at enemies over 9\" away, it gets +1 to hit rolls\"")]),
            Game(Cults, "soul-snatcher-cults-grounded-precision", Play.Shoots, ["Grounded Precision"], [R(1, "Grounded Precision"), R(2, "Grounded Precision")], [],
                [Modifier("Grounded Precision", "hit", 1, "\"If a unit where all models have this rule has most of them within 1\" of terrain, they get +1 to hit rolls when attacking\"")], board: Ruins),
            Game(Saurians, "saurian-starhost-heavy-impact", Play.Charges, ["Heavy Impact"], [R(1, "Heavy Impact", 2), R(2, "Heavy Impact", 2), R(3, "Heavy Impact", 2)], [],
                [ImpactAp("\"Counts as having Impact(X) with hits that have AP(1)\"")], units: 3),
            // p.10: the first shooting's fixed hits and blocks kill two of three models, the unit tests morale at half, and the fixed 1 fails it.
            Game(Infected, "infected-colonies-no-retreat", Play.Shot, ["No Retreat"], [R(1, "No Retreat"), R(2, "No Retreat")], [Fix("hit", 6, 6, 1), Fix("defense", 1, 1), Fix("morale", 1)],
                [Holds("\"the test counts as passed instead. Then, roll as many dice as the number of wounds it would take to fully destroy it, and for each result of 1-3 the unit takes one wound, which can't be ignored\"")],
                enemyUnits: 1, enemyModels: 1, enemyAttacks: 3, units: 2),
            Game(Ratmen, "ratmen-clans-quick-readjustment", Play.Moves, ["Quick Readjustment"], [R(1, "Quick Readjustment"), R(2, "Quick Readjustment")], [],
                [NoMovedPenalty("\"This model ignores penalties from shooting after moving when using Indirect weapons\"")], weapon: [R(0, "Indirect")]),
        ];
    }
}
