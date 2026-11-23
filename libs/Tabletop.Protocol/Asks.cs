using System.Text.Json.Serialization;

namespace Tabletop.Protocol;

/// <summary>
/// A question addressed to one team (several for a sealed window), with the server-computed legal set.
/// Any seat of an addressed team may answer it, for any unit of the team (rulebook p.50 Team Games: "the turn order is on a team basis and not
/// on a player basis"; owner ruling: "Either player from the team can take the activation across any unit in their team."); the log records
/// who. FrameId is null for the scheduler's Asks. WindowKind names the window (before-attacking, casting); null when the Ask is not a window.
/// </summary>
public sealed record Ask(
    Guid Id, string Question, string Prompt, IReadOnlyList<Guid> Teams, LegalSet Legal,
    bool AllowPass, bool Sealed, string? WindowKind, Guid? FrameId);

/// <summary>
/// A discrete choice. Pieces are the ids to highlight (for activate: the unit). Note, when set, is the ruleset's reason this
/// choice's set is limited, in the book's words with its page (activate: every option of a limited unit carries the same note);
/// null when nothing limits it. Clients show the text as sent.
/// </summary>
public sealed record AskOption(string Id, string Label, IReadOnlyList<Guid> Pieces, string? Note = null);

/// <summary>The engine-owned questions. Ruleset questions are GameRules' own names.</summary>
public static class AskQuestions
{
    public const string Activate = "activate";
    public const string ActOrEndPhase = "act_or_end_phase";
    public const string Deploy = "deploy";
    public const string DeployScouts = "deploy_scouts";
    public const string DeployAmbusher = "deploy_ambusher";
    /// <summary>A round-boundary pick of a unit a rule brings in from the army's reserve units, placed by hand as an Ambusher is.</summary>
    public const string DeployReinforcement = "deploy_reinforcement";
    /// <summary>The round boundary: an unframed Confirmation Ask to every team, whose first answer starts the next round.</summary>
    public const string EndRound = "end_round";
    public const string Confirm = "confirm";
    public const string React = "react";
    public const string SpendTokens = "spend_tokens";
    /// <summary>warn's answer to a breach that ends the game: an unframed, sealed Options Ask to every team, End or Continue. Any End ends the game; it goes on only when every team continues.</summary>
    public const string GameOver = "game_over";
}

/// <summary>The option ids of the game_over Ask.</summary>
public static class GameOverChoices
{
    public const string End = "end", Continue = "continue";
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
/// KeepAway, at a deploy question, is the distance from enemy units each eligible unit is placed by, keyed by the unit; a unit it does not name
/// has none. Null: no eligible unit has one.
/// ZoneReach, at a deploy question, is how far from the placing team's deployment zone each eligible unit is placed, keyed by the unit: every
/// base fully within that many inches of the zone (OPR p.14 Scout 12", 3.6.0 p.15 9"; 0 is fully within the zone). A unit it does not name, or
/// null, is held to no reach the Ask states.
/// </summary>
public sealed record PieceSelection(IReadOnlyList<Guid> Eligible, int Min, int Max, IReadOnlyDictionary<Guid, KeepAway>? KeepAway = null,
    IReadOnlyDictionary<Guid, double>? ZoneReach = null) : LegalSet;
/// <summary>Distribute Amount over Targets, exactly (wound assignment, casualty removal): the counts sum to Amount, and at most
/// MaxTargets targets get a count above zero (null: any number).</summary>
public sealed record Allocation(IReadOnlyList<Guid> Targets, int Amount, int? MaxTargets = null) : LegalSet;
/// <summary>
/// Give items one target each, at least one item and at most MaxDistinctTargets distinct targets in all (weapon groups, rulebook
/// p.8: up to two); an item the answer leaves out takes none (a weapon group that holds fire, p.8: models "may" shoot).
/// Targets is every target some item may take. TargetsByItem, when non-null, is the targets each item may take, keyed by item id
/// (a weapon group to the units it can legally shoot); an item it does not name takes none. Null lets every item take any of Targets.
/// </summary>
public sealed record Assignment(IReadOnlyList<AskOption> Items, IReadOnlyList<Guid> Targets, int MaxDistinctTargets,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>>? TargetsByItem = null) : LegalSet
{
    /// <summary>The targets one item may take.</summary>
    public IReadOnlyList<Guid> TargetsOf(string itemId) =>
        TargetsByItem is null ? Targets : TargetsByItem.TryGetValue(itemId, out var own) ? own : [];
}
/// <summary>
/// Anywhere within the allowance: parameters, not an enumeration. The client draws the region with Tabletop.Geometry.Movement.Region.
/// ImpassableVolumeIds and DifficultVolumeIds name volumes on the Board (TerrainPlacement.WorldVolumes()); DifficultCap is what is
/// left of the ruleset's cap in this segment (OPR: the 6" less what an earlier segment of the move used), applied when any listed difficult volume is entered; null when the ruleset has none. It holds
/// every listed model once any one of them enters (OPR p.11: "all models in the unit may not move more than 6"").
/// Containment is non-null only when this move may embark or is disembarking; null is every move on open table.
/// MinDistance, when set, is how far every listed model must move (OPR p.13 Aircraft: 30"), or as far as the table lets its whole
/// base go (Tabletop.Geometry.Movement.FarthestOnTable) when that is less; the region is then an annulus. Null is no minimum. The "when
/// that is less" is a standing ruling until the owner rules: p.13 assumes the room, p.6 allows "smaller areas", p.7 keeps every base on the table.
/// Stoppable: the move may stop here and go on after the window that follows a segment (OPR p.13 Caster(X) "at any point before
/// attacking"), answered by Destinations.Continues. True only when that window has something to offer the mover now.
/// Separation: how far the moving models must stay from models of other units (OPR p.7: 1"); null is no limit.
/// PassesModels: the unit may move through other models (OPR p.13 Flying, Aircraft); false is "may never move through other models".
/// IgnoresTerrain: the unit ignores terrain when moving and stopping (OPR p.13 Aircraft), so no landing in terrain is judged either.
/// ContactWith: the models a moving model must end in base contact with when a path reaches one within the allowance (OPR p.9 Charge:
/// "must move by up to 12” to get into base contact with an enemy model from the target unit, or as close as possible"); null for
/// every other move.
/// ContactGrace: with ContactWith, how far apart a moving model's base may end from one of those models and still count as reaching it; the
/// server draws such a model flush into base contact, and a client draws its landing flush the same way. Null with no ContactWith.
/// ContactVertical: with ContactWith, how far above or below one of those models' bases a moving model's landing may rest and still be in contact
/// with it (OPR p.9 who can strike: 3.5.1 4" vertically, 3.6.0 3"); a landing farther than that is not drawn against it. Null with no ContactWith.
/// SteepVolumeIds: volumes on the Board that count as difficult for a model whose base starts off the footprint (sharing no interior with it), and as
/// open ground for one whose base starts on it (3.6.0 p.12 Steep; 3.5.1 p.12 Hills "Difficult when moving up"): Tabletop.Geometry.Movement.SteepEntering per model.
/// Null is none.
/// DangerousVolumeIds: volumes on the Board that are dangerous terrain to this move, entered at any length (OPR p.11 "moves in or through dangerous
/// terrain"; 3.5.1 p.12's "Dangerous when using rush/charge" on a Rush or a Charge). UnstableVolumeIds: volumes that count as dangerous terrain only to a
/// model whose move is over UnstableOver (3.6.0 p.12 Unstable: "moves over 6” when moving into or through unstable terrain"), UnstableOver being what is
/// left of that length after the earlier segments of the move. Both null for a unit that ignores dangerous terrain or a move that tests none. The client
/// draws its area in bands by them (Tabletop.Geometry.MoveReach.Bands); they change no path and no reach.
/// KeepAway: how far from enemy units the moving models are placed (3.6 Fanatic, Vanguard: "over 9” away from non-Vanguard enemy units"); null is none.
/// </summary>
public sealed record MoveRegion(IReadOnlyList<Guid> ModelIds, double Allowance, IReadOnlyList<Guid> ImpassableVolumeIds,
    IReadOnlyList<Guid> DifficultVolumeIds, double? DifficultCap, MoveContainment? Containment = null, double? MinDistance = null, bool Stoppable = false,
    double? Separation = null, bool PassesModels = false, bool IgnoresTerrain = false, IReadOnlyList<Guid>? ContactWith = null, double? ContactGrace = null,
    IReadOnlyList<Guid>? SteepVolumeIds = null, IReadOnlyList<Guid>? DangerousVolumeIds = null, IReadOnlyList<Guid>? UnstableVolumeIds = null,
    double? UnstableOver = null, KeepAway? KeepAway = null, double? ContactVertical = null) : LegalSet;
/// <summary>
/// What containment allows on one move (rulebook p.15 Transport(X), p.21 solid buildings). Enterable are the containers a
/// model reaching one embarks the whole unit into, so the answer is Inside(container) for every model of the unit. ExitingId
/// is the container the unit is inside and ExitWithin the distance its models must be placed fully within of it (OPR: 6"),
/// which is Tabletop.Geometry.Containment.BaseFullyWithin; the client draws that region rather than Movement.Region, because
/// an occupant has no position to move from. Never both — a unit may not embark and disembark in one activation.
/// </summary>
public sealed record MoveContainment(IReadOnlyList<Guid> Enterable, Guid? ExitingId, double? ExitWithin);
/// <summary>
/// A placement "over X away from enemy units" (OPR p.13 Ambush, 3.6.0 p.15 Scout, 3.6 Reinforcement, Fanatic, Vanguard): every base placed stays over
/// each listed unit's Over from every base of that unit on the table, measured base to base (p.5); the units are the ones the rule counts
/// (Scout: non-Scout enemy units), and Over may differ by unit (Repel Ambushers: 12"). A placed base within IgnoredWithin of a base of an
/// IgnoredNear model ignores every distance (Ambush Beacon: "may ignore distance restrictions from enemies if they are deployed within 6” of this
/// model"). The client draws the area with Tabletop.Geometry.Clearance.Outline and marks a ghost by Clearance.Breaks.
/// </summary>
public sealed record KeepAway(IReadOnlyList<AwayFrom> Units, IReadOnlyList<Guid>? IgnoredNear = null, double? IgnoredWithin = null);
/// <summary>One unit a placement keeps away from, and how far: over Over inches.</summary>
public sealed record AwayFrom(Guid UnitId, double Over);
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
/// <summary>Continues: a stop, not the whole move — after the window that follows the segment the move goes on with what is left (MoveRegion.Stoppable).</summary>
public sealed record Destinations(IReadOnlyList<ModelRelocation> Moves, bool Continues = false) : AskAnswer;
/// <summary>Tokens paid per model. A model absent from Counts paid nothing; an empty map says what Passed says.</summary>
public sealed record TokensSpent(IReadOnlyDictionary<Guid, int> Counts) : AskAnswer;
public sealed record Confirmed : AskAnswer;
/// <summary>A real answer, logged; only legal when the Ask allows it.</summary>
public sealed record Passed : AskAnswer;
