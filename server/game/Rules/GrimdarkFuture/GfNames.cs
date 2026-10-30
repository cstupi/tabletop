namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>Every Grimdark Future name the C# half knows. The data half (each Content/&lt;edition&gt;/core.properties.json) declares the same names.</summary>
public static class GfNames
{
    public const string Quality = "Quality", Defense = "Defense", Range = "Range", Attacks = "Attacks";
    public const string Hold = "Hold", Advance = "Advance", Rush = "Rush", Charge = "Charge";
    public const string Hit = "hit", DefenseRoll = "defense", Morale = "morale", Casting = "casting", RollOff = "roll-off";
    public const string Shaken = "shaken", Fatigued = "fatigued", Moved = "moved";
    /// <summary>A unit that moved since the round began (Entrenched, Mobile Artillery: "hasn't moved during the round"); cleared as a round starts.</summary>
    public const string MovedThisRound = "moved this round";
    public const string BeforeAttacking = "before-attacking";
    /// <summary>Army-book rules spec → the pick: the sequence a Buff, Mark, Debuff or before-attacking active pushes, its Ask's question and its steps.</summary>
    public const string PickRuleSeq = "pick-rule", RuleTarget = "rule_target", RuleRollStep = "rule-roll", RuleTargetStep = "rule-target", RuleEffectsStep = "rule-effects";
    public const string QuickShot = "Quick Shot";   // army-rule hooks: a name check on the rush sequence's shooting
    public const string ActivationStart = "activation-start", AfterShooting = "after-shooting", AfterMelee = "after-melee", AttackStart = "attack-start", RepositionSeq = "reposition";
    /// <summary>Army-book rules spec, batch 4: the end of an activation (Dash, Coordinate), the sequences it opens, and a rule's choice of effect at a point.</summary>
    public const string ActivationEnd = "activation-end", ActivationEndSeq = "activation-end", CoordinateSeq = "coordinate", ChooseEffectSeq = "choose-effect", Delay = "Delay";
    /// <summary>Army-book rules spec, batch 3: the point after a unit is deployed (Fanatic, Vanguard) and its window.</summary>
    public const string AfterDeploy = "after-deploy", AfterDeploySeq = "after-deploy";
    public const string Coordinate = "Coordinate", DelayedAction = "Delayed Action", Instinctive = "Instinctive", InstinctiveCondition = "instinctive";
    public const string InquisitorialAgent = "Inquisitorial Agent", MartialProwess = "Martial Prowess";
    /// <summary>Army-book rules spec, batch 5: hits and wounds a rule deals outside an attack, and the rules that deal them.</summary>
    public const string RuleHitsSeq = "rule-hits", RuleWoundsSeq = "rule-wounds";
    /// <summary>Army-book rules spec, batch 6: the round-start roll that may end Shaken, and the rule read off a model's ignored wounds.</summary>
    public const string RallySeq = "rally", RegenerativeStrength = "Regenerative Strength";
    /// <summary>Army-book rules spec, batch 8: the rules the grammar additions leave to the ruleset.</summary>
    public const string HeavyImpact = "Heavy Impact", NoRetreat = "No Retreat";
    public const string Deathstrike = "Deathstrike", Retaliate = "Retaliate", SelfDestruct = "Self-Destruct", Ravage = "Ravage", CrossingAttack = "Crossing Attack", Strafing = "Strafing";
    public const string HoldSeq = "hold", AdvanceSeq = "advance", RushSeq = "rush", ChargeSeq = "charge", Shooting = "shooting", Melee = "melee";
    public const string AttackSequence = "attack-sequence", ResolveHits = "resolve-hits", DealWounds = "deal-wounds", MoraleTest = "morale-test";
    /// <summary>3.6.0 p.12: the unit's Dangerous Terrain Test (GfMoves.DangerousTerrain36), beside 3.5.1 p.11's per-model one.</summary>
    public const string DangerousTerrain36 = "dangerous-terrain-36";
    public const string DangerousTerrain = "dangerous-terrain", CastingSeq = "casting", SpellHitsSeq = "spell-hits", RollOffSeq = "roll-off";
    /// <summary>The roll-off's two step names. The scheduler reads the order off the step_completed named by the second.</summary>
    public const string RollOffRollStep = "roll-off", RollOffOrderStep = "order";
    public const string CounterAttack = "Counter-Attack", Unwieldy = "Unwieldy";
    public const string ChargeRangeKey = "charge_range";   // hundredths of an inch, on the charge frame's charge-target step
    public const string IdleSeq = "idle", SpellTestSeq = "spell-test";                                                // p.10 idle activation; spell library take_test
    public const string TransportDestroyedSeq = "transport-destroyed";                                                  // p.15: a wrecked container disgorges (Task 19)
    public const double MeleeHorizontal = 2, MeleeVertical = 4, ChargeCloseIn = 3, Consolidate = 3, FallBack = 1, Separation = 1, CastRange = 18, DifficultCap = 6;
    /// <summary>3.6.0 p.12 Unstable: "moves over 6” when moving into or through unstable terrain".</summary>
    public const double UnstableDistance = 6;
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
    /// <summary>p.7 UNIT COHERENCY (GfMoves.CoherencyBreach): a move answer that takes a unit in coherency out of it.</summary>
    public const string MoveCoherencyRule = "move_coherency";
    /// <summary>p.7: "within 1” of at least one other model, and ... within 9” of all other models"; p.11: "a coherency of 3” is allowed to models at different elevation".</summary>
    public const double CoherencyChain = 1, CoherencyElevated = 3, CoherencySpread = 9;
    /// <summary>p.9 Charge moves (GfMoves.ChargeContactBreach): no charging model ended in base contact with the target unit though a path reached it.</summary>
    public const string ChargeContactRule = "charge_contact";
    /// <summary>
    /// Owner ruling: "charging base to base is TOO strict and requires literally overlapping. It should have a small grace window, and if it had the
    /// movement to make the charge then it should be accepted." A charging model whose base ends this far or less from a target model counts as
    /// reaching it and is drawn flush into base contact (GfMoves.ChargeLanding; MoveRegion.ContactGrace, which the clients' landing reads). Half an
    /// inch: wider than a drop a player can judge by eye on a 32 mm base, and half of p.7's 1", the distance no other move may close to an enemy.
    /// </summary>
    public const double ChargeContactGrace = 0.5;
    /// <summary>IGameRules.PlacementBreach rule name for a deployment placement outside the team's zone (p.6), or beyond a Scout's 12" of it (p.14).</summary>
    public const string DeployZoneRule = "deploy_zone";
    /// <summary>IGameRules.PlacementBreach rule name for an Ambusher placed within 9" of an enemy unit (p.13: "over 9” away from enemy units").</summary>
    public const string AmbushDistanceRule = "ambush_distance";
    /// <summary>IGameRules.AnswerBreach rule name for a deploy answer naming a unit already inside a Transport (p.15: it deploys with the Transport).</summary>
    public const string DeployInsideRule = "deploy_inside";
    /// <summary>IGameRules.AnswerBreach rule name for a pass to the activate Ask (p.7: "Players must activate one unit that has not been activated yet").</summary>
    public const string ActivationRequiredRule = "activation_required";
    public const double ScoutReach = 12;
    public const int TokenCap = 6;

    // The scheduler plan's names (its Task 2 reads them from here; one names class).
    public const string Deployment = "deployment", Scouts = "scouts";
    public const string Rounds = Scheduler.SchedulerBase.RoundsPhase;
    public const string Deploy = "Deploy";                                                                              // Activation.Action for a deployment answer
    public const string Scout = "Scout", Ambush = "Ambush", Immobile = "Immobile", Aircraft = "Aircraft", Caster = "Caster", Tough = "Tough", Hero = "Hero";
    /// <summary>p.13 Flying and p.14 Strider: the movement rules a move reads from the unit's live set (GfMoves.TerrainFor).</summary>
    public const string Flying = "Flying", Strider = "Strider";
    /// <summary>p.36 Relic Wargear, Mystic Banners: "The Hero and its unit may ignore all terrain effects." Read with Flying and Strider (GfMoves.TerrainFor).</summary>
    public const string EternalFlame = "Banner of Eternal Flame";
    /// <summary>The Values key prefix a step writes for a granted Flying it used and kept (GfMoves.ConsumeKeepingFlying): "flying_held_{unit:N}_{grant:N}".</summary>
    public const string FlyingHeldPrefix = "flying_held_";
    public const string Artillery = "Artillery";                                                                        // 3.5.1 p.13: "May only use Hold actions" (GfEdition.ArtilleryHoldOnly); not in the core catalog, so read off a unit's own properties
    public const string Transport = "Transport";                                                                        // p.15; its (X) is read for a passenger put in before deployment (GfRules.Facts Fits); embarking in play does not check it
    public const string Indirect = "Indirect";                                                                          // p.14; a weapon property, read by GfAttack for sight and cover
    public const string Takedown = "Takedown";                                                                          // p.14; a weapon property, read by GfAttack.IsTakedownGroup
    public const string SetAside = "set-aside";                                                                         // menu-written condition on a shelved unit
    public const double AmbushDistance = 9;
}
