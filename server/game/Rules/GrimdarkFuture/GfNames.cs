namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>Every Grimdark Future name the C# half knows. The data half (Content/grimdark-future-core.properties.json) declares the same names.</summary>
public static class GfNames
{
    public const string Quality = "Quality", Defense = "Defense", Range = "Range", Attacks = "Attacks";
    public const string Hold = "Hold", Advance = "Advance", Rush = "Rush", Charge = "Charge";
    public const string Hit = "hit", DefenseRoll = "defense", Morale = "morale", Casting = "casting", RollOff = "roll-off";
    public const string Shaken = "shaken", Fatigued = "fatigued", Moved = "moved";
    public const string BeforeAttacking = "before-attacking";
    /// <summary>Army-book rules spec → the pick: the sequence a Buff, Mark or Debuff's use pushes, its Ask's question and its one step.</summary>
    public const string PickRuleSeq = "pick-rule", RuleTarget = "rule_target", RuleTargetStep = "rule-target";
    public const string QuickShot = "Quick Shot";   // army-rule hooks: a name check on the rush sequence's shooting
    public const string ActivationStart = "activation-start", AfterShooting = "after-shooting", AfterMelee = "after-melee", AttackStart = "attack-start", RepositionSeq = "reposition";
    public const string HoldSeq = "hold", AdvanceSeq = "advance", RushSeq = "rush", ChargeSeq = "charge", Shooting = "shooting", Melee = "melee";
    public const string AttackSequence = "attack-sequence", ResolveHits = "resolve-hits", DealWounds = "deal-wounds", MoraleTest = "morale-test";
    public const string DangerousTerrain = "dangerous-terrain", CastingSeq = "casting", SpellHitsSeq = "spell-hits", RollOffSeq = "roll-off";
    /// <summary>The roll-off's two step names. The scheduler reads the order off the step_completed named by the second.</summary>
    public const string RollOffRollStep = "roll-off", RollOffOrderStep = "order";
    public const string CounterAttack = "Counter-Attack", Unwieldy = "Unwieldy";
    public const string ChargeRangeKey = "charge_range";   // hundredths of an inch, on the charge frame's charge-target step
    public const string IdleSeq = "idle", SpellTestSeq = "spell-test";                                                // p.10 idle activation; spell library take_test
    public const string TransportDestroyedSeq = "transport-destroyed";                                                  // p.15: a wrecked container disgorges (Task 19)
    public const double MeleeHorizontal = 2, MeleeVertical = 4, ChargeCloseIn = 3, Consolidate = 3, FallBack = 1, Separation = 1, CastRange = 18, DifficultCap = 6;
    /// <summary>p.15 and p.21: a unit leaving a container "must stay fully within 6" of it".</summary>
    public const double ExitWithin = 6;
    /// <summary>p.13 Aircraft: "adding 30" to its total move".</summary>
    public const double AircraftMove = 30;
    /// <summary>The move step's Values key: an earlier segment of this move went in or through difficult terrain (p.11 "for that movement").</summary>
    public const string EnteredDifficultKey = "entered_difficult";
    /// <summary>IGameRules.AnswerBreach rule names (enforcement spec → The breach): p.11's cap over the unit, p.13 Aircraft's mandatory move.</summary>
    public const string DifficultCapRule = "difficult_cap", AircraftMinimumRule = "aircraft_minimum";
    /// <summary>IGameRules.AnswerBreach rule names for a move answer's geometry (p.7, p.11, p.15) and for a relocation the move steps do not read.</summary>
    public const string MoveAllowanceRule = "move_allowance", MoveImpassableRule = "move_impassable", MoveOffTableRule = "move_off_table",
        MoveExitRule = "move_exit", MoveNotOfferedRule = "move_not_offered";
    /// <summary>p.7's contact clauses (GfMoves.ContactBreach): a landing on another model's base, a path through a model, a landing within 1" of another unit.</summary>
    public const string MoveOverlapRule = "move_overlap", MoveThroughModelsRule = "move_through_models", MoveSeparationRule = "move_separation";
    /// <summary>IGameRules.PlacementBreach rule name for a deployment placement outside the team's zone (p.6), or beyond a Scout's 12" of it (p.14).</summary>
    public const string DeployZoneRule = "deploy_zone";
    /// <summary>IGameRules.PlacementBreach rule name for an Ambusher placed within 9" of an enemy unit (p.13: "over 9” away from enemy units").</summary>
    public const string AmbushDistanceRule = "ambush_distance";
    /// <summary>IGameRules.AnswerBreach rule name for a deploy answer naming a unit already inside a Transport (p.15: it deploys with the Transport).</summary>
    public const string DeployInsideRule = "deploy_inside";
    public const double ScoutReach = 12;
    public const int TokenCap = 6;

    // The scheduler plan's names (its Task 2 reads them from here; one names class).
    public const string Deployment = "deployment", Scouts = "scouts";
    public const string Rounds = Scheduler.SchedulerBase.RoundsPhase;
    public const string Deploy = "Deploy";                                                                              // Activation.Action for a deployment answer
    public const string Scout = "Scout", Ambush = "Ambush", Immobile = "Immobile", Aircraft = "Aircraft", Caster = "Caster", Tough = "Tough", Hero = "Hero";
    /// <summary>p.13 Flying and p.14 Strider: the movement rules a move reads from the unit's live set (GfMoves.TerrainFor).</summary>
    public const string Flying = "Flying", Strider = "Strider";
    /// <summary>The Values key prefix a step writes for a granted Flying it used and kept (GfMoves.ConsumeKeepingFlying): "flying_held_{unit:N}_{grant:N}".</summary>
    public const string FlyingHeldPrefix = "flying_held_";
    public const string Artillery = "Artillery";                                                                        // p.13: "May only use Hold actions"; not in the core catalog, so read off a unit's own properties
    public const string Transport = "Transport";                                                                        // p.15; its (X) is read for a passenger put in before deployment (GfRules.Facts Fits); embarking in play does not check it
    public const string Indirect = "Indirect";                                                                          // p.14; a weapon property, read by GfAttack for sight and cover
    public const string Takedown = "Takedown";                                                                          // p.14; a weapon property, read by GfAttack.IsTakedownGroup
    public const string SetAside = "set-aside";                                                                         // menu-written condition on a shelved unit
    public const double AmbushDistance = 9;
}
