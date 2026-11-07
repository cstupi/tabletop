using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// What differs between Grimdark Future 3.5.1 and 3.6.0 (rule-set versions spec → Where the rules live), read from GameInfo.GameRules
/// as GfOptions.Of reads OptionalRules. One record, two instances; the code shared by both editions reads a field where the rules differ.
/// The comments name the rules-diff item each field carries.
/// </summary>
public sealed record GfEdition(
    string Id, string Name,
    string ContentFolder,
    double MeleeHorizontal, double MeleeVertical,                 // C1
    bool FatiguedSixesTrigger,                                    // C2
    bool MeleeLossByWoundsTaken,                                  // C3
    bool UnitDangerousTest,                                       // C4: false per model on Tough dice, true the whole unit on dice = wounds to destroy
    bool UnstableByDistance,                                      // C5: false DangerousOnRushCharge on Rush/Charge, true over 6" moved into or through
    IReadOnlyDictionary<TerrainDeclarations, string> TerrainWords, // C5: the card and label words; a flag not listed keeps today's word
    IReadOnlyDictionary<TerrainDeclarations, string> TerrainRules, // the terrain card's sentence per declaration: 3.5.1 p.11-12, 3.6.0 p.12
    bool ArtilleryHoldOnly,                                       // S2
    bool HasAssault,                                              // S3
    bool CasterTargetNeedsSight, bool SpendersNeedSight, bool SpendersMeasuredToCasterModel, bool OneCastWindow, // S4
    bool ImpactBeforeCounter, bool CounterReducesImpact, bool CounterOptional, bool CounterLostWhenFatigued, // S5, S11
    bool HasCumbersome,                                           // S6
    bool DefenderChoice,                                          // attack-sequence asks the defender's `attacked` choice (Versatile Defense) before the roll to hit
    int? HeroAuraTough,                                           // S10
    bool IndirectNeedsHold,                                       // S12
    double ScoutReach, double ScoutClearance,                     // S13
    double VanguardClearance,                                     // Fanatic, Vanguard "over 9" away from non-Vanguard enemy units"; 0 none
    bool AircraftTransportUnreachable,                            // S15
    bool PlacedUnitsHoldNoObjectives,                             // Spawn / Split "can't seize or contest objectives on the round they are placed"
    bool ReinforcementOnShaken, double ReinforcementEdgeReach, double ReinforcementClearance, // Reinforcement's removal when Shaken, edge strip, distance from enemies
    bool HazardousOnModel,                                        // Hazardous's 1 wounds the model that rolled it, X wounds with Blast(X)/Deadly(X)
    bool StrikeTurnRolls,                                         // Ravage and Regenerative Strength roll at the model's turn to strike, once per round, not Fatigued
    bool AttackRangeRecorded,                                     // determine-attacks records `range_hundredths`, which a morale test after shooting reads ("when shot from over 9\" away")
    bool HasParry,                                                // Parry's roll opens resolve-hits under a melee (GfParry); 3.6 books carry the rule
    double SideBattleLong, double SideBattleShort,                // M1
    string? ArtilleryNote,
    string? CumbersomeNote)
{
    public static GfEdition V351 { get; } = new(
        RuleSets.Gf351, "Grimdark Future v3.5.1", "grimdark-future-3.5.1",
        MeleeHorizontal: GfNames.MeleeHorizontal, MeleeVertical: GfNames.MeleeVertical,
        FatiguedSixesTrigger: true,
        MeleeLossByWoundsTaken: false,
        UnitDangerousTest: false,
        UnstableByDistance: false,
        TerrainWords: new Dictionary<TerrainDeclarations, string>
        {
            [TerrainDeclarations.BlocksLineOfSight] = "Blocks line of sight",
            [TerrainDeclarations.SeeInAndOut] = "See in and out",
            [TerrainDeclarations.DangerousOnRushCharge] = "Dangerous when rushing or charging",
            [TerrainDeclarations.Steep] = "Difficult moving up",
        },
        TerrainRules: new Dictionary<TerrainDeclarations, string>
        {
            [TerrainDeclarations.Cover] = "+1 to Defense rolls against shooting while most of the unit's models are fully inside.",
            [TerrainDeclarations.Difficult] = "A unit with any model moving in or through it moves at most 6\" that move.",
            [TerrainDeclarations.Dangerous] = "A model moving in or through it, or activated in it, takes a dangerous terrain test: a 1 on any die is a wound.",
            [TerrainDeclarations.DangerousOnRushCharge] = "Dangerous terrain for a unit using Rush or Charge.",
            [TerrainDeclarations.Steep] = "Difficult terrain for a model moving up onto it; open terrain moving off it.",
            [TerrainDeclarations.Impassable] = "Units may not move through it.",
            [TerrainDeclarations.BlocksLineOfSight] = "Units may not draw line of sight through it.",
            [TerrainDeclarations.SeeInAndOut] = "Units may see into and out of it, but not through it.",
        },
        ArtilleryHoldOnly: true,
        HasAssault: false,
        CasterTargetNeedsSight: true, SpendersNeedSight: true, SpendersMeasuredToCasterModel: false, OneCastWindow: false,
        ImpactBeforeCounter: false, CounterReducesImpact: true, CounterOptional: false, CounterLostWhenFatigued: false,
        HasCumbersome: false,
        DefenderChoice: false,
        HeroAuraTough: null,
        IndirectNeedsHold: false,
        ScoutReach: 12, ScoutClearance: 0,
        VanguardClearance: 0,
        AircraftTransportUnreachable: false,
        PlacedUnitsHoldNoObjectives: false,
        ReinforcementOnShaken: false, ReinforcementEdgeReach: 12, ReinforcementClearance: 0,
        HazardousOnModel: false,
        StrikeTurnRolls: false,
        AttackRangeRecorded: false,
        HasParry: false,
        SideBattleLong: 0.75, SideBattleShort: 0.625,
        ArtilleryNote: "Artillery: may only use Hold actions",
        CumbersomeNote: null);

    public static GfEdition V360 { get; } = new(
        RuleSets.Gf360, "Grimdark Future v3.6.0", "grimdark-future-3.6",
        MeleeHorizontal: 3, MeleeVertical: 3,
        FatiguedSixesTrigger: false,
        MeleeLossByWoundsTaken: true,
        UnitDangerousTest: true,
        UnstableByDistance: true,
        TerrainWords: new Dictionary<TerrainDeclarations, string>
        {
            [TerrainDeclarations.BlocksLineOfSight] = "Sight blocking",
            [TerrainDeclarations.SeeInAndOut] = "Dense",
            [TerrainDeclarations.DangerousOnRushCharge] = "Unstable",
            [TerrainDeclarations.Steep] = "Steep",
        },
        TerrainRules: new Dictionary<TerrainDeclarations, string>
        {
            [TerrainDeclarations.Cover] = "+1 to Defense rolls against shooting while most of the unit's models are fully inside.",
            [TerrainDeclarations.Difficult] = "A unit with any model moving in or through it moves at most 6\" that move.",
            [TerrainDeclarations.Dangerous] = "A unit with any model moving into or through it, or activating in it, tests as a whole unit: a die per wound to destroy it, each 1 a wound.",
            [TerrainDeclarations.DangerousOnRushCharge] = "Dangerous terrain for a model moving over 6\" into or through it.",
            [TerrainDeclarations.Steep] = "Difficult terrain for a model moving onto it; open terrain moving off it.",
            [TerrainDeclarations.Impassable] = "Units may not move through it.",
            [TerrainDeclarations.BlocksLineOfSight] = "Units may not draw line of sight through it.",
            [TerrainDeclarations.SeeInAndOut] = "Units may draw line of sight into and out of it, but not through it.",
        },
        ArtilleryHoldOnly: false,
        HasAssault: true,
        CasterTargetNeedsSight: false, SpendersNeedSight: false, SpendersMeasuredToCasterModel: true, OneCastWindow: true,
        ImpactBeforeCounter: true, CounterReducesImpact: false, CounterOptional: true, CounterLostWhenFatigued: true,
        HasCumbersome: true,
        DefenderChoice: true,
        HeroAuraTough: 9,
        IndirectNeedsHold: true,
        ScoutReach: 9, ScoutClearance: 9,
        VanguardClearance: 9,
        AircraftTransportUnreachable: true,
        PlacedUnitsHoldNoObjectives: true,
        ReinforcementOnShaken: true, ReinforcementEdgeReach: 9, ReinforcementClearance: 9,
        HazardousOnModel: true,
        StrikeTurnRolls: true,
        AttackRangeRecorded: true,
        HasParry: true,
        SideBattleLong: 2.0 / 3, SideBattleShort: 0.75,
        ArtilleryNote: null,
        CumbersomeNote: "Cumbersome: may only use Hold or Advance actions, and can't shoot when moving");

    public static GfEdition Of(GameState state) => Of(state.Info.GameRules);

    public static ZoneLegs ZoneLegsOf(GfEdition edition) => new(edition.SideBattleLong, edition.SideBattleShort);

    public static GfEdition Of(string rulesId) =>
        rulesId == RuleSets.Gf360 ? V360 : rulesId == RuleSets.Gf351 ? V351 : throw new UnknownGameRulesException(rulesId);
}
