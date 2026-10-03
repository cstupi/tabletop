using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Effects;

/// <summary>One instance of a catalog entry in play: who holds it and where it came from. A granted instance (spell library → A grant is its
/// effect row) exposes only the row its grant holds, and that row's children; every other instance exposes its entry's effects.</summary>
public sealed record LiveProperty(Side Holder, CatalogEntry Entry, int? Parameter, Guid? ModelId, Guid? WeaponId, Guid? GrantId, Effect? GrantRow = null, IReadOnlyList<Effect>? Chosen = null)
{
    public IReadOnlyList<Effect> Effects => Chosen ?? (GrantRow is { } row ? [row] : Entry.Effects);
    public Side ActedOn(Effect e) => e.Subject == EffectSubject.Self ? Holder : Interaction.Other(Holder);
}

/// <summary>Pass one's result: the properties that are live, the modifier classes suppressed, and what was applied to get there.</summary>
public sealed class LiveSet(Interaction interaction, IReadOnlyList<LiveProperty> live, IReadOnlySet<TargetModifier> suppressedModifiers,
    IReadOnlyList<(LiveProperty Source, Effect Effect)> applied, HashSet<(Guid UnitId, Guid GrantId)>? read = null, PassOneActs? acts = null)
{
    private readonly HashSet<(Guid UnitId, Guid GrantId)> _read = read ?? [];
    private readonly PassOneActs _acts = acts ?? new();
    /// <summary>Every granted source a consumer used, with the unit holding the grant. A step that acts on this live set emits RollEffects.Consume.</summary>
    public IReadOnlyCollection<(Guid UnitId, Guid GrantId)> GrantsRead => _read;
    /// <summary>Records a granted source, under the unit that holds the grant (a derived instance's Holder is the side it acts on, which an opponent-subject grant makes the other unit).
    /// Reading a property also reads the granted pass-one effects that changed its parameter.</summary>
    public void Read(LiveProperty source)
    {
        if (source.GrantId is { } g)
            foreach (var unit in new[] { Interaction.Attacker.UnitId, Interaction.Defender?.UnitId })
                if (unit is { } u && Interaction.State.Projection.Units[u].Grants.Any(x => x.Id == g)) _read.Add((u, g));
        if (_acts.Modified.TryGetValue(PassOneActs.Key(source), out var by)) foreach (var m in by) Read(m);
    }

    /// <summary>A modifier class a pass-one effect suppressed has just been left out of a plan or a range: the granted sources that suppressed it were used.</summary>
    public void ReadSuppressedModifier(TargetModifier modifier)
    {
        foreach (var (source, m) in _acts.SuppressedModifiers) if (m == modifier) Read(source);
    }

    /// <summary>A property pass one removed would have had an effect of this kind read here: the granted sources that removed it were used.</summary>
    public void ReadRemoved(Side side, EffectKind kind, Func<Effect, bool>? filter = null)
    {
        foreach (var (source, removed) in _acts.Removed)
            if (removed.Holder == side && removed.Effects.Any(e => e.Kind == kind && (filter?.Invoke(e) ?? true))) Read(source);
    }
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

    /// <summary>The rows acting on a side that are offered at a point: an at_point row naming it, whose other conditions hold now. The live set's
    /// interaction must carry that Point, or no at_point condition holds.</summary>
    public IEnumerable<(LiveProperty Source, Effect Effect)> AtPoint(Side side, string point) =>
        Live.SelectMany(p => p.Effects.Where(e => p.ActedOn(e) == side && e.Conditions.Any(c => c.Kind == ConditionKind.AtPoint && c.Values.Contains(point))
                && Conditions.Hold(Interaction, p.Holder, e, p, null)).Select(e => (p, e)));

    public LiveSet WithGranted(Side holder, string entry, int? parameter) =>
        Interaction.Properties.Entry(entry) is { } ce ? new LiveSet(Interaction, [.. Live, new LiveProperty(holder, ce, parameter, null, null, null)], SuppressedModifiers, Applied, _read, _acts) : this;
}

/// <summary>What pass one did, kept so a consumer that reads the affected item can mark the granted source that acted on it: properties removed, parameters modified, modifier classes suppressed.</summary>
public sealed class PassOneActs
{
    public static (Side, string, Guid?, Guid?, Guid?) Key(LiveProperty p) => (p.Holder, p.Entry.Name, p.ModelId, p.WeaponId, p.GrantId);
    public List<(LiveProperty Source, LiveProperty Removed)> Removed { get; } = [];
    public Dictionary<(Side, string, Guid?, Guid?, Guid?), List<LiveProperty>> Modified { get; } = [];
    public List<(LiveProperty Source, TargetModifier Modifier)> SuppressedModifiers { get; } = [];
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
        var acts = new PassOneActs();
        // Every pass-one effect whose conditions hold now, across both sides, in ordinal order. Per-die grants (a roll_result condition) wait for pass two.
        // Pass one runs to a fixed point: an instance a grant adds has its own pass-one rows read in the next round (a granted Boost suppresses its base).
        // Each instance is scanned once, and a grant row adds once per holder instance, so a rule that grants itself stops.
        var scanned = new HashSet<LiveProperty>(ReferenceEqualityComparer.Instance);
        var granted = new HashSet<(Guid, (Side, string, Guid?, Guid?, Guid?))>();
        while (true)
        {
            var candidates = live.Where(p => !scanned.Contains(p)).ToList();
            if (candidates.Count == 0) break;
            foreach (var p in candidates) scanned.Add(p);
            var ordered = candidates.SelectMany(p => p.Effects.Where(e => PassOne.Contains(e.Kind) && e.Conditions.All(c => c.Kind != ConditionKind.RollResult)).Select(e => (p, e)))
                .OrderBy(x => x.e.Ordinal).ToList();
            foreach (var (source, effect) in ordered)
            {
                if (!live.Contains(source) || !Conditions.Hold(ix, source.Holder, effect, source, null)) continue;   // suppressed earlier this pass, or conditions fail
                var side = source.ActedOn(effect);
                switch (effect.Kind)
                {
                    case EffectKind.GrantProperty when ix.Properties.Entry(effect.TargetEntry!) is { } grantedEntry && granted.Add((effect.Id, PassOneActs.Key(source))):
                        live.Add(WithFace(ix, new LiveProperty(side, grantedEntry, effect.Amount, source.ModelId, source.WeaponId, source.GrantId))); break;
                    case EffectKind.SuppressProperty:
                        foreach (var gone in live.Where(p => p.Holder == side && p.Entry.Name == effect.TargetEntry).ToList()) acts.Removed.Add((source, gone));
                        live.RemoveAll(p => p.Holder == side && p.Entry.Name == effect.TargetEntry); break;
                    case EffectKind.ModifyParameter:
                        for (var i = 0; i < live.Count; i++)
                            if (live[i].Holder == side && live[i].Entry.Name == effect.TargetEntry) {
                                var was = live[i];
                                live[i] = was with { Parameter = (was.Parameter ?? 0) + effect.EffectiveAmount(source.Parameter) };
                                if (scanned.Contains(was)) scanned.Add(live[i]);   // the same instance with a new parameter, not a new one
                                if (!acts.Modified.TryGetValue(PassOneActs.Key(live[i]), out var by)) acts.Modified[PassOneActs.Key(live[i])] = by = [];
                                by.Add(source);
                            }
                        break;
                    case EffectKind.SuppressModifiers: suppressed.Add(effect.TargetModifier!.Value); acts.SuppressedModifiers.Add((source, effect.TargetModifier!.Value)); break;
                }
                applied.Add((source, effect));
            }
        }
        return new LiveSet(ix, live, suppressed, applied, null, acts);
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
            .Select(g => WithFace(ix, g.First()));
    }

    /// <summary>
    /// A property whose roll row is asked at the attack-start point and whose face this attack recorded (Interaction.Faces) exposes, instead of its
    /// rows, that roll's children whose roll_result on its own roll holds against the face, with that condition dropped (army-rule hooks → A die
    /// at the start of the attack). Any other property is returned as it is.
    /// </summary>
    static LiveProperty WithFace(Interaction ix, LiveProperty p)
    {
        if (ix.Faces is null || !ix.Faces.TryGetValue(p.Holder, out var faces) || !faces.TryGetValue(p.Entry.Name, out var raw)) return p;
        bool Own(EffectCondition c) => c.Kind == ConditionKind.RollResult && c.Values.Count == 2 && c.Values[0] == p.Entry.Name;
        var rolls = p.Effects.Where(e => e.Kind == EffectKind.Roll && e.Conditions.Any(c => c.Kind == ConditionKind.AtPoint && c.Values.Contains("attack-start"))).ToList();
        if (rolls.Count == 0) return p;
        return p with { Chosen = rolls.SelectMany(r => r.Children).Where(ch => ch.Conditions.Where(Own).All(c => c.Compare(raw)))
            .Select(ch => ch with { Conditions = ch.Conditions.Where(c => !Own(c)).ToList() }).ToList() };
    }
}
