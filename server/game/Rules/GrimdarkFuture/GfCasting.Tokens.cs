using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfCasting
{
    /// <summary>
    /// p.13's caps, grouped by the team that owns them: every eligible model keyed to the tokens it holds, which is one team's slice of
    /// TokenSpend.MaxByModel, and whose keys are that team's own models for the spend step's ownership check. Called from spend-tokens,
    /// so the caster's entry is already net of the spell's cost.
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, int>> SpendCapacity(GameState state, Guid casterModel) =>
        EligibleSpenders(state, casterModel).GroupBy(m => Teams.Of(state, m.UnitId)).Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!.Value, g => (IReadOnlyDictionary<Guid, int>)g.ToDictionary(m => m.Id, m => m.SpellTokens));

    /// <summary>
    /// The per-model eligibility test itself, the caster first and the rest nearest the caster's unit next — an order the log and the Ask
    /// list their models in, and nothing more, since the player names its own payers. The caster's own model is an entry whenever it
    /// still holds a token: p.13 does not exclude it, and it is admitted without measurement, being a model of the caster's own unit.
    /// That is the one exception to a test positional on both ends, and it is deliberate: an occupant has no line of sight out (p.15),
    /// so for a caster inside a container `mine` is empty and no other model's tokens reach the roll, while its own still do. A model
    /// inside a container never spends on a cast outside.
    /// 3.5.1 p.13 measures to any model of the caster's unit with the spender in line of sight of it ("within 18” in line of sight of the
    /// caster’s unit"); 3.6.0 p.13 to the caster model alone, with no sight ("Models within 18” of the Caster"): GfEdition.SpendersNeedSight,
    /// SpendersMeasuredToCasterModel. The anchor is still a living model on the table, so a caster inside a container is reached by nobody.
    /// </summary>
    public static IReadOnlyList<ModelInPlay> EligibleSpenders(GameState state, Guid casterModel)
    {
        var self = state.Projection.Models[casterModel];
        var edition = GfEdition.Of(state);
        var mine = Lookup.LivingModels(state, self.UnitId).Where(c => !edition.SpendersMeasuredToCasterModel || c == casterModel).ToList();
        var spenders = new List<ModelInPlay>();
        if (self.SpellTokens > 0) spenders.Add(self);
        var sights = new Dictionary<Guid, Sight>();                                                                // one per spending unit, not per pair
        Sight From(Guid unit) => sights.TryGetValue(unit, out var s) ? s : sights[unit] = Sight.From(state, unit);
        // A model holding tokens that is no caster is a Spell Accumulator: it spends only for a friendly caster of another unit in its reach (GfSpellSupport).
        var accumulators = GfSpellSupport.Accumulators(state, casterModel).Select(m => m.Id).ToHashSet();
        spenders.AddRange(state.Projection.Models.Values.Where(m => m.Id != casterModel && m.Location is OnTable && m.SpellTokens > 0
                && (GfSpellSupport.CasterValue(state, m.Id) is not null ? mine.Any(c => Battlefield.Distance(state, c, m.Id) <= GfNames.CastRange && (!edition.SpendersNeedSight || From(m.UnitId).CanSee(m.Id, c))) : accumulators.Contains(m.Id)))
            .OrderBy(m => mine.Min(c => Battlefield.Distance(state, c, m.Id))));
        return spenders;
    }

    /// <summary>remove_spell_tokens: n tokens off these models, each emptied in turn from the one holding the most (a tie in the order given), to zero.</summary>
    internal static IReadOnlyList<StateChange> TakeTokens(GameState state, IEnumerable<Guid> models, int n)
    {
        var changes = new List<StateChange>();
        foreach (var m in models.Select(id => state.Projection.Models[id]).Where(m => m.SpellTokens > 0).OrderByDescending(m => m.SpellTokens))
        {
            if (n <= 0) break;
            var take = Math.Min(n, m.SpellTokens);
            changes.Add(new SpellTokensSet(m.Id, m.SpellTokens - take));
            n -= take;
        }
        return changes;
    }

    /// <summary>The piece a spell-test frame's parent picked for it: the casting's target at its `target_index`, or the pick-rule's at its `effect_index`.</summary>
    static Guid? PickedPiece(StepContext ctx, FrameView parent)
    {
        var picked = parent.Frame.Sequence == GfNames.PickRuleSeq ? GfPick.PickedTargets(ctx.State, parent) : GfAnswers.Pieces(ctx.State, parent, "targets", "spell_targets");
        var index = parent.Value(parent.Frame.Sequence == GfNames.PickRuleSeq ? "effect_index" : "target_index");
        return index < picked.Count ? picked[index] : null;
    }
}
