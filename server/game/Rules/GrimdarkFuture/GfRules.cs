namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Only OPR's GameRules is built (architecture.md → Rules as data). Partial: identity here; the scheduler plan's members in
/// GfRules.Scheduling.cs and GfRules.Deployment.cs, the interpreter plan's in GfRules.Sequences.cs, dispatch's answer judge in
/// GfRules.Moves.cs. RulesId, Instance and Id
/// are declared once, here.
/// </summary>
public sealed partial class GrimdarkFutureRules : IGameRules
{
    public const string RulesId = "grimdark-future";
    public static GrimdarkFutureRules Instance { get; } = new();
    public string Id => RulesId;
}
