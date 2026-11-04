using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Effects;

/// <summary>One instance of a catalog entry in play: who holds it and where it came from. A granted instance (spell library → A grant is its
/// effect row) exposes only the row its grant holds, and that row's children. A carried pick entry (one with targeting) exposes none: its once
/// rows act only through the grants its pick writes (army-book rules spec → the pick). Every other instance exposes its entry's effects.
/// Via: the rule whose per-die grant put this instance here (WithGranted), for RollModifier.Via.</summary>
public sealed record LiveProperty(Side Holder, CatalogEntry Entry, int? Parameter, Guid? ModelId, Guid? WeaponId, Guid? GrantId, Effect? GrantRow = null, IReadOnlyList<Effect>? Chosen = null,
    string? Via = null)
{
    public IReadOnlyList<Effect> Effects => Chosen ?? (GrantRow is { } row ? [row] : Entry.Targeting is not null ? [] : Carried(Entry));
    /// <summary>A carried entry's rows, less its marker rows, which act only through the grants a marker is.</summary>
    static IReadOnlyList<Effect> Carried(CatalogEntry entry) => CarriedRows.GetValue(entry, e => e.Effects.Any(f => f.Duration is EffectDuration.Marker or EffectDuration.SpentMarker)
        ? e.Effects.Where(f => f.Duration is not (EffectDuration.Marker or EffectDuration.SpentMarker)).ToList() : e.Effects);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CatalogEntry, IReadOnlyList<Effect>> CarriedRows = new();
    public Side ActedOn(Effect e) => e.Subject == EffectSubject.Self ? Holder : Interaction.Other(Holder);
}

/// <summary>Pass one's result: the properties that are live, the modifier classes suppressed, and what was applied to get there.</summary>
public sealed class LiveSet(Interaction interaction, IReadOnlyList<LiveProperty> live, IReadOnlySet<TargetModifier> suppressedModifiers,
    IReadOnlyList<(LiveProperty Source, Effect Effect)> applied, HashSet<(Guid UnitId, Guid GrantId)>? read = null, PassOneActs? acts = null, AppliedEntries? recorder = null)
{
    private readonly HashSet<(Guid UnitId, Guid GrantId)> _read = read ?? [];
    private readonly PassOneActs _acts = acts ?? new();
    /// <summary>Every granted source a consumer used, with the unit holding the grant. A step that acts on this live set spends them through StepContext.Consume.</summary>
    public IReadOnlyCollection<(Guid UnitId, Guid GrantId)> GrantsRead => _read;

    /// <summary>A source whose row applied here: it is spent (Spend) and its entry recorded as having acted (Record).</summary>
    public void Read(LiveProperty source)
    {
        Spend(source);
        Record(source);
    }

    /// <summary>Records a granted source as used, under the unit that holds the grant (a derived instance's Holder is the side it acts on, which an opponent-subject grant makes the other unit),
    /// without saying it changed anything: "next time the effect would apply" spends a grant whatever the dice then show.
    /// Spending a property also spends the granted pass-one effects that changed its parameter.</summary>
    public void Spend(LiveProperty source)
    {
        if (source.GrantId is { } g && source.GrantRow?.Duration != EffectDuration.Marker)   // a Marker grant stays for the game
            foreach (var unit in new[] { Interaction.Attacker.UnitId, Interaction.Defender?.UnitId })
                if (unit is { } u && Interaction.State.Projection.Units[u].Grants.Any(x => x.Id == g)) _read.Add((u, g));
        if (_acts.Modified.TryGetValue(PassOneActs.Key(source), out var by)) foreach (var m in by) Spend(m);
    }

    /// <summary>The source's entry acted in this step (StepContext.Applied), and with it the pass-one sources behind it: the one that granted it and those that changed its parameter.
    /// Nothing is recorded for a live set settled with no recorder, which is every question asked outside a step.</summary>
    public void Record(LiveProperty source) => Record(source, new HashSet<LiveProperty>(ReferenceEqualityComparer.Instance));

    void Record(LiveProperty source, HashSet<LiveProperty> seen)
    {
        if (recorder is null || !seen.Add(source)) return;
        recorder.Add(source.Entry.Name);
        if (_acts.GrantedBy.TryGetValue(source, out var grantor)) Record(grantor, seen);
        if (_acts.Modified.TryGetValue(PassOneActs.Key(source), out var by)) foreach (var m in by) Record(m, seen);
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
            if (removed.Holder == side && removed.Effects.Any(e => e.Kind == kind && (filter?.Invoke(e) ?? true)))
            {
                Read(source);
                recorder?.Ignore(removed.Entry.Name, source.Entry.Name);
            }
    }

    /// <summary>The rows of one kind acting on a side, from properties pass one removed (or whose rows on a roll it took away), whose conditions
    /// hold now: what would have applied, and the property that removed it (detailed-log spec). Pure.</summary>
    public IEnumerable<(LiveProperty Removed, Effect Effect, LiveProperty By)> RemovedRows(Side actedOn, EffectKind kind, Func<Effect, bool>? filter = null) =>
        _acts.Removed.SelectMany(x => x.Removed.Effects
            .Where(e => e.Kind == kind && x.Removed.ActedOn(e) == actedOn && (filter?.Invoke(e) ?? true) && Conditions.Hold(Interaction, x.Removed.Holder, e, x.Removed, null))
            .Select(e => (x.Removed, e, x.Source)))
            .OrderBy(x => x.e.Ordinal);
    public Interaction Interaction { get; } = interaction;
    public IReadOnlyList<LiveProperty> Live { get; } = live;
    public IReadOnlySet<TargetModifier> SuppressedModifiers { get; } = suppressedModifiers;
    public IReadOnlyList<(LiveProperty Source, Effect Effect)> Applied { get; } = applied;

    public IEnumerable<LiveProperty> On(Side side) => Live.Where(p => p.Holder == side);
    public bool Has(Side side, string entry) => On(side).Any(p => p.Entry.Name == entry);
    /// <summary>Has, for a rule that is applied here: every instance of it is read, so a granted one is spent by RollEffects.Consume with the step's other reads.</summary>
    public bool Use(Side side, string entry)
    {
        var any = false;
        foreach (var p in On(side).Where(p => p.Entry.Name == entry).ToList()) { Read(p); any = true; }
        return any;
    }
    /// <summary>Use without spending: every instance is recorded as having acted, for a rule whose use the step does not spend (Strider, Indirect's sight and cover halves).</summary>
    public bool Record(Side side, string entry)
    {
        var any = false;
        foreach (var p in On(side).Where(p => p.Entry.Name == entry).ToList()) { Record(p); any = true; }
        return any;
    }
    /// <summary>Use without recording: every instance is spent, for a rule a step spends whether or not it changed anything (a granted Flying on any applied move).</summary>
    public bool Spend(Side side, string entry)
    {
        var any = false;
        foreach (var p in On(side).Where(p => p.Entry.Name == entry).ToList()) { Spend(p); any = true; }
        return any;
    }
    public int? Parameter(Side side, string entry)
    {
        var instances = On(side).Where(p => p.Entry.Name == entry).ToList();
        return instances.Count == 0 ? null : ParameterSum.Of(instances.Select(p => p.Parameter));
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

    /// <summary>The live set with one per-die grant added (a block group's hits, keyed by score-hits). The added instance's Via is the
    /// live rule whose per-die grant_property row names that entry and amount, which is what the dice set off.</summary>
    public LiveSet WithGranted(Side holder, string entry, int? parameter) =>
        Interaction.Properties.Entry(entry) is { } ce ? new LiveSet(Interaction, [.. Live, new LiveProperty(holder, ce, parameter, null, null, null, Via: PerDieGrantor(holder, entry, parameter))], SuppressedModifiers, Applied, _read, _acts, recorder) : this;

    string? PerDieGrantor(Side holder, string entry, int? parameter) => PerDieGrantorOf(holder, entry, parameter)?.Entry.Name;

    LiveProperty? PerDieGrantorOf(Side holder, string entry, int? parameter) =>
        Live.FirstOrDefault(p => p.Effects.Any(e => e.Kind == EffectKind.GrantProperty && e.TargetEntry == entry && (e.Amount ?? 0) == (parameter ?? 0)
            && p.ActedOn(e) == holder && e.Conditions.Any(c => c.Kind == ConditionKind.RollResult)));

    /// <summary>The entry that granted this instance: pass one's grantor, or a per-die grant's (Via). Null for a property held outright.</summary>
    public string? GrantorOf(LiveProperty p) => p.Via ?? (_acts.GrantedBy.TryGetValue(p, out var by) ? by.Entry.Name : null);

    /// <summary>Whether this instance comes from the side's own rules: traced back through the grants that put it here (pass one's grantor, a per-die
    /// grant's rule), the rule at the root is held by that side, and a granted row on it was placed by its own team (ForeignToHitPenalties).</summary>
    public bool OwnedBy(LiveProperty p, Side side)
    {
        var seen = new HashSet<LiveProperty>(ReferenceEqualityComparer.Instance);
        while (seen.Add(p))
        {
            if (_acts.GrantedBy.TryGetValue(p, out var by)) { p = by; continue; }
            if (p.Via is not null && PerDieGrantorOf(p.Holder, p.Entry.Name, p.Parameter) is { } die) { p = die; continue; }
            break;
        }
        if (p.Holder != side) return false;
        if (p.GrantId is not { } g) return true;
        var unit = Interaction.Of(side).UnitId;
        var grant = Interaction.State.Projection.Units[unit].Grants.FirstOrDefault(x => x.Id == g);
        return grant is not null && Interpreter.Teams.Of(Interaction.State, grant.CasterUnitId) == Interpreter.Teams.Of(Interaction.State, unit);
    }

    /// <summary>The entries whose pass-one rows suppressed this modifier class.</summary>
    public IReadOnlyList<string> Suppressors(TargetModifier modifier) =>
        _acts.SuppressedModifiers.Where(x => x.Modifier == modifier).Select(x => x.Source.Entry.Name).Distinct().ToList();
}

/// <summary>What pass one did, kept so a consumer that reads the affected item can mark the granted source that acted on it: properties removed, parameters modified, modifier classes suppressed,
/// and which source granted each instance a grant row added.</summary>
public sealed class PassOneActs
{
    public static (Side, string, Guid?, Guid?, Guid?) Key(LiveProperty p) => (p.Holder, p.Entry.Name, p.ModelId, p.WeaponId, p.GrantId);
    public Dictionary<LiveProperty, LiveProperty> GrantedBy { get; } = new(ReferenceEqualityComparer.Instance);
    public List<(LiveProperty Source, LiveProperty Removed)> Removed { get; } = [];
    public Dictionary<(Side, string, Guid?, Guid?, Guid?), List<LiveProperty>> Modified { get; } = [];
    public List<(LiveProperty Source, TargetModifier Modifier)> SuppressedModifiers { get; } = [];
}

public static class LiveProperties
{
    static readonly EffectKind[] PassOne = [EffectKind.GrantProperty, EffectKind.SuppressProperty, EffectKind.ModifyParameter, EffectKind.SuppressModifiers];

    /// <summary>The recorder, when given, is the step's (StepContext.Settle): the set records into it what its consumers apply.</summary>
    public static LiveSet Settle(Interaction ix, AppliedEntries? recorder = null)
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
                        // A grant of the holder's own X ("Counts as having Impact(X)") takes the parameter.
                        var derived = WithFace(ix, new LiveProperty(side, grantedEntry, effect.AmountFromParameter ? effect.EffectiveAmount(source.Parameter) : effect.Amount, source.ModelId, source.WeaponId, source.GrantId));
                        live.Add(derived); acts.GrantedBy[derived] = source; break;
                    // "Ignores penalties from …": a suppression naming a roll takes away only that rule's modifiers of the roll, and keeps the rule.
                    case EffectKind.SuppressProperty when effect.TargetRoll is { } roll:
                        for (var i = 0; i < live.Count; i++)
                            if (live[i].Holder == side && live[i].Entry.Name == effect.TargetEntry && live[i].Effects.Any(e => e.Kind == EffectKind.ModifyRoll && e.TargetRoll == roll))
                            {
                                acts.Removed.Add((source, live[i]));
                                live[i] = live[i] with { Chosen = live[i].Effects.Where(e => !(e.Kind == EffectKind.ModifyRoll && e.TargetRoll == roll)).ToList() };
                            }
                        break;
                    case EffectKind.SuppressProperty:
                        foreach (var gone in live.Where(p => p.Holder == side && p.Entry.Name == effect.TargetEntry).ToList()) acts.Removed.Add((source, gone));
                        live.RemoveAll(p => p.Holder == side && p.Entry.Name == effect.TargetEntry); break;
                    case EffectKind.ModifyParameter:
                        for (var i = 0; i < live.Count; i++)
                            if (live[i].Holder == side && live[i].Entry.Name == effect.TargetEntry) {
                                var was = live[i];
                                // "to a min. of AP(0)": a floor holds the sum there, and never raises a parameter already below it.
                                var sum = (was.Parameter ?? 0) + effect.EffectiveAmount(source.Parameter);
                                live[i] = was with { Parameter = effect.Floor is { } floor ? Math.Max(sum, Math.Min(was.Parameter ?? 0, floor)) : sum };
                                if (scanned.Contains(was)) scanned.Add(live[i]);   // the same instance with a new parameter, not a new one
                                if (acts.GrantedBy.TryGetValue(was, out var grantor)) acts.GrantedBy[live[i]] = grantor;
                                if (!acts.Modified.TryGetValue(PassOneActs.Key(live[i]), out var by)) acts.Modified[PassOneActs.Key(live[i])] = by = [];
                                by.Add(source);
                            }
                        break;
                    case EffectKind.SuppressModifiers: suppressed.Add(effect.TargetModifier!.Value); acts.SuppressedModifiers.Add((source, effect.TargetModifier!.Value)); break;
                }
                applied.Add((source, effect));
            }
        }
        return new LiveSet(ix, live, suppressed, applied, null, acts, recorder);
    }

    /// <summary>Unit properties and grants, unit-kind properties on the unit's slots, model properties under aggregation, weapon properties;
    /// duplicates collapsed unless the rule takes (X).</summary>
    static IEnumerable<LiveProperty> Gather(Interaction ix, Side side)
    {
        var party = ix.Of(side);
        var state = ix.State;
        var unit = state.Projection.Units[party.UnitId];
        var result = new List<LiveProperty>();
        // A text-only entry (Played false) plays as an absent name does (army-book rules spec → Played flag).
        CatalogEntry? Playing(string name) => ix.Properties.Entry(name) is { Played: true } e ? e : null;
        foreach (var p in unit.Definition.Properties) if (Playing(p.Name) is { } e) result.Add(new LiveProperty(side, e, p.Parameter, null, null, null));
        // A grant is its row (spell library spec): the spell entry it names, exposing only that row. A subject-opponent row ("friendly units get
        // X against") reaches only an opponent on the caster's team, which is p.13's "friendly" read against the grant. A grant its holder's own side
        // placed (a marker a unit's own rule puts on it: "enemies attacking this unit get AP(-1)") reaches every opponent.
        var holderTeam = Interpreter.Teams.Of(state, unit.Id);
        foreach (var g in unit.Grants)
        {
            if (ix.Properties.Entry(g.EntryName) is not { } spell || ix.Properties.EffectById(g.EffectId) is not { } row) continue;
            var casterTeam = Interpreter.Teams.Of(state, g.CasterUnitId);
            var ownMarker = casterTeam == holderTeam && row.Duration is EffectDuration.Marker or EffectDuration.SpentMarker;
            if (row.Subject == EffectSubject.Opponent && ix.Defender is not null && !ownMarker
                && Interpreter.Teams.Of(state, ix.Of(Interaction.Other(side)).UnitId) != casterTeam) continue;
            result.Add(new LiveProperty(side, spell, null, null, null, g.Id, row));
        }
        // A unit-kind entry on a model slot is the unit's rule, not a per-model one, and aggregation is not declared for the kind. Import produces the
        // shape two ways — a rule whose count is smaller than the unit, and a joined leader, whose rules all land on its slot whatever their kind — so it
        // is read off the definition, holder-less, and Lookup.UnitHas reads it the same way. Its effects therefore act on the whole unit.
        foreach (var (p, e) in Lookup.UnitKindOnSlots(state, ix.Properties, party.UnitId)) result.Add(new LiveProperty(side, e, p.Parameter, null, null, null));
        var models = party.ModelIds.Count > 0 ? party.ModelIds : Lookup.LivingModels(state, party.UnitId);
        foreach (var group in Lookup.ModelProperties(state, party.UnitId, models).GroupBy(x => x.Property.Name))
        {
            if (Playing(group.Key) is not { } e || e.Kind == CatalogKind.Unit) continue;   // added above, off the definition
            var count = group.Select(x => x.ModelId).Distinct().Count();
            var live = e.Aggregation switch { Aggregation.All => count == models.Count, Aggregation.Any => count > 0, Aggregation.Majority => count * 2 > models.Count, _ => true };
            if (!live) continue;
            if (e.Aggregation == Aggregation.None) result.AddRange(group.Select(x => new LiveProperty(side, e, x.Property.Parameter, x.ModelId, null, null)));
            else result.Add(new LiveProperty(side, e, group.First().Property.Parameter, null, null, null));
        }
        foreach (var (weaponId, modelId, p) in Lookup.WeaponProperties(state, party.WeaponIds))
            if (Playing(p.Name) is { } e) result.Add(new LiveProperty(side, e, p.Parameter, modelId, weaponId, null));
        // A spell's weapon rules are the attacker's ordinary rules for these hits, so their pass-one rows and per-die rows act like a weapon's.
        if (side == Side.Attacker && ix.SpellRules is { } spellRules)
            foreach (var (name, parameter) in spellRules) if (Playing(name) is { } rule) result.Add(new LiveProperty(side, rule, parameter, null, null, null));
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
