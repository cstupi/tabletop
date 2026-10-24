using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>OPR's dice: 1D6 against a target number, never clamped. Thresholds are functions of the live set; fatigue and cover are read here.</summary>
public static class GfRolls
{
    /// <summary>
    /// p.5: *"a roll of 6 always counts as a success and a roll of 1 always counts as a fail, regardless of how much it is being modified by"*.
    /// The page is General Principles and names no roll, so every OPR roll carries it: hit, defense, morale, casting and a property's own
    /// roll. The roll-off has no threshold, so the faces never judge it. A roll the book says succeeds only on an unmodified face (p.9's
    /// fatigue) is a RequiredRaw plan, which the Roller judges on the raw face alone.
    /// </summary>
    static readonly RollConvention D6 = new(6, null, AlwaysFails: 1, AlwaysSucceeds: 6);
    static readonly StatRef Quality = new(StatLevel.Unit, GfNames.Quality), Defense = new(StatLevel.Unit, GfNames.Defense);
    static readonly StatRef ModelQuality = new(StatLevel.Model, GfNames.Quality), ModelDefense = new(StatLevel.Model, GfNames.Defense);

    /// <summary>
    /// The default threshold for each roll, which is the unit's number. Three of them are overridden per plan where the rulebook
    /// makes the target number a model's: HitQuality for the attacking model (p.4), DefensePlan for a lone joined hero (p.14) and
    /// MoralePlan for a hero testing on the unit's behalf (p.14). A caller that names no threshold gets what is here.
    /// </summary>
    public static RollSpec Spec(string roll) => roll switch
    {
        GfNames.Hit => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Quality)),
        GfNames.DefenseRoll => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Defense)),
        GfNames.Morale => new(roll, D6, (live, side) => StatReader.TargetNumber(live, side, Quality)),
        GfNames.Casting => new(roll, D6, (_, _) => new TargetRead(4, null)),
        GfNames.RollOff => new(roll, D6, (_, _) => null),
        _ => new(roll, D6, (_, _) => null),   // a property's own roll (Regeneration, Impact, Fearless) carries its threshold in the effect
    };

    /// <summary>p.9: after attacking in melee for the first time in a round, a unit hits only on unmodified 6s until the round ends. p.10: a Shaken unit "may strike back counting as fatigued".</summary>
    public static bool Fatigued(StepContext ctx, LiveSet live, AttackKind kind) =>
        kind == AttackKind.Melee && ctx.Projection.Units[live.Interaction.Attacker.UnitId].Conditions is var c && (c.Contains(GfNames.Fatigued) || c.Contains(GfNames.Shaken));

    /// <summary>
    /// The Quality value one attacking model tests against. p.4: a Quality test succeeds on "the model's Quality value or higher",
    /// so the value is the model's, and a slot that declares its own is a joined hero (p.14). Every other slot
    /// falls back to the unit's, so an ordinary squad has one value. Effects on the roll's target number are already folded in,
    /// which is what puts Reliable's 2+ over a hero's own 3+.
    ///
    /// p.9's fatigue replaces the value rather than modifying it, and it is the whole unit's condition, so a fatigued melee has one
    /// value for every model and one pool however many Qualities the unit holds.
    /// </summary>
    public static int HitQuality(StepContext ctx, LiveSet live, AttackKind kind, Guid modelId) =>
        Fatigued(ctx, live, kind) ? 6 : StatReader.TargetNumber(live, Side.Attacker, ModelQuality, modelId).Value;

    /// <summary>
    /// One pooled hit roll for one Quality value. `determine-attacks` splits the group's attacks by `HitQuality` and `roll-to-hit`
    /// asks for a plan per distinct value, which is p.4's *"we recommend having dice of multiple colors ... and then roll them all
    /// at once"* for the case a joined hero creates: a hero and its squad carrying one weapon type that `fire-groups`
    /// merged into a single group.
    ///
    /// A fatigued melee is a flat 6+ with no modifiers and `RequiredRaw = 6`, because p.9 says *"only hit on unmodified rolls of 6"*.
    /// </summary>
    public static RollPlan HitPlan(StepContext ctx, LiveSet live, AttackKind kind, int quality) =>
        Fatigued(ctx, live, kind)
            ? new RollPlan(GfNames.Hit, 6, [], D6, RequiredRaw: 6, RerollRaw: new HashSet<int>())
            : ctx.Plan(live, Side.Attacker, GfNames.Hit, [], threshold: new TargetRead(quality, HitBasis(live, quality)));

    /// <summary>One pool's basis: the first attacking model, in party order, that brings the value. Peeked: determine-attacks accounted it.</summary>
    static ThresholdBasis? HitBasis(LiveSet live, int quality) =>
        live.Interaction.Attacker.ModelIds.Select(m => StatReader.Peek(live, Side.Attacker, ModelQuality, m)).FirstOrDefault(r => r.Value == quality)?.Basis;

    /// <summary>
    /// p.11's *"If the majority of models in a unit are fully inside a piece of cover terrain or behind a sight blocker"*: one
    /// majority over the models that pass either test, so a unit with one model in a wood and another screened by a third unit's
    /// base is in cover at three models. A majority is strictly more than half, so five of ten is not one.
    /// The terrain test follows the book's wording for the unit's size: whole base inside ("fully inside") for a
    /// multi-model unit, base centre inside (standing for "mostly inside") for a single-model unit, sized by starting size so a
    /// squad reduced to one model is still judged as a squad. The sight test is the same for both sizes: every attacking model
    /// with a clear line to the model has a sight blocker along it, or none sees it at all (`Battlefield.ModelsBehindSightBlockers`).
    /// p.14 Indirect *"ignores cover from sight obstructions"*, which is the sight test only: `ignoresSightObstructions` drops it and
    /// the terrain test stands. Blast's *"Ignores cover"* (p.13) is the whole modifier and is suppressed in the roll instead.
    /// The fraction is a ruleset number, so it is here and `Battlefield` only lists.
    /// </summary>
    public static bool InCover(GameState state, Guid targetUnit, IReadOnlyList<Guid> attackerModels, bool ignoresSightObstructions) =>
        Cover(state, targetUnit, attackerModels, ignoresSightObstructions).Majority;

    /// <summary>InCover's count: the living models that pass either test (only the terrain test once it is a majority, or under
    /// Indirect), of the unit's living models. The log's cover verdict carries both numbers.</summary>
    /// <param name="takedown">p.14 Takedown's picked model, "resolved as if it was a unit of [1]": the count is over that model alone, judged as a
    /// single-model unit, and its squad-mates "don't block line of sight or provide cover to the target model" (the sight test never counts them).</param>
    public static CoverCount Cover(GameState state, Guid targetUnit, IReadOnlyList<Guid> attackerModels, bool ignoresSightObstructions, Guid? takedown = null)
    {
        var living = Lookup.LivingModels(state, targetUnit);
        if (takedown is { } picked) living = living.Contains(picked) ? [picked] : [];
        var models = living.Count;
        if (models == 0) return new CoverCount(0, 0);
        var wholeBase = takedown is null && GfHooks.StartingSize(state, targetUnit) > 1;
        var covered = Battlefield.ModelsInCoverTerrain(state, targetUnit, wholeBase).Where(living.Contains).ToHashSet();
        if (covered.Count * 2 > models || ignoresSightObstructions) return new CoverCount(covered.Count, models);
        covered.UnionWith(Battlefield.ModelsBehindSightBlockers(state, targetUnit, attackerModels).Where(living.Contains));
        return new CoverCount(covered.Count, models);
    }

    /// <summary>Models of Of in cover; a majority is strictly more than half.</summary>
    public sealed record CoverCount(int Models, int Of)
    {
        public bool Majority => Models * 2 > Of;
    }

    /// <summary>
    /// p.11: +1 to Defense against shooting when in cover. Blast suppresses it (pass one); Indirect's half is read in `InCover`.
    ///
    /// p.14's Hero clause is the other half of the threshold. A joined hero *"must use the unit's Defense until all other models have
    /// been killed"*, so the unit's value stands while anything else lives, and once the hero is alone its own slot Defense is read.
    /// `defenderModels` is that clause's *all other models*, and it is the count `fire-groups` settled before any group fired rather
    /// than a live one: p.8 fires a unit's weapons simultaneously, so a second group must not find the hero alone that
    /// the first group left it. A model without a slot Defense reads the unit's through the same call, so nothing branches on `Hero`.
    /// `takedown` is p.14 Takedown's picked model, "resolved as if it was a unit of [1]": it stands alone, so its own Defense is read.
    /// </summary>
    /// `verdict` is the unit's cover verdict for the log (RollRecord.Cover); when the +1 it grants is suppressed (Blast), the verdict
    /// names the suppressor as IgnoredBy.
    public static RollPlan DefensePlan(StepContext ctx, LiveSet live, bool inCover, int defenderModels, CoverVerdict? verdict = null, Guid? takedown = null)
    {
        var shooting = live.Interaction.Attack == AttackKind.Shooting;
        if (shooting && inCover && verdict is { IgnoredBy: null } && live.SuppressedModifiers.Contains(TargetModifier.Cover))
            verdict = verdict with { IgnoredBy = live.Suppressors(TargetModifier.Cover).FirstOrDefault() };
        var living = Lookup.LivingModels(ctx.State, live.Interaction.Defender!.UnitId);
        var alone = takedown ?? (defenderModels == 1 && living.Count == 1 ? living[0] : (Guid?)null);
        if (alone is { } hero && ctx.Projection.Models[hero].Slot.Properties.Any(p => p.Name == GfNames.Hero) && ctx.Projection.Models[hero].Slot.Stats.ContainsKey(GfNames.Defense))
            ctx.Applied.Add(GfNames.Hero);
        return ctx.Plan(live, Side.Defender, GfNames.DefenseRoll,
            shooting && inCover ? [new RollModifier("cover", 1)] : [],
            threshold: alone is { } id ? StatReader.TargetNumber(live, Side.Defender, ModelDefense, id) : null, cover: shooting ? verdict : null);
    }

    /// <summary>
    /// p.10: one Quality test. p.14 lets a joined hero *"take morale tests on behalf of the unit"*, and that clause is resolved
    /// always-when-it-helps, so the threshold is the best of the unit's Quality and every living Hero's own and no Ask
    /// is opened. Nothing else of the test moves: p.14's *"even if the hero is the last model remaining, it takes morale tests as the
    /// unit"* keeps the starting-size comparison that decides Shaken from Rout on the unit.
    /// </summary>
    public static RollPlan MoralePlan(StepContext ctx, LiveSet live)
    {
        var unit = live.Interaction.Attacker.UnitId;
        var own = StatReader.TargetNumber(live, Side.Attacker, Quality);
        var heroes = Lookup.LivingModels(ctx.State, unit)
            .Where(m => ctx.Projection.Models[m].Slot.Properties.Any(p => p.Name == GfNames.Hero))
            .Select(m => StatReader.TargetNumber(live, Side.Attacker, ModelQuality, m)).ToList();
        var best = heroes.Prepend(own).MinBy(r => r.Value)!;   // the first minimum: a tie stays the unit's own
        if (best.Value < own.Value) ctx.Applied.Add(GfNames.Hero);
        return ctx.Plan(live, Side.Attacker, GfNames.Morale, [], threshold: best);
    }

    /// <summary>p.13: one die on 4+, ±1 per token spent for or against.</summary>
    /// <summary>conduit: cast from a Spell Conduit's position, which "get +1 to casting rolls when doing so" (army-book rules spec, batch 7).</summary>
    public static RollPlan CastingPlan(StepContext ctx, LiveSet live, int netTokens, bool conduit = false) =>
        ctx.Plan(live, Side.Attacker, GfNames.Casting, [.. netTokens == 0 ? [] : new[] { new RollModifier("tokens", netTokens) }, .. conduit ? new[] { new RollModifier(GfSpellSupport.SpellConduit, 1) } : []],
            threshold: new TargetRead(4, null));

    /// <summary>
    /// The roll-off the scheduler starts (scheduler → Deployment; p.6, p.50). Two steps, run in one go for players. Under the wait setting
    /// (development, the rules suite) the boundary between them is a confirm gate that keeps the frame on the stack while SetDice fixes the
    /// dice, and dice_edited reaches back into a RollRecord and never rewrites a step_completed's Values — so the ranking is derived by
    /// the second step rather than written by the step that rolled.
    /// </summary>
    public static Sequence RollOffSequence() => new(GfNames.RollOffSeq, [new RollOffStep(), new RollOffOrderStep()]);

    /// <summary>
    /// One die per team in ordinal order, no threshold; teams whose results are still tied roll again among themselves — roll k's dice
    /// belong, in ordinal order, to the teams tied after rolls 0..k-1 — until every team is ordered, because free-for-all activates
    /// "in descending order" and needs the whole order, not a winner.
    /// </summary>
    sealed class RollOffStep : IStep
    {
        public string Name => GfNames.RollOffRollStep;
        public StepOutcome Run(StepContext ctx)
        {
            var teams = ctx.State.Info.Teams.OrderBy(t => t.Ordinal).ToList();
            var plan = new RollPlan(GfNames.RollOff, null, [], D6, null, new HashSet<int>());
            var keys = teams.ToDictionary(t => t.Id, _ => new List<int>());
            for (var round = 0; round < 100; round++)
            {
                var tied = Tied(teams, keys);
                if (tied.Count == 0) break;
                var record = ctx.Roll(plan, tied.Count);
                for (var i = 0; i < tied.Count; i++) keys[tied[i].Id].Add(record.Dice[i].Raw);
            }
            return new Complete([]);
        }
    }

    /// <summary>
    /// Ranks the teams from the dice the previous step recorded, replaying the same tie loop over them instead of rolling, so a die
    /// edited at the gate is the one that counts. It never rolls: a residual tie an edit created falls to team ordinal, which
    /// is the tolerance Scheduler.RollOff.Order already has for a hand-edited log.
    /// Values: winner_ordinal; place_1..place_n — the team ordinals best first — which Scheduler.RollOff.Order reads into round_started.Order;
    /// and rolled_&lt;ordinal&gt;_&lt;k&gt;, each team's k-th face (k from 1; a tie's re-rolls follow), which the client shows as the roll-off's result.
    /// </summary>
    sealed class RollOffOrderStep : IStep
    {
        public string Name => GfNames.RollOffOrderStep;
        public StepOutcome Run(StepContext ctx)
        {
            var teams = ctx.State.Info.Teams.OrderBy(t => t.Ordinal).ToList();
            var keys = teams.ToDictionary(t => t.Id, _ => new List<int>());
            foreach (var record in ctx.Frame.Frame.Steps.Single(s => s.Step == GfNames.RollOffRollStep).Rolls)
            {
                var tied = Tied(teams, keys);
                for (var i = 0; i < Math.Min(tied.Count, record.Dice.Count); i++) keys[tied[i].Id].Add(record.Dice[i].Raw);
            }
            var ranked = teams.OrderByDescending(t => keys[t.Id], KeyComparer.Instance).ThenBy(t => t.Ordinal).ToList();
            var values = new Dictionary<string, int>();
            if (ranked.Count > 0) values["winner_ordinal"] = ranked[0].Ordinal;
            for (var k = 0; k < ranked.Count; k++) values[$"place_{k + 1}"] = ranked[k].Ordinal;
            foreach (var team in teams)
                for (var k = 0; k < keys[team.Id].Count; k++) values[$"rolled_{team.Ordinal}_{k + 1}"] = keys[team.Id][k];
            return new Complete([], values);
        }
    }

    /// <summary>The teams still sharing a result key with another team, in ordinal order: whose dice the next roll-off roll holds.</summary>
    static List<Team> Tied(List<Team> teams, Dictionary<Guid, List<int>> keys) =>
        teams.Where(t => teams.Any(o => o.Id != t.Id && keys[o.Id].SequenceEqual(keys[t.Id]))).ToList();

    /// <summary>Lexicographic over each team's raw results; a longer key (more tie-break rolls) ranks above its prefix.</summary>
    sealed class KeyComparer : IComparer<List<int>>
    {
        public static readonly KeyComparer Instance = new();
        public int Compare(List<int>? a, List<int>? b)
        {
            for (var i = 0; i < Math.Min(a!.Count, b!.Count); i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Count.CompareTo(b.Count);
        }
    }
}
