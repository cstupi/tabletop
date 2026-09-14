using System.Text.Json.Serialization;

namespace Tabletop.Protocol;

/// <summary>
/// A question addressed to one team (several for a sealed window), with the server-computed legal set.
/// Any team member may answer; the log records who. FrameId is null for the scheduler's Asks.
/// WindowKind is the name a player mutes by; null when the Ask is not a window.
/// </summary>
public sealed record Ask(
    Guid Id, string Question, string Prompt, IReadOnlyList<Guid> Teams, LegalSet Legal,
    bool AllowPass, bool Sealed, string? WindowKind, Guid? FrameId);

/// <summary>A discrete choice. Pieces are the ids to highlight (for activate: the unit).</summary>
public sealed record AskOption(string Id, string Label, IReadOnlyList<Guid> Pieces);

/// <summary>The engine-owned questions. Ruleset questions are GameRules' own names.</summary>
public static class AskQuestions
{
    public const string Activate = "activate";
    public const string ActOrEndPhase = "act_or_end_phase";
    public const string Deploy = "deploy";
    public const string DeployScouts = "deploy_scouts";
    public const string DeployAmbusher = "deploy_ambusher";
    /// <summary>The round boundary: an unframed Confirmation Ask to every team, whose first answer starts the next round.</summary>
    public const string EndRound = "end_round";
    public const string Confirm = "confirm";
    public const string React = "react";
    public const string SpendTokens = "spend_tokens";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Options), "options")]
[JsonDerivedType(typeof(PieceSelection), "pieces")]
[JsonDerivedType(typeof(Allocation), "allocation")]
[JsonDerivedType(typeof(Assignment), "assignment")]
[JsonDerivedType(typeof(MoveRegion), "move_region")]
[JsonDerivedType(typeof(TokenSpend), "token_spend")]
[JsonDerivedType(typeof(Confirmation), "confirmation")]
public abstract record LegalSet;
public sealed record Options(IReadOnlyList<AskOption> Choices) : LegalSet;
/// <summary>
/// Pick between Min and Max of the Eligible ids. The ids are units (deploy, an Ambusher, a charge target) or models
/// (a model-grain spell, a Takedown's target model — rulebook p.14); nothing here distinguishes the two grains.
/// </summary>
public sealed record PieceSelection(IReadOnlyList<Guid> Eligible, int Min, int Max) : LegalSet;
/// <summary>Distribute Amount over Targets, exactly (wound assignment, casualty removal): the counts sum to Amount.</summary>
public sealed record Allocation(IReadOnlyList<Guid> Targets, int Amount) : LegalSet;
/// <summary>Give each item one target (weapon groups to up to N targets).</summary>
public sealed record Assignment(IReadOnlyList<AskOption> Items, IReadOnlyList<Guid> Targets, int MaxDistinctTargets) : LegalSet;
/// <summary>
/// Anywhere within the allowance: parameters, not an enumeration. The client draws the region with Tabletop.Geometry.Movement.Region.
/// ImpassableVolumeIds and DifficultVolumeIds name volumes on the Board (TerrainPlacement.WorldVolumes()); DifficultCap is the
/// whole-move cap GameRules computed when any listed difficult volume is entered (OPR: 6"), null when the ruleset has none.
/// Containment is non-null only when this move may embark or is disembarking; null is every move on open table.
/// </summary>
public sealed record MoveRegion(IReadOnlyList<Guid> ModelIds, double Allowance, IReadOnlyList<Guid> ImpassableVolumeIds,
    IReadOnlyList<Guid> DifficultVolumeIds, double? DifficultCap, MoveContainment? Containment = null) : LegalSet;
/// <summary>
/// What containment allows on one move (rulebook p.15 Transport(X), p.21 solid buildings). Enterable are the containers a
/// model reaching one embarks the whole unit into, so the answer is Inside(container) for every model of the unit. ExitingId
/// is the container the unit is inside and ExitWithin the distance its models must be placed fully within of it (OPR: 6"),
/// which is Tabletop.Geometry.Containment.BaseFullyWithin; the client draws that region rather than Movement.Region, because
/// an occupant has no position to move from. Never both — a unit may not embark and disembark in one activation.
/// </summary>
public sealed record MoveContainment(IReadOnlyList<Guid> Enterable, Guid? ExitingId, double? ExitWithin);
/// <summary>
/// Spend spell tokens at the same time as every other addressed team, each token giving the caster +1/-1 on the one casting
/// roll (rulebook p.13 Caster(X)). MaxByModel is every model that may pay — on the table, holding tokens, within 18" and in
/// line of sight of the caster's unit, the caster itself included with what it holds after the spell's cost — keyed to the
/// tokens it holds, which is its cap. A team's maximum is the sum of its own entries and is carried nowhere else. One map
/// serves every addressed team, and it discloses nothing: token counts are in every client's projection.
/// </summary>
public sealed record TokenSpend(IReadOnlyDictionary<Guid, int> MaxByModel) : LegalSet;
public sealed record Confirmation : LegalSet;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Chosen), "chosen")]
[JsonDerivedType(typeof(SelectedPieces), "selected_pieces")]
[JsonDerivedType(typeof(Allocated), "allocated")]
[JsonDerivedType(typeof(Assigned), "assigned")]
[JsonDerivedType(typeof(Destinations), "destinations")]
[JsonDerivedType(typeof(TokensSpent), "tokens_spent")]
[JsonDerivedType(typeof(Confirmed), "confirmed")]
[JsonDerivedType(typeof(Passed), "passed")]
public abstract record AskAnswer;
public sealed record Chosen(string OptionId) : AskAnswer;
public sealed record SelectedPieces(IReadOnlyList<Guid> PieceIds) : AskAnswer;
public sealed record Allocated(IReadOnlyDictionary<Guid, int> Counts) : AskAnswer;
public sealed record Assigned(IReadOnlyDictionary<string, Guid> Targets) : AskAnswer;
public sealed record Destinations(IReadOnlyList<ModelRelocation> Moves) : AskAnswer;
/// <summary>Tokens paid per model. A model absent from Counts paid nothing; an empty map says what Passed says.</summary>
public sealed record TokensSpent(IReadOnlyDictionary<Guid, int> Counts) : AskAnswer;
public sealed record Confirmed : AskAnswer;
/// <summary>A real answer, logged; only legal when the Ask allows it.</summary>
public sealed record Passed : AskAnswer;
