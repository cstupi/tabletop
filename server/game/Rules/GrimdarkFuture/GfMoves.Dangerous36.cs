using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// 3.6.0 p.12: "If any model in a unit moves into or through Dangerous terrain, or activates in it, then the entire unit must take a Dangerous
/// Terrain Test. To take a Dangerous Terrain Test, roll as many dice as the number of wounds it would take to fully destroy the unit, and for each
/// result is 1 the unit takes one wound." Read when GfEdition.UnitDangerousTest; 3.5.1 keeps p.11's per-model test (DangerousTerrain).
/// </summary>
public static partial class GfMoves
{
    public static Sequence DangerousTerrain36() => new(GfNames.DangerousTerrain36, [new UnitDangerousTestStep(), new DangerousDealStep()]);

    /// <summary>The edition's dangerous-terrain sequence: the unit's under 3.6.0 p.12, the per-model one under 3.5.1 p.11.</summary>
    public static string DangerousSeq(GameState state) => GfEdition.Of(state).UnitDangerousTest ? GfNames.DangerousTerrain36 : GfNames.DangerousTerrain;

    /// <summary>3.6.0 p.12 "the number of wounds it would take to fully destroy the unit": each model still in play, its Tough (or 1) less the wounds it has taken.</summary>
    public static int WoundsToDestroy(GameState state, Guid unit) =>
        Lookup.AliveModels(state, unit).Sum(m => Math.Max(0, (Lookup.ModelParameter(state, m, GfNames.Tough) ?? 1) - state.Projection.Models[m].Wounds));

    /// <summary>The dice a test owes for these affected models: 3.5.1 p.11's per-model Tough dice, or under 3.6.0 p.12 the whole unit's WoundsToDestroy when any model is affected.</summary>
    public static int OwedDice(GameState state, Guid unit, IReadOnlyCollection<Guid> models) =>
        !GfEdition.Of(state).UnitDangerousTest ? DangerousDice(state, models) : models.Count > 0 ? WoundsToDestroy(state, unit) : 0;

    /// <summary>3.6.0 p.12 is one test for the unit: the activation frame records it as Values["dangerous_tested_unit_&lt;unit&gt;"] (the unit named, since a
    /// reaction's move may run inside another unit's activation), and a later step of the same activation owes nothing.</summary>
    static string UnitTestedKey(Guid unit) => $"dangerous_tested_unit_{unit:N}";

    static bool UnitTestedThisActivation(FrameView? frame, Guid unit)
    {
        var key = UnitTestedKey(unit);
        for (var f = frame; f is not null; f = f.Parent) if (f.Value(key) > 0) return true;
        return false;
    }

    /// <summary>The affected models that owe a test: under 3.5.1 those not yet tested this activation, under 3.6.0 all of them unless the unit has been.</summary>
    static IReadOnlyList<Guid> Untested(StepContext ctx, Guid unit, IReadOnlyList<Guid> exposed) =>
        GfEdition.Of(ctx.State).UnitDangerousTest
            ? (UnitTestedThisActivation(ctx.Frame, unit) ? [] : exposed)
            : exposed.Where(m => !TestedThisActivation(ctx.Frame, m)).ToList();

    /// <summary>MarkTested, and under 3.6.0 the unit's mark when any model tested.</summary>
    static void MarkTested(GameState state, Guid unit, IDictionary<string, int> values, IReadOnlyCollection<Guid> models)
    {
        MarkTested(values, models);
        if (GfEdition.Of(state).UnitDangerousTest && models.Count > 0) values[UnitTestedKey(unit)] = 1;
    }

    /// <summary>3.6.0 p.12's test: the pushing step settled the dice in Values["dangerous_dice"] (WoundsToDestroy); the fallback is the whole unit when
    /// any model stands in it now. Threshold 2, so each 1 is a wound, which `deal` reads off the dice as they then stand (DangerousWounds).</summary>
    public sealed class UnitDangerousTestStep : IStep
    {
        public string Name => "test";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            var owed = ctx.Frame.Parent?.Value("dangerous_dice") ?? 0;
            var dice = owed > 0 ? owed : StandingInDangerous(ctx.State, unit).Count > 0 ? WoundsToDestroy(ctx.State, unit) : 0;
            if (dice == 0) return new Finish([], new Dictionary<string, int> { ["wounds"] = 0 });
            var record = ctx.Roll(new Dice.RollPlan(GfNames.DangerousTerrain, 2, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), dice);
            return new Complete([], new Dictionary<string, int> { ["wounds"] = Dice.RollOutcome.Failures(record) });
        }
    }
}
