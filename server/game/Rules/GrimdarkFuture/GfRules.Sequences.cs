using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>The interpreter plan's partial of GrimdarkFutureRules (identity is in the skeleton's GfRules.cs; the scheduler's hooks in GfRules.Scheduling.cs and GfRules.Deployment.cs; the answer judge in GfRules.Moves.cs). The sequence catalog fills in over Tasks 12–17.</summary>
public sealed partial class GrimdarkFutureRules
{
    /// <summary>A flagged interpretation, not a silent guess (action sequences → Rulings): the scope is the activation, so a caster that failed a spell may try it again in a later activation.</summary>
    public const string OneTryPerSpellScope = "activation";

    private readonly Lazy<IReadOnlyDictionary<string, Sequence>> _sequences;

    /// <summary>This edition's sequence catalog, built once per instance on first use.</summary>
    private IReadOnlyDictionary<string, Sequence> BuildSequences() => new Sequence[]
    {
        GfRolls.RollOffSequence(),
        GfAttack.DealWounds(),
        GfAttack.AttackSequence(),
        GfAttack.ResolveHits(),
        GfShooting.Shooting(),
        GfMoves.Hold(),
        GfMoves.Advance(),
        GfMoves.Rush(),
        GfMoves.DangerousTerrain(),
        GfMoves.DangerousTerrain36(),
        GfMoves.Idle(),
        GfMoves.Reposition(),
        GfMorale.MoraleTest(Edition),
        GfMelee.Charge(),
        GfMelee.Melee(Edition),
        GfCasting.Casting(),
        GfCasting.SpellHits(),
        GfCasting.SpellTest(),
        GfPick.PickRule(),
        GfChoice.ChooseEffect(),
        GfActivationEnd.Window(),
        GfActivationEnd.Coordinate(),
        GfActivationEnd.AfterDeploy(),
        GfAmbush.AmbushRedeploy(),
        GfRuleHits.RuleHits(),
        GfRuleHits.RuleWounds(),
        GfMarkers.Rally(),
        GfContainers.TransportDestroyed(Edition),
        GfReserves.PlaceReserve(),
    }.ToDictionary(s => s.Name);

    public Sequence Sequence(string name) => _sequences.Value.TryGetValue(name, out var s) ? s : throw new UnknownSequenceException(name);
    public string? ActionSequence(string actionName) => actionName switch
    {
        GfNames.Hold => GfNames.HoldSeq, GfNames.Advance => GfNames.AdvanceSeq, GfNames.Rush => GfNames.RushSeq, GfNames.Charge => GfNames.ChargeSeq, ActivationOption.Idle => GfNames.IdleSeq,
        _ => null,   // an out-of-set action accepted under warn/silent: recorded, no sequence
    };
    public RollSpec RollSpec(string roll) => GfRolls.Spec(roll);
    public string HitRoll => GfNames.Hit;
    public IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame) => GfHooks.ReactionsAt(state, properties, point, frame);
    public IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, TurnContext turn) => GfActivationEnd.Sequences(state, properties, turn.Activation);
    public IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round) => round == 0 ? [] : GfHooks.OnRoundStarted(state, properties);
    public IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, TurnContext turn) => GfHooks.OnTurnEnded(state, properties, turn);
    public IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeam, IReadOnlyList<Guid> reactingTeams) => null;
}
