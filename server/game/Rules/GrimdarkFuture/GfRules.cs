namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Only OPR's GameRules is built (architecture.md → Rules as data), one instance per edition (V351, V360; GfEdition). Partial: identity here; the scheduler plan's members in
/// GfRules.Scheduling.cs and GfRules.Deployment.cs, the interpreter plan's in GfRules.Sequences.cs, dispatch's answer judge in
/// GfRules.Moves.cs. RulesId, V351, V360, Instance,
/// Edition and Id are declared once, here.
/// </summary>
public sealed partial class GrimdarkFutureRules : IGameRules
{
    /// <summary>The 3.5.1 id, which every game created before 3.6.0 holds.</summary>
    public const string RulesId = Tabletop.Content.RuleSets.Gf351;
    public static GrimdarkFutureRules V351 { get; } = new(GfEdition.V351);
    public static GrimdarkFutureRules V360 { get; } = new(GfEdition.V360);
    public static GrimdarkFutureRules Instance => V351;

    private GrimdarkFutureRules(GfEdition edition)
    {
        Edition = edition;
        _sequences = new(BuildSequences);
    }

    public GfEdition Edition { get; }
    public string Id => Edition.Id;
}
