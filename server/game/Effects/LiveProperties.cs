using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Effects;

/// <summary>One instance of a catalog entry in play: who holds it and where it came from. A granted instance (spell library → A grant is its
/// effect row) exposes only the row its grant holds, and that row's children; every other instance exposes its entry's effects.</summary>
public sealed record LiveProperty(Side Holder, CatalogEntry Entry, int? Parameter, Guid? ModelId, Guid? WeaponId, Guid? GrantId, Effect? GrantRow = null)
{
    public IReadOnlyList<Effect> Effects => GrantRow is { } row ? [row] : Entry.Effects;
    public Side ActedOn(Effect e) => e.Subject == EffectSubject.Self ? Holder : Interaction.Other(Holder);
}

/// <summary>Pass one's result: the properties that are live, the modifier classes suppressed, and what was applied to get there.</summary>
public sealed class LiveSet(Interaction interaction, IReadOnlyList<LiveProperty> live, IReadOnlySet<TargetModifier> suppressedModifiers,
    IReadOnlyList<(LiveProperty Source, Effect Effect)> applied, HashSet<(Guid UnitId, Guid GrantId)>? read = null)
{
    private readonly HashSet<(Guid UnitId, Guid GrantId)> _read = read ?? [];
    /// <summary>Every granted source a consumer used, with the unit holding the grant. A step that acts on this live set emits RollEffects.Consume.</summary>
    public IReadOnlyCollection<(Guid UnitId, Guid GrantId)> GrantsRead => _read;
    public void Read(LiveProperty source) { if (source.GrantId is { } g) _read.Add((Interaction.Of(source.Holder).UnitId, g)); }
    public Interaction Interaction { get; } = interaction;
    public IReadOnlyList<LiveProperty> Live { get; } = live;
    public IReadOnlySet<TargetModifier> SuppressedModifiers { get; } = suppressedModifiers;
    public IReadOnlyList<(LiveProperty Source, Effect Effect)> Applied { get; } = applied;

    public IEnumerable<LiveProperty> On(Side side) => Live.Where(p => p.Holder == side);
    public bool Has(Side side, string entry) => On(side).Any(p => p.Entry.Name == entry);
    public int? Parameter(Side side, string entry)
    {
        var instances = On(side).Where(p => p.Entry.Name == entry).ToList();
        return instances.Count == 0 ? null : instances.Sum(p => p.Parameter ?? 0);
    }

    /// <summary>Effects of one kind acting on a side, in ordinal order, whose conditions hold now (a per-die condition is judged with the die).</summary>
    public IEnumerable<(LiveProperty Source, Effect Effect)> Effects(Side actedOn, EffectKind kind, DieContext? die = null) =>
        Live.SelectMany(p => p.Effects.Where(e => e.Kind == kind && p.ActedOn(e) == actedOn && Conditions.Hold(Interaction, p.Holder, e, p, die)).Select(e => (p, e)))
            .OrderBy(x => x.e.Ordinal).Select(x => (x.p, x.e));

    public LiveSet WithGranted(Side holder, string entry, int? parameter) =>
        Interaction.Properties.Entry(entry) is { } ce ? new LiveSet(Interaction, [.. Live, new LiveProperty(holder, ce, parameter, null, null, null)], SuppressedModifiers, Applied, _read) : this;
}

public static class LiveProperties
{
    static readonly EffectKind[] PassOne = [EffectKind.GrantProperty, EffectKind.SuppressProperty, EffectKind.ModifyParameter, EffectKind.SuppressModifiers];

    public static LiveSet Settle(Interaction ix)
    {
        var live = Gather(ix, Side.Attacker).ToList();
        if (ix.Defender is not null) live.AddRange(Gather(ix, Side.Defender));
        var suppressed = new HashSet<TargetModifier>();
        var applied = new List<(LiveProperty, Effect)>();
        var read = new HashSet<(Guid UnitId, Guid GrantId)>();
        void Mark(LiveProperty s) { if (s.GrantId is { } g) read.Add((ix.Of(s.Holder).UnitId, g)); }
        // Every pass-one effect whose conditions hold now, across both sides, in ordinal order. Per-die grants (a roll_result condition) wait for pass two.
        var candidates = live.SelectMany(p => p.Effects.Where(e => PassOne.Contains(e.Kind) && e.Conditions.All(c => c.Kind != ConditionKind.RollResult)).Select(e => (p, e)))
            .OrderBy(x => x.e.Ordinal).ToList();
        foreach (var (source, effect) in candidates)
        {
            if (!live.Contains(source) || !Conditions.Hold(ix, source.Holder, effect, source, null)) continue;   // suppressed earlier this pass, or conditions fail
            var side = source.ActedOn(effect);
            switch (effect.Kind)
            {
                case EffectKind.GrantProperty when ix.Properties.Entry(effect.TargetEntry!) is { } granted:
                    live.Add(new LiveProperty(side, granted, effect.Amount, source.ModelId, source.WeaponId, source.GrantId)); break;
                case EffectKind.SuppressProperty: if (live.RemoveAll(p => p.Holder == side && p.Entry.Name == effect.TargetEntry) > 0) Mark(source); break;
                case EffectKind.ModifyParameter:
                    for (var i = 0; i < live.Count; i++)
                        if (live[i].Holder == side && live[i].Entry.Name == effect.TargetEntry) { live[i] = live[i] with { Parameter = (live[i].Parameter ?? 0) + effect.EffectiveAmount(source.Parameter) }; Mark(source); }
                    break;
                case EffectKind.SuppressModifiers: suppressed.Add(effect.TargetModifier!.Value); if (ix.Defender is not null) Mark(source); break;
            }
            applied.Add((source, effect));
        }
        return new LiveSet(ix, live, suppressed, applied, read);
    }

    /// <summary>Unit properties and grants, unit-kind properties on the unit's slots, model properties under aggregation, weapon properties;
    /// duplicates collapsed unless the rule takes (X).</summary>
    static IEnumerable<LiveProperty> Gather(Interaction ix, Side side)
    {
        var party = ix.Of(side);
        var state = ix.State;
        var unit = state.Projection.Units[party.UnitId];
        var result = new List<LiveProperty>();
        foreach (var p in unit.Definition.Properties) if (ix.Properties.Entry(p.Name) is { } e) result.Add(new LiveProperty(side, e, p.Parameter, null, null, null));
        // A grant is its row (spell library spec): the spell entry it names, exposing only that row. A subject-opponent row ("friendly units get
        // X against") reaches only an opponent on the caster's team, which is p.13's "friendly" read against the grant.
        foreach (var g in unit.Grants)
        {
            if (ix.Properties.Entry(g.EntryName) is not { } spell || ix.Properties.AllEffects().FirstOrDefault(x => x.Id == g.EffectId) is not { } row) continue;
            if (row.Subject == EffectSubject.Opponent && ix.Defender is not null
                && Interpreter.Teams.Of(state, ix.Of(Interaction.Other(side)).UnitId) != Interpreter.Teams.Of(state, g.CasterUnitId)) continue;
            result.Add(new LiveProperty(side, spell, null, null, null, g.Id, row));
        }
        // A unit-kind entry on a model slot is the unit's rule, not a per-model one, and aggregation is not declared for the kind. Import produces the
        // shape two ways — a rule whose count is smaller than the unit, and a joined leader, whose rules all land on its slot whatever their kind — so it
        // is read off the definition, holder-less, and Lookup.UnitHas reads it the same way. Its effects therefore act on the whole unit.
        foreach (var (p, e) in Lookup.UnitKindOnSlots(state, ix.Properties, party.UnitId)) result.Add(new LiveProperty(side, e, p.Parameter, null, null, null));
        var models = party.ModelIds.Count > 0 ? party.ModelIds : Lookup.LivingModels(state, party.UnitId);
        foreach (var group in Lookup.ModelProperties(state, party.UnitId, models).GroupBy(x => x.Property.Name))
        {
            if (ix.Properties.Entry(group.Key) is not { } e || e.Kind == CatalogKind.Unit) continue;   // added above, off the definition
            var count = group.Select(x => x.ModelId).Distinct().Count();
            var live = e.Aggregation switch { Aggregation.All => count == models.Count, Aggregation.Any => count > 0, Aggregation.Majority => count * 2 > models.Count, _ => true };
            if (!live) continue;
            if (e.Aggregation == Aggregation.None) result.AddRange(group.Select(x => new LiveProperty(side, e, x.Property.Parameter, x.ModelId, null, null)));
            else result.Add(new LiveProperty(side, e, group.First().Property.Parameter, null, null, null));
        }
        foreach (var (weaponId, modelId, p) in Lookup.WeaponProperties(state, party.WeaponIds))
            if (ix.Properties.Entry(p.Name) is { } e) result.Add(new LiveProperty(side, e, p.Parameter, modelId, weaponId, null));
        // A rule instance doesn't stack with another of the same kind unless it declares a parameter. Per-model rules (aggregation none) keep one
        // instance per model; a weapon rule is one rule per weapon *type*, so three of the same weapon's AP(1) are one AP(1), not three summed.
        string WeaponName(Guid? id) => id is { } w ? state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).First(x => x.Id == w).Name : "";
        return result.GroupBy(p => (p.Entry.Name, p.Entry.Aggregation == Aggregation.None ? p.ModelId ?? Guid.Empty : Guid.Empty, p.WeaponId is null ? "" : WeaponName(p.WeaponId), p.GrantId ?? Guid.Empty))
            .Select(g => g.First());
    }
}
