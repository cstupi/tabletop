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
    bool ArtilleryHoldOnly,                                       // S2
    bool HasAssault,                                              // S3
    bool CasterTargetNeedsSight, bool SpendersNeedSight, bool SpendersMeasuredToCasterModel, // S4
    bool ImpactBeforeCounter, bool CounterReducesImpact, bool CounterOptional, bool CounterLostWhenFatigued, // S5, S11
    bool HasCumbersome,                                           // S6
    int? HeroAuraTough,                                           // S10
    bool IndirectNeedsHold,                                       // S12
    double ScoutReach, double ScoutClearance,                     // S13
    bool AircraftTransportUnreachable,                            // S15
    bool UnstoppableKeepsOwnPenalties,                            // S16
    double SideBattleLong, double SideBattleShort,                // M1
    string HybridSightPage,
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
        },
        ArtilleryHoldOnly: true,
        HasAssault: false,
        CasterTargetNeedsSight: true, SpendersNeedSight: true, SpendersMeasuredToCasterModel: false,
        ImpactBeforeCounter: false, CounterReducesImpact: true, CounterOptional: false, CounterLostWhenFatigued: false,
        HasCumbersome: false,
        HeroAuraTough: null,
        IndirectNeedsHold: false,
        ScoutReach: 12, ScoutClearance: 0,
        AircraftTransportUnreachable: false,
        UnstoppableKeepsOwnPenalties: false,
        SideBattleLong: 0.75, SideBattleShort: 0.625,
        HybridSightPage: "p.56",
        ArtilleryNote: "Artillery: may only use Hold actions (p.13)",
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
        },
        ArtilleryHoldOnly: false,
        HasAssault: true,
        CasterTargetNeedsSight: false, SpendersNeedSight: false, SpendersMeasuredToCasterModel: true,
        ImpactBeforeCounter: true, CounterReducesImpact: false, CounterOptional: true, CounterLostWhenFatigued: true,
        HasCumbersome: true,
        HeroAuraTough: 9,
        IndirectNeedsHold: true,
        ScoutReach: 9, ScoutClearance: 9,
        AircraftTransportUnreachable: true,
        UnstoppableKeepsOwnPenalties: true,
        SideBattleLong: 2.0 / 3, SideBattleShort: 0.75,
        HybridSightPage: "3.6.0 p.58",
        ArtilleryNote: null,
        CumbersomeNote: "Cumbersome: may only use Hold or Advance actions, and can't shoot when moving (3.6.0 p.14)");

    public static GfEdition Of(GameState state) => Of(state.Info.GameRules);

    public static ZoneLegs ZoneLegsOf(GfEdition edition) => new(edition.SideBattleLong, edition.SideBattleShort);

    public static GfEdition Of(string rulesId) =>
        rulesId == RuleSets.Gf360 ? V360 : rulesId == RuleSets.Gf351 ? V351 : throw new UnknownGameRulesException(rulesId);
}
