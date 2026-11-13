using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    /// <summary>
    /// p.8: models with line of sight to a target model and a weapon in range of it; p.9 who can strike (`WithinStrike`, by edition).
    /// Containment answers before any of that (p.15): a unit inside a container and a unit outside it cannot target each other at
    /// all, and two units in one container — or an occupant and the container model itself — need no measurement, because there is
    /// no distance and no sight line inside a box. Everything armed is eligible then; only the weapon check survives.
    /// The occupant-and-container half of that clause is unreachable in a game: `ValidTargets` offers enemy units
    /// only, a unit's own transport is friendly, and no unit may enter an enemy container, so the branch is written for
    /// faithfulness to p.15 and is exercised only by a menu-written state.
    /// </summary>
    public static IReadOnlyList<Guid> EligibleModels(GameState state, LiveSet live, Guid attacker, Guid target, IReadOnlyList<Guid> weapons, AttackKind kind)
    {
        // Quick Shot: "This model may shoot after using Rush actions" (p.7 Rush "can't shoot"): after a Rush only a party Quick Shot is live on
        // (its models hold it, GfGroups.Split keeping them apart, or it is granted on the unit) has models that may shoot.
        if (kind == AttackKind.Shooting && live.Interaction.ActionTaken == GfNames.Rush && !live.Has(Side.Attacker, GfNames.QuickShot)) return [];
        if (Battlefield.InsideReach(state, attacker, target) is { } reaches)
            return reaches ? Lookup.AliveModels(state, attacker).Where(m => state.Projection.Models[m].Slot.Weapons.Any(w => weapons.Contains(w.Id))).ToList() : [];
        var targets = Lookup.LivingModels(state, target);
        var indirect = IndirectApplies(live);
        bool Reaches(Guid m, Sight? sight)
        {
            var mine = state.Projection.Models[m].Slot.Weapons.Where(w => weapons.Contains(w.Id)).ToList();
            if (mine.Count == 0) return false;
            if (kind == AttackKind.Melee) return targets.Any(t => WithinStrike(state, m, t));
            var range = mine.Max(w => StatReader.WeaponRange(live, Side.Attacker, w.Id) ?? 0);
            return targets.Any(t => Battlefield.Distance(state, m, t) <= range && (sight is null || sight.CanSee(m, t)));
        }
        var sight = kind == AttackKind.Shooting && !indirect ? Sight.From(state, attacker) : null;               // built once for every pair below
        var eligible = Lookup.LivingModels(state, attacker).Where(m => Reaches(m, sight)).ToList();
        // p.14 Indirect "may target enemies that are not in line of sight": it acted when a model is eligible only because sight was not asked.
        if (indirect && kind == AttackKind.Shooting && eligible.Count > 0)
        {
            var seen = Sight.From(state, attacker);
            if (eligible.Any(m => !Reaches(m, seen))) RecordIndirect(live);
        }
        return eligible;
    }

    /// <summary>p.14 Indirect's sight and cover halves: 3.5.1 whenever the weapon has it; 3.6.0 p.14 "When using Hold actions and shooting" only, the action
    /// read off the interaction (the activation frame's `action_taken`; none, before an activation names its action, is not holding), or on any action
    /// when an attacking model holds Quick Throw ("may always target enemies that are not in line of sight when shooting with Indirect weapons").</summary>
    internal static bool IndirectApplies(LiveSet live) =>
        live.Has(Side.Attacker, GfNames.Indirect) && (!NeedsHold(live) || live.Interaction.ActionTaken == GfNames.Hold || live.Has(Side.Attacker, GfNames.QuickThrow));

    static bool NeedsHold(LiveSet live) => GfEdition.Of(live.Interaction.State).IndirectNeedsHold;

    /// <summary>Indirect acted; off a Hold under 3.6, Quick Throw is what let it.</summary>
    static void RecordIndirect(LiveSet live)
    {
        live.Record(Side.Attacker, GfNames.Indirect);
        if (NeedsHold(live) && live.Interaction.ActionTaken != GfNames.Hold) live.Record(Side.Attacker, GfNames.QuickThrow);
    }

    /// <summary>
    /// Where each eligible shooting model aims, for the clients' shot beams: the nearest target model in its range that it sees, and the index into
    /// `LineOfSight.TargetPoints` of the point its clear line reaches (`SightLine.Seen`). A model sees past whatever blocks under the game's sight
    /// options (p.5 "the perimeter of other units"), so no beam runs to a model hidden behind them. An Indirect weapon (p.14 "may target enemies
    /// that are not in line of sight") aims at the nearest one in range, at its body centre, when it sees none. Off-table models aim nowhere.
    /// </summary>
    public static IReadOnlyList<(Guid Shooter, Guid Target, int Point)> Aims(GameState state, LiveSet live, Guid attacker, Guid target, IReadOnlyList<Guid> eligible, IReadOnlyList<Guid> weapons)
    {
        var targets = Lookup.LivingModels(state, target).Where(t => Battlefield.Placed(state, t) is not null).ToList();
        if (targets.Count == 0) return [];
        var indirect = IndirectApplies(live);
        var sight = Sight.From(state, attacker);
        var aims = new List<(Guid, Guid, int)>();
        foreach (var m in eligible)
        {
            if (Battlefield.Placed(state, m) is null) continue;
            var range = state.Projection.Models[m].Slot.Weapons.Where(w => weapons.Contains(w.Id)).Select(w => StatReader.WeaponRange(live, Side.Attacker, w.Id) ?? 0).DefaultIfEmpty(0).Max();
            var inRange = targets.Select(t => (Id: t, Distance: Battlefield.Distance(state, m, t))).Where(t => t.Distance <= range).OrderBy(t => t.Distance).ToList();
            var seen = inRange.Select(t => (t.Id, Line: sight.Line(m, t.Id))).FirstOrDefault(x => x.Line is { Clear: true, Seen: not null });
            if (seen.Line is { Seen: { } at })
                aims.Add((m, seen.Id, Tabletop.Geometry.LineOfSight.TargetPoints(Battlefield.Placed(state, seen.Id)!.Value).ToList().IndexOf(at)));
            else if (indirect && inRange.Count > 0)
                aims.Add((m, inRange[0].Id, Tabletop.Geometry.LineOfSight.TargetPoints(Battlefield.Placed(state, inRange[0].Id)!.Value).Count() / 3));
        }
        return aims;
    }

    /// <summary>p.11's majority with p.14 Indirect's "ignores cover from sight obstructions": Indirect acted when dropping the sight half took the cover away,
    /// and the verdict then is the full test's, ignored by Indirect. The +1 applies when the verdict is in cover and nothing ignored it (Applies).</summary>
    static CoverVerdict CoverOf(GameState state, LiveSet live, Guid target, IReadOnlyList<Guid> eligible, Guid? takedown = null)
    {
        var indirect = IndirectApplies(live);
        var count = GfRolls.Cover(state, target, eligible, indirect, takedown);
        if (indirect && !count.Majority && GfRolls.Cover(state, target, eligible, ignoresSightObstructions: false, takedown) is { Majority: true } full)
        {
            RecordIndirect(live);
            return new CoverVerdict(true, full.Models, full.Of, GfNames.Indirect);
        }
        return new CoverVerdict(count.Majority, count.Models, count.Of);
    }

    static bool Applies(CoverVerdict c) => c is { InCover: true, IgnoredBy: null };

    /// <summary>The verdict as Values: `in_cover` (the +1 test passed), `cover_models`, `cover_of`, and `cover_indirect` when Indirect's dropped
    /// sight half lost the cover, each with the pusher's `_&lt;i&gt;` suffix when fire-groups settles it.</summary>
    public static void WriteCover(IDictionary<string, int> values, CoverVerdict c, string suffix = "")
    {
        values[$"in_cover{suffix}"] = Applies(c) ? 1 : 0;
        values[$"cover_models{suffix}"] = c.Models;
        values[$"cover_of{suffix}"] = c.Of;
        if (c.IgnoredBy == GfNames.Indirect) values[$"cover_indirect{suffix}"] = 1;
    }

    /// <summary>The verdict a frame's Values hold; null when it holds no `cover_of` (a frame written before the verdict).</summary>
    static CoverVerdict? ReadCover(IReadOnlyDictionary<string, int> values)
    {
        if (!values.TryGetValue("cover_of", out var of)) return null;
        var indirect = values.GetValueOrDefault("cover_indirect") == 1;
        return new CoverVerdict(values.GetValueOrDefault("in_cover") == 1 || indirect, values.GetValueOrDefault("cover_models"), of, indirect ? GfNames.Indirect : null);
    }

    /// <summary>
    /// p.8's Determine Attacks, asked from outside the group's own frame: what this group would roll against this target now,
    /// split by the Quality value each attack is tested against. `fire-groups` calls it once per group before the first shot,
    /// because p.8 fires a unit's weapons simultaneously, and melee's striking steps before the first strike (p.9). The action is
    /// the pushed child's, since a defender strikes back under none.
    /// </summary>
    public static IReadOnlyList<(int Quality, int Attacks)> AttacksAgainst(StepContext ctx, Guid attacker, Guid target, IReadOnlyList<Guid> weapons, AttackKind kind, string? action)
    {
        var live = ctx.Settle(AttackInteraction(ctx, attacker, target, weapons, kind, action));
        return Attacks(ctx, live, EligibleModels(ctx.State, live, attacker, target, weapons, kind), weapons, kind);
    }

    /// <summary>
    /// p.11's cover majority, asked from outside the group's own frame, for the same reason and at the same moment as the attack
    /// counts: p.8 fires a unit's weapons simultaneously, so a group firing after another group's casualties judges *"the majority
    /// of models in a unit"* against the unit as it stood when the action started. Positions do not change
    /// between groups; only the count does.
    /// </summary>
    public static CoverVerdict InCoverAgainst(StepContext ctx, Guid attacker, Guid target, IReadOnlyList<Guid> weapons)
    {
        var live = ctx.Settle(AttackInteraction(ctx, attacker, target, weapons, AttackKind.Shooting, ctx.Frame.Context.Action));
        return CoverOf(ctx.State, live, target, EligibleModels(ctx.State, live, attacker, target, weapons, AttackKind.Shooting));
    }

    /// <summary>
    /// p.8 sums the Attack values of the models that can shoot, and p.8's next step takes "as many Quality tests as attacks". p.4
    /// scores each of those against *"the model's Quality value"*, so the sum is per Quality value rather than per unit: a joined
    /// hero contributes its own attacks at its own value and everything else contributes at the unit's. Ordered
    /// by the value, best first, so the pools and therefore the dice cursor do not depend on model order.
    /// </summary>
    static IReadOnlyList<(int Quality, int Attacks)> Attacks(StepContext ctx, LiveSet live, IEnumerable<Guid> models, IReadOnlyList<Guid> weapons, AttackKind kind) =>
        PerModelAttacks(ctx, live, models, weapons, kind)
              .GroupBy(x => x.Quality).Select(g => (Quality: g.Key, Attacks: g.Sum(x => x.Attacks)))
              .OrderBy(x => x.Quality).ToList();

    /// <summary>Each model with attacks, in the given order, with the Quality value its attacks are tested against.</summary>
    static IReadOnlyList<(Guid Model, int Quality, int Attacks)> PerModelAttacks(StepContext ctx, LiveSet live, IEnumerable<Guid> models, IReadOnlyList<Guid> weapons, AttackKind kind) =>
        models.Select(m => (Model: m, Quality: GfRolls.HitQuality(ctx, live, kind, m),
                            Attacks: ctx.Projection.Models[m].Slot.Weapons.Where(w => weapons.Contains(w.Id)).Sum(w => StatReader.Attacks(ctx.State, w.Id))
                                + RegenerativeStrength(ctx, m, kind, weapons)))
              .Where(x => x.Attacks > 0).ToList();

    /// <summary>Regenerative Strength's "+X attacks" in melee, on the model's first melee weapon when this group holds it (3.5.1). A text-only entry plays
    /// nothing, as the marker rules' Played gate (GfMarkers); 3.6's rolls dice instead (GfEdition.StrikeTurnRolls, GfStrikeRolls36).</summary>
    static int RegenerativeStrength(StepContext ctx, Guid model, AttackKind kind, IReadOnlyList<Guid> weapons)
    {
        if (kind != AttackKind.Melee || GfEdition.Of(ctx.State).StrikeTurnRolls || ctx.Properties.Entry(GfNames.RegenerativeStrength) is not { Played: true } || ctx.Projection.Models[model].Slot.Weapons.FirstOrDefault(w => GfMelee.IsMelee(ctx.State, w)) is not { } first || !weapons.Contains(first.Id)) return 0;
        var x = GfMarkers.RegenerativeStrength(ctx.State, model);
        if (x > 0) ctx.Applied.Add(GfNames.RegenerativeStrength);
        return x;
    }
}
