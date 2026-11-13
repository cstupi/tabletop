using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Reanimation (army-book rules spec, batch 7): "When a unit where all models have this rule is activated, roll as many dice as the max. number of
/// models/wounds it could restore. For each 5+ you may restore one model/wound. Note that new models may only be restored if they can be placed in
/// coherency with non-restored models." Read: the dice are the unit's killed models plus the wounds its models in play carry; each success restores a
/// killed model first (placed by the engine within 1" of a model in play, on no base: p.7's coherency), then a wound, once the owning team chose to.
/// </summary>
public static class GfReanimation
{
    public const string Reanimation = "Reanimation";
    const int Threshold = 5;

    /// <summary>The unit's Reanimation roll as its activation starts (activation-terrain's first run): the number of 5+ results; 0 when it rolls
    /// nothing or none succeed.</summary>
    public static int Roll(StepContext ctx, Guid unit)
    {
        if (!Lookup.UnitHas(ctx.State, ctx.Properties, unit, Reanimation)) return 0;
        var inPlay = Lookup.LivingModels(ctx.State, unit);
        if (inPlay.Count == 0) return 0;
        var killed = ctx.Projection.Units[unit].Definition.Models.Count(m => ctx.Projection.Models[m.Id].Location is OnShelf);
        var dice = Math.Min(killed + inPlay.Sum(m => ctx.Projection.Models[m].Wounds), GfRuleHits.MaxDealt);
        if (dice == 0) return 0;
        var record = ctx.Roll(new Dice.RollPlan(Reanimation, Threshold, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), dice);
        ctx.Applied.Add(Reanimation);
        return Dice.RollOutcome.Successes(record);
    }

    /// <summary>"For each 5+ you may restore one model/wound": the activation-terrain repeat asks the owning team once for the roll's restores
    /// (`reanimation`, Restore or No, a pass is No), and on Restore applies them. Null when nothing can be restored.</summary>
    public static StepOutcome? Offer(StepContext ctx, Guid unit, int successes)
    {
        var (changes, models, wounds) = Restores(ctx, unit, successes);
        if (changes.Count == 0) return null;
        if (ctx.History.LastClosed(Question) is not { } closed)
        {
            var what = string.Join(" and ", new[] { (models, "model"), (wounds, "wound") }.Where(x => x.Item1 > 0).Select(x => $"{x.Item1} {x.Item2}{(x.Item1 == 1 ? "" : "s")}"));
            return new OpenAsks([ctx.Ask(Question, $"Reanimation: restore {what}?", ctx.TeamOf(unit)!.Value,
                new Options([new AskOption("restore", "Restore", [unit]), new AskOption("no", "No", [])]), allowPass: true)]);
        }
        return new Complete(closed.Single is Chosen { OptionId: "restore" } ? changes : [], new Dictionary<string, int> { ["reanimation_asked"] = 1 }, Repeat: true);
    }

    public const string Question = "reanimation";

    /// <summary>What the successes restore: a killed model first ("new models may only be restored if they can be placed in coherency with
    /// non-restored models": placed by the engine within 1" of a model in play, on no base, p.7), then a wound.</summary>
    static (IReadOnlyList<StateChange> Changes, int Models, int Wounds) Restores(StepContext ctx, Guid unit, int restore)
    {
        var inPlay = Lookup.LivingModels(ctx.State, unit);
        var killed = ctx.Projection.Units[unit].Definition.Models.Select(m => ctx.Projection.Models[m.Id]).Where(m => m.Location is OnShelf).ToList();
        var wounded = inPlay.Select(m => ctx.Projection.Models[m]).Where(m => m.Wounds > 0).ToList();
        var changes = new List<StateChange>();
        var placed = new List<PlacedModel>(ctx.Projection.Models.Values.Where(m => m.Location is OnTable).Select(m => Battlefield.Placed(ctx.State, m.Id)!.Value));
        var moves = new List<ModelRelocation>();
        var woundsBack = 0;
        foreach (var model in killed)
        {
            if (restore == 0) break;
            if (Spot(ctx.State, model.Id, inPlay, placed) is not { } at) continue;
            moves.Add(new ModelRelocation(model.Id, model.Location, new OnTable(at)));
            if (model.Wounds > 0) changes.Add(new WoundsSet(model.Id, 0));
            placed.Add(Battlefield.At(ctx.State, model.Id, at));
            restore--;
        }
        if (moves.Count > 0) changes.Insert(0, new ModelsMoved(moves));
        foreach (var model in wounded)
        {
            if (restore == 0) break;
            var back = Math.Min(restore, model.Wounds);
            changes.Add(new WoundsSet(model.Id, model.Wounds - back));
            restore -= back;
            woundsBack += back;
        }
        return (changes, moves.Count, woundsBack);
    }

    /// <summary>Within the table's edges by an inch (the frame's origin is the table's corner).</summary>
    static bool OnTable(Placement p, TableSize size) =>
        p.Position.X >= 1 && p.Position.Y >= 1 && p.Position.X <= size.Width - 1 && p.Position.Y <= size.Length - 1;

    /// <summary>A placement for a restored model within 1" of a model in play of its unit, touching no other base and on the table; null when there is none.</summary>
    static Placement? Spot(GameState state, Guid model, IReadOnlyList<Guid> unitInPlay, IReadOnlyList<PlacedModel> placed)
    {
        var size = state.Board.Table.Size;
        foreach (var anchor in unitInPlay)
        {
            var at = ((OnTable)state.Projection.Models[anchor].Location).Placement;
            for (var d = 0.5; d <= 4; d += 0.25)
                for (var a = 0; a < 360; a += 30)
                {
                    var p = new Placement(at.Position with { X = at.Position.X + d * Math.Cos(a * Math.PI / 180), Y = at.Position.Y + d * Math.Sin(a * Math.PI / 180) }, at.Yaw);
                    if (OnTable(at, size) && !OnTable(p, size)) continue;
                    var body = Battlefield.At(state, model, p);
                    var gap = Distance.EdgeToEdge(body, Battlefield.Placed(state, anchor)!.Value);
                    if (gap <= 0.01 || gap > 1) continue;
                    if (placed.Any(o => Distance.EdgeToEdge(body, o) <= 0.01)) continue;
                    return Battlefield.Settle(state, model, p);
                }
        }
        return null;
    }
}
