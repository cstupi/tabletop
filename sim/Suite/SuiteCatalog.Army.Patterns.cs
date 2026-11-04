using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>One game per Army Forge pattern (army-book rules spec → Coverage); each covers its pattern's generated wrappers by its Pattern.</summary>
    private static partial class ArmyGames
    {
        const string BuffQuote = "\"pick one friendly unit within 12\", which gets Stealth once (next time the effect would apply)\"";
        const string MarkQuote = "\"pick one enemy unit within 18\" in line of sight, which friendly units gets Rending against once\"";
        const string DebuffQuote = "\"pick one enemy unit within 18\" in line of sight, which gets -1 to defense rolls once\"";
        const string QuickShotQuote = "\"which friendly units gets Quick Shot against once\": Quick Shot, \"This model may shoot after using Rush actions\"";
        static (string Uid, string Army) LustDaemons => ("9qvy1oufoangt2gl", "Wormhole Daemons of Lust");

        public static IReadOnlyList<TestGame> Patterns =>
        [
            // Shot: the opponents shoot from their zone, at least 12" away, so Stealth's "shot from over 9\"" holds on every shot.
            Game(Custodians, "pattern-aura-stealth", Play.Shot, ["Stealth Aura"], [], [],
                [AuraModifier("Stealth Aura", "Stealth", "hit", -1, "\"This model and its unit get Stealth.\"")],
                carriers: [Carrier("Stealth Aura")]) with { Pattern = "Aura" },
            // The one rule unit is the only friendly unit within 12": it buffs itself, then the first opponent's shooting spends Stealth.
            Game(Hives, "pattern-buff-stealth", Play.Shot, ["Stealth Buff"], [], [],
                [PickWithin("Stealth Buff", 12, friendly: true, BuffQuote),
                 Once("Stealth Buff", $"{BuffQuote}: Stealth -1 to hit", a => a.HitRolls("hit").ToList() is { Count: > 0 } rolls
                     ? rolls.Any(r => r.Modifiers.Any(m => m.Source == "Stealth" && m.Amount == -1)) : null)],
                carriers: [Carrier("Stealth Buff")]) with { Pattern = "Buff" },
            // Unit 1 marks the one enemy unit and shoots it with four fixed 6s; unit 2's shooting after it is unmarked.
            // Opponent unit 2 stands about 21" from the carrier, beyond the pick's 18", and never shot: every shooting is at unit 1, the one in reach.
            Aiming(AnchorAt(AnchoredH(Game(Orcs, "pattern-mark-rending", Play.Shoots, ["Rending Mark"], [], [Fix("hit", 6, 6, 6, 6)],
                [PickWithin("Rending Mark", 18, friendly: false, MarkQuote),
                 Once("Rending Mark", $"{MarkQuote}: p.13 Rending, AP(+4) on unmodified 6s to hit", a => a.Hit is null || Count(a.HitDice, [6]) == 0 ? null : a.Scored("granted_AP_4") > 0)],
                carriers: [Carrier("Rending Mark")], units: 2, enemyUnits: 2, length: 26, more: UnitOneFirst(Orcs.Army)), 15, 11), 1, 2, new(28, 1)), 0, "Opponents unit 1") with { Pattern = "Mark" },
            Aiming(AnchorAt(AnchoredH(Game(Ratmen, "pattern-debuff-defense", Play.Shoots, ["Defense Debuff"], [], [],
                [PickWithin("Defense Debuff", 18, friendly: false, DebuffQuote),
                 Once("Defense Debuff", $"{DebuffQuote}: defense -1", a => a.Blocks.SelectMany(b => b.Rolls("defense")).ToList() is { Count: > 0 } rolls
                     ? rolls.Any(r => r.Modifiers.Any(m => m.Source == "Defense Debuff" && m.Amount == -1)) : null)],
                carriers: [Carrier("Defense Debuff")], units: 2, enemyUnits: 2, length: 26, more: UnitOneFirst(Ratmen.Army)), 15, 11), 1, 2, new(28, 1)), 0, "Opponents unit 1") with { Pattern = "Debuff" },
            // Moves: unit 1 Advances, unit 2 (the carrier's) Rushes 12" toward the one enemy unit, which puts it within 18", is offered the Mark
            // before attacking, and may then shoot the marked unit after its Rush; the script answers every shoot_or_not "shoot".
            Game(LustDaemons, "pattern-mark-quick-shot", Play.Moves, ["Quick Shot Mark"], [], [],
                [PickWithin("Quick Shot Mark", 18, friendly: false, QuickShotQuote), RushShotMarked("Quick Shot Mark", QuickShotQuote)],
                carriers: [new ScenarioModel(2, 1, [R(2, "Quick Shot Mark")])], units: 2, enemyUnits: 1) with { Pattern = "Mark" },
        ];

        /// <summary>The first model of unit 1 carries the rule.</summary>
        static ScenarioModel Carrier(string rule) => new(1, 1, [R(1, rule)]);

        /// <summary>Unit 1 of the army is activated whenever it may be (its only action, holding, is "Hold").</summary>
        static Action<ScriptBuilder> UnitOneFirst(string army) => s => s.Match(AskQuestions.Activate, (ask, _) => AskPicks.Activate(ask, $"{army} unit 1", "Hold"));
    }
}
