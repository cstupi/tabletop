using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Markers a rule places on a unit (army-book rules spec, batch 6): one grant of the rule's marker row per marker, stacking, so "for each marker" is
/// the sum of its grants. A rule with two marker rows ("for every two markers") cycles them, its second row the one that acts. The triggers are
/// here: "when it fully destroys an enemy unit" (the Frenzies, Vengeance), "at the beginning of each round" (the Growths), and Steadfast's die.
/// </summary>
public static class GfMarkers
{
    /// <summary>"Place one marker on this unit when it fully destroys an enemy unit, up to a max. of two markers."</summary>
    public static readonly string[] Frenzies = ["Defensive Frenzy", "Piercing Frenzy", "Precision Frenzy"];
    public const int FrenzyCap = 2;
    /// <summary>"Place one marker on it at the beginning of each round if it's on the table and isn't Shaken, up to a max. of four markers."</summary>
    public static readonly string[] Growths = ["Fortified Growth", "Defensive Growth", "Piercing Growth", "Precision Growth"];
    public const int GrowthCap = 4;
    public const string Vengeance = "Vengeance";
    /// <summary>The most markers of one rule a unit holds, each a grant: X comes from imported army data, and the shipped books' largest is a Tag's or
    /// Target's X of 1 and Vengeance's one per model of a unit (20 at most), so 20 bounds every rule while no imported X can grow the grants without end.</summary>
    public const int MaxHeld = 20;
    /// <summary>"If a unit where all models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being Shaken."</summary>
    public static readonly string[] RoundStartRallies = ["Steadfast", "Battleborn", "Honor Code"];

    /// <summary>The rule's marker and token rows, the dice row's children included ("roll one die, on a 4+ place a marker").</summary>
    public static IReadOnlyList<Effect> Rows(CatalogEntry entry) =>
        entry.Effects.SelectMany(e => e.Kind == EffectKind.Roll ? e.Children : [e]).Where(e => e.Duration is EffectDuration.Marker or EffectDuration.SpentMarker or EffectDuration.OneMarker).ToList();

    /// <summary>n more markers of the rule on the unit, up to the cap; the marker rows cycle from the count already held.</summary>
    public static IReadOnlyList<StateChange> Place(CatalogEntry entry, UnitInPlay unit, Guid caster, int n, int? cap = null)
    {
        var rows = Rows(entry);
        if (rows.Count == 0) return [];
        var have = unit.Grants.Count(g => g.EntryName == entry.Name);
        var limit = Math.Min(cap ?? MaxHeld, MaxHeld);
        var add = Math.Max(0, Math.Min(n, limit - have));
        return Enumerable.Range(0, add).Select(i => (StateChange)new GrantAdded(unit.Id, new Grant(Guid.CreateVersion7(), rows[(have + i) % rows.Count].Id, entry.Name, caster, 1))).ToList();
    }

    /// <summary>The Growths' markers riding round_started: each unit on the table and not Shaken that holds one.</summary>
    public static IReadOnlyList<StateChange> AtRoundStart(GameState state, PropertySet props) =>
        state.Projection.Units.Values.Where(u => !u.Conditions.Contains(GfNames.Shaken) && TeamUnits.AnyOnTable(state, u))
            .SelectMany(u => Growths.Where(g => props.Entry(g) is { Played: true } && Lookup.UnitHas(state, props, u.Id, g)).SelectMany(g => Place(props.Entry(g)!, u, u.Id, 1, GrowthCap)))
            .ToList();

    /// <summary>deal-wounds' step after the wounds: an attack that left its target with no model in play fully destroyed it. The attacker's Frenzies each
    /// place a marker; the target's Vengeance places one marker per model that carried it at the beginning of the game on the attacker, for the target's side.</summary>
    public sealed class DestroyedStep : IStep
    {
        public string Name => "destroyed";
        public StepOutcome Run(StepContext ctx)
        {
            if (ctx.Frame.Context.ActorUnitId is not { } attacker || ctx.Frame.Context.TargetUnitIds is not [var target, ..]) return new Complete([]);
            if (!ctx.Frame.Values.Keys.Any(k => k.StartsWith("killed_", StringComparison.Ordinal)) || Lookup.AliveModels(ctx.State, target).Count > 0
                || ctx.TeamOf(attacker) == ctx.TeamOf(target) || !ctx.Projection.Units.TryGetValue(attacker, out var by)) return new Complete([]);
            var changes = new List<StateChange>();
            foreach (var rule in Frenzies.Where(r => ctx.Properties.Entry(r) is { Played: true } && Lookup.UnitHas(ctx.State, ctx.Properties, attacker, r)))
            {
                var placed = Place(ctx.Properties.Entry(rule)!, by, attacker, 1, FrenzyCap);
                if (placed.Count > 0) ctx.Applied.Add(rule);
                changes.AddRange(placed);
            }
            var avengers = ctx.Projection.Units[target].Definition.Models.Count(m => m.Properties.Any(p => p.Name == Vengeance))
                + (ctx.Projection.Units[target].Definition.Properties.Any(p => p.Name == Vengeance) ? ctx.Projection.Units[target].Definition.Models.Count : 0);
            if (avengers > 0 && ctx.Properties.Entry(Vengeance) is { Played: true } vengeance)
            {
                changes.AddRange(Place(vengeance, by, target, avengers));
                ctx.Applied.Add(Vengeance);
            }
            return new Complete(changes);
        }
    }

    /// <summary>`rally`: the round-start roll of a Shaken unit holding Steadfast, Battleborn or Honor Code; a 4+ ends its Shaken.</summary>
    public static Sequence Rally() => new(GfNames.RallySeq, [new RallyStep()]);

    /// <summary>The units a round's start rolls for, with the rule each rolls under: Shaken, on the table, holding one (its own aggregation, all).</summary>
    public static IReadOnlyList<(Guid Unit, string Rule)> Rallying(GameState state, PropertySet props) =>
        state.Projection.Units.Values.Where(u => u.Conditions.Contains(GfNames.Shaken) && TeamUnits.AnyOnTable(state, u))
            .Select(u => (u.Id, Rule: RoundStartRallies.FirstOrDefault(r => props.Entry(r) is { Played: true } && Lookup.UnitHas(state, props, u.Id, r))))
            .Where(x => x.Rule is not null).Select(x => (x.Id, x.Rule!)).ToList();

    sealed class RallyStep : IStep
    {
        public string Name => "rally";
        public StepOutcome Run(StepContext ctx)
        {
            var unit = ctx.Frame.Context.ActorUnitId!.Value;
            if (ctx.Frame.Context.Action is not { } rule || !ctx.Projection.Units[unit].Conditions.Contains(GfNames.Shaken)) return new Complete([]);
            var record = ctx.Roll(new Dice.RollPlan(rule, 4, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), 1);
            ctx.Applied.Add(rule);
            return new Complete(Dice.RollOutcome.Successes(record) > 0 ? [new ConditionSet(new Subject(SubjectKind.Unit, unit), GfNames.Shaken, false)] : []);
        }
    }

    /// <summary>
    /// Regenerative Strength: "Place one marker on this model when it ignores a wound. When in melee, pick one of its weapons to get +X attacks, where X is the
    /// number of markers on it." The markers are the model's ignored wounds in the log (assign's `ignored_&lt;model&gt;`); the weapon is the model's first melee
    /// weapon, picked for the player.
    /// </summary>
    public static int RegenerativeStrength(GameState state, Guid model) =>
        Lookup.ModelParameter(state, model, GfNames.RegenerativeStrength) is null && !state.Projection.Models[model].Slot.Properties.Any(p => p.Name == GfNames.RegenerativeStrength) ? 0
            : EventApplier.EffectiveLog(state.Applier.Log).Select(e => e.Payload).OfType<StepCompleted>().Where(s => s.Step == "assign")
                .Sum(s => s.Values?.GetValueOrDefault($"ignored_{model:N}") ?? 0);
}
