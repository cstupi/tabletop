namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>Every Grimdark Future name the C# half knows. The data half (Content/grimdark-future-core.properties.json) declares the same names.</summary>
public static class GfNames
{
    public const string Quality = "Quality", Defense = "Defense", Range = "Range", Attacks = "Attacks";
    public const string Hold = "Hold", Advance = "Advance", Rush = "Rush", Charge = "Charge";
    public const string Hit = "hit", DefenseRoll = "defense", Morale = "morale", Casting = "casting", RollOff = "roll-off";
    public const string Shaken = "shaken", Fatigued = "fatigued", Moved = "moved";
    public const string BeforeAttacking = "before-attacking";
    public const string HoldSeq = "hold", AdvanceSeq = "advance", RushSeq = "rush", ChargeSeq = "charge", Shooting = "shooting", Melee = "melee";
    public const string AttackSequence = "attack-sequence", ResolveHits = "resolve-hits", DealWounds = "deal-wounds", MoraleTest = "morale-test";
    public const string DangerousTerrain = "dangerous-terrain", CastingSeq = "casting", RollOffSeq = "roll-off";
    /// <summary>The roll-off's two step names. The scheduler reads the order off the step_completed named by the second.</summary>
    public const string RollOffRollStep = "roll-off", RollOffOrderStep = "order";
    public const string TransportDestroyedSeq = "transport-destroyed";                                                  // p.15: a wrecked container disgorges (Task 19)
    public const double MeleeHorizontal = 2, MeleeVertical = 4, ChargeCloseIn = 3, Consolidate = 3, FallBack = 1, Separation = 1, CastRange = 18, DifficultCap = 6;
    /// <summary>p.15 and p.21: a unit leaving a container "must stay fully within 6" of it".</summary>
    public const double ExitWithin = 6;
    public const int TokenCap = 6;

    // The scheduler plan's names (its Task 2 reads them from here; one names class).
    public const string Deployment = "deployment", Scouts = "scouts";
    public const string Rounds = "rounds";                                                                              // = SchedulerBase.RoundsPhase (scheduler plan Task 5); switch to the reference when it lands
    public const string Deploy = "Deploy";                                                                              // Activation.Action for a deployment answer
    public const string Scout = "Scout", Ambush = "Ambush", Immobile = "Immobile", Aircraft = "Aircraft", Caster = "Caster", Tough = "Tough", Hero = "Hero";
    public const string Transport = "Transport";                                                                        // p.15; its (X) is carried and never read — game flow: capacity is not enforced
    public const string Takedown = "Takedown";                                                                          // p.14; a weapon property, read by GfAttack.IsTakedownGroup
    public const string SetAside = "set-aside";                                                                         // menu-written condition on a shelved unit
    public const double AmbushDistance = 9;
}
