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
    static IReadOnlyList<Effect> Carried(CatalogEntry entry) => CarriedRows.GetValue(entry, e => e.Effects.Any(f => IsMarker(f.Duration))
        ? e.Effects.Where(f => !IsMarker(f.Duration)).ToList() : e.Effects);
    static bool IsMarker(EffectDuration d) => d is EffectDuration.Marker or EffectDuration.SpentMarker or EffectDuration.OneMarker;
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CatalogEntry, IReadOnlyList<Effect>> CarriedRows = new();
    public Side ActedOn(Effect e) => e.Subject == EffectSubject.Self ? Holder : Interaction.Other(Holder);
}

/// <summary>Pass one's result: the properties that are live, the modifier classes suppressed, and what was applied to get there.</summary>
public sealed class LiveSet(Interaction interaction, IReadOnlyList<LiveProperty> live, IReadOnlySet<TargetModifier> suppressedModifiers,
    IReadOnlyList<(LiveProperty Source, Effect Effect)> applied, HashSet<(Guid UnitId, Guid GrantId)>? read = null, PassOneActs? acts = null, AppliedEntries? recorder = null,
    IReadOnlyList<TotalChange>? totals = null)
{
    private readonly IReadOnlyList<TotalChange> _totals = totals ?? [];
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
    /// without saying it changed anything: "next time the effect would apply" spends a grant whatever the dice then show. It marks that grant alone;
    /// RollEffects.Consume takes its fellows with it unless it is a one_marker token (a spent_marker grant is read only outside a step: Interaction.RemovedTokens).
    /// Spending a property also spends the granted pass-one effects that changed its parameter.</summary>
    public void Spend(LiveProperty source)
    {
        // A Marker grant stays for the game ("until the end of the game"): the grant itself, and an instance its grant_property row derived (Piercing
        // Frenzy, Piercing Growth: "get AP(+1)"), which carries the grant's id but not its row, so the row is read off the grant.
        if (source.GrantId is { } g && source.GrantRow?.Duration != EffectDuration.Marker)
            foreach (var unit in new[] { Interaction.Attacker.UnitId, Interaction.Defender?.UnitId })
                if (unit is { } u && Interaction.State.Projection.Units[u].Grants.FirstOrDefault(x => x.Id == g) is { } held
                    && Interaction.Properties.EffectById(held.EffectId)?.Duration != EffectDuration.Marker) _read.Add((u, g));
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
            .Where(e => e.Kind == kind && x.Removed.ActedOn(e) == actedOn && (filter?.Invoke(e) ?? true) && Conditions.Hold(Interaction, x.Removed.Holder, e, x.Removed, null, this))
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
        Live.SelectMany(p => p.Effects.Where(e => e.Kind == kind && p.ActedOn(e) == actedOn && Conditions.Hold(Interaction, p.Holder, e, p, die, this)).Select(e => (p, e)))
            .OrderBy(x => x.e.Ordinal).Select(x => (x.p, x.e));

    /// <summary>The rows acting on a side that are offered at a point: an at_point row naming it, whose other conditions hold now. The live set's
    /// interaction must carry that Point, or no at_point condition holds.</summary>
    public IEnumerable<(LiveProperty Source, Effect Effect)> AtPoint(Side side, string point) =>
        Live.SelectMany(p => p.Effects.Where(e => p.ActedOn(e) == side && e.Conditions.Any(c => c.Kind == ConditionKind.AtPoint && c.Values.Contains(point))
                && Conditions.Hold(Interaction, p.Holder, e, p, null, this)).Select(e => (p, e)));

    /// <summary>The live set with one per-die grant added (a block group's hits, keyed by score-hits). The added instance's Via is the
    /// live rule whose per-die grant_property row names that entry and amount, which is what the dice set off, or `via` when the caller names the
    /// rule that gave the hits it (Heavy Impact's AP(1)). The changes to a weapon rule's total (LiveProperties.Totals) are made again over the
    /// instances as pass one left them plus this one, so a reduction comes off the hit's whole total once.</summary>
    public LiveSet WithGranted(Side holder, string entry, int? parameter, string? via = null)
    {
        if (Interaction.Properties.Entry(entry) is not { } ce) return this;
        var raw = Live.Select(p => _acts.Raw.TryGetValue(p, out var r) ? r : p).ToList();
        raw.Add(new LiveProperty(holder, ce, parameter, null, null, null, Via: via ?? PerDieGrantor(holder, entry, parameter)));
        return new LiveSet(Interaction, LiveProperties.Totals(raw, _totals, _acts), SuppressedModifiers, Applied, _read, _acts, recorder, _totals);
    }

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
    /// <summary>An instance a weapon rule's total change (LiveProperties.Totals) rewrote, to the instance as pass one left it.</summary>
    public Dictionary<LiveProperty, LiveProperty> Raw { get; } = new(ReferenceEqualityComparer.Instance);
    public List<(LiveProperty Source, LiveProperty Removed)> Removed { get; } = [];
    public Dictionary<(Side, string, Guid?, Guid?, Guid?), List<LiveProperty>> Modified { get; } = [];
    public List<(LiveProperty Source, TargetModifier Modifier)> SuppressedModifiers { get; } = [];
}

/// <summary>A modify_parameter row on a weapon rule (AP: "those hits count as having AP(-1), to a min. of AP(0)", "loses AP(1) on its weapons"): it changes
/// the total the side's instances of the rule sum to on a hit, once, with its floor held on that total.</summary>
public sealed record TotalChange(Side Side, string Entry, int Amount, int? Floor, LiveProperty Source);

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
        var totals = new List<TotalChange>();
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
                    // A weapon rule sums over the hit: AP(1) on the weapon and AP(+1) from Piercing Charge are AP(2) on each of its hits, and "those hits
                    // count as having AP(-1)" makes it AP(1), not AP(0). The change is made on the total once pass one is done (Totals).
                    case EffectKind.ModifyParameter when ix.Properties.Entry(effect.TargetEntry!) is { Kind: CatalogKind.Weapon }:
                        totals.Add(new TotalChange(side, effect.TargetEntry!, effect.EffectiveAmount(source.Parameter), effect.Floor, source)); break;
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
        return new LiveSet(ix, Totals(live, totals, acts), suppressed, applied, null, acts, recorder, totals);
    }

    /// <summary>
    /// The instances with each weapon rule's total changed (TotalChange): per side and rule, the instances' parameters summed, each change added in
    /// ordinal order with its floor held on the sum (a floor never raises a total already below it), and the difference written back onto the
    /// instances. A reduction comes off the instances carrying the most first, each down to zero, and what an unfloored change takes beyond that
    /// goes on one instance. A rewritten instance keeps its grantor and records the changes as having modified it, as a per-instance change does, and PassOneActs.Raw maps it back.
    /// </summary>
    public static IReadOnlyList<LiveProperty> Totals(IReadOnlyList<LiveProperty> live, IReadOnlyList<TotalChange> totals, PassOneActs acts)
    {
        if (totals.Count == 0) return live;
        var result = live.ToList();
        foreach (var key in totals.Select(t => (t.Side, t.Entry)).Distinct())
        {
            var at = Enumerable.Range(0, result.Count).Where(i => result[i].Holder == key.Side && result[i].Entry.Name == key.Entry).ToList();
            if (at.Count == 0) continue;
            var changes = totals.Where(t => (t.Side, t.Entry) == key).ToList();   // pass one's order, which is the rows' ordinals
            var raw = ParameterSum.Of(at.Select(i => result[i].Parameter));
            var total = raw;
            foreach (var t in changes)
            {
                var sum = total + t.Amount;
                total = t.Floor is { } floor ? Math.Max(sum, Math.Min(total, floor)) : sum;
            }
            var delta = total - raw;
            var next = at.ToDictionary(i => i, i => result[i].Parameter ?? 0);
            if (delta > 0) next[at[0]] += delta;
            foreach (var i in at.OrderByDescending(i => next[i]))
            {
                if (delta >= 0) break;
                var cut = Math.Min(Math.Max(0, next[i]), -delta);
                next[i] -= cut; delta += cut;
            }
            if (delta < 0) next[at[0]] += delta;   // an unfloored change takes the total below zero, on one instance
            foreach (var i in at.Where(i => next[i] != (result[i].Parameter ?? 0)))
            {
                var was = result[i];
                result[i] = was with { Parameter = next[i] };
                acts.Raw[result[i]] = acts.Raw.TryGetValue(was, out var original) ? original : was;
                if (acts.GrantedBy.TryGetValue(was, out var grantor)) acts.GrantedBy[result[i]] = grantor;
                if (!acts.Modified.TryGetValue(PassOneActs.Key(result[i]), out var by)) acts.Modified[PassOneActs.Key(result[i])] = by = [];
                foreach (var t in changes.Where(t => !by.Contains(t.Source))) by.Add(t.Source);
            }
        }
        return result;
    }

    const string UnitWideText = "This model and its unit";

    /// <summary>"This model and its unit get X" (an Aura, Banner, Musician): a model entry one living carrier anywhere in the unit holds for every party of it.</summary>
    public static bool IsUnitWide(CatalogEntry e) => e is { Kind: CatalogKind.Model, Aggregation: Aggregation.Any } && e.RuleText.StartsWith(UnitWideText, StringComparison.Ordinal);

    /// <summary>Unit properties and grants, unit-kind properties on the unit's slots, model properties under aggregation, weapon properties;
    /// duplicates collapsed unless the rule takes (X). Hits no weapon made (Interaction.UnarmedHits) gather nothing on the attacker's side.</summary>
    static IEnumerable<LiveProperty> Gather(Interaction ix, Side side)
    {
        // Hits no weapon and no attack made: every attacker rule that acts on a hit acts on its weapons or its attacks ("its weapons get AP(+1)",
        // "hits from weapons", "hits from this model's weapons"), so none acts on these. What the rule dealing them adds is its own caller's.
        if (side == Side.Attacker && ix.UnarmedHits) return [];
        var party = ix.Of(side);
        var state = ix.State;
        var unit = state.Projection.Units[party.UnitId];
        var result = new List<LiveProperty>();
        var models = party.ModelIds.Count > 0 ? party.ModelIds : Lookup.LivingModels(state, party.UnitId);
        // A text-only entry (Played false) plays as an absent name does (army-book rules spec → Played flag).
        CatalogEntry? Playing(string name) => ix.Properties.Entry(name) is { Played: true } e ? e : null;
        foreach (var p in unit.Definition.Properties) if (Playing(p.Name) is { } e) result.Add(new LiveProperty(side, e, p.Parameter, null, null, null));
        // A grant is its row (spell library spec): the spell entry it names, exposing only that row. A subject-opponent row ("friendly units get
        // X against") reaches only an opponent on the caster's team, which is p.13's "friendly" read against the grant. A grant its holder's own side
        // placed (a marker a unit's own rule puts on it: "enemies attacking this unit get AP(-1)") reaches every opponent.
        // A one_marker token is one: of an entry's tokens on the unit, the first that reaches this interaction acts ("may remove one token … to get").
        var holderTeam = Interpreter.Teams.Of(state, unit.Id);
        var tokens = new HashSet<string>();
        foreach (var g in unit.Grants)
        {
            if (ix.Properties.Entry(g.EntryName) is not { } spell || ix.Properties.EffectById(g.EffectId) is not { } row) continue;
            // "Friendly units may remove tokens from their target … where X is the number of removed tokens": inside a step a spent_marker
            // grant acts only through the count its attack removed (RemovedTokens, below), never as it lies.
            if (row.Duration == EffectDuration.SpentMarker && ix.RemovedTokens is not null) continue;
            var casterTeam = Interpreter.Teams.Of(state, g.CasterUnitId);
            var ownMarker = casterTeam == holderTeam && row.Duration is EffectDuration.Marker or EffectDuration.SpentMarker or EffectDuration.OneMarker;
            if (row.Subject == EffectSubject.Opponent && ix.Defender is not null && !ownMarker
                && Interpreter.Teams.Of(state, ix.Of(Interaction.Other(side)).UnitId) != casterTeam) continue;
            if (row.Duration == EffectDuration.OneMarker && !tokens.Add(g.EntryName)) continue;
            // "For each token all models with this rule in this unit get AP(+1)": the tokens of a rule any model may carry, placed on a unit some of
            // whose models carry it, reach only a party whose models all carry it, so neither a joined Hero without it nor the squad beside a Hero
            // with it gets them. A rule granted to the unit with no model carrying it (a relic's) reaches every party.
            if (ownMarker && spell is { Kind: CatalogKind.Model, Aggregation: Aggregation.Any } && !IsUnitWide(spell)
                && unit.Definition.Models.Any(m => m.Properties.Any(p => p.Name == spell.Name))
                && !models.All(m => state.Projection.Models[m].Slot.Properties.Any(p => p.Name == spell.Name))) continue;
            result.Add(new LiveProperty(side, spell, null, null, null, g.Id, row));
        }
        // A unit-kind entry on a model slot is the unit's rule, not a per-model one, and aggregation is not declared for the kind. Import produces the
        // shape two ways — a rule whose count is smaller than the unit, and a joined leader, whose rules all land on its slot whatever their kind — so it
        // is read off the definition, holder-less, and Lookup.UnitHas reads it the same way. Its effects therefore act on the whole unit.
        foreach (var (p, e) in Lookup.UnitKindOnSlots(state, ix.Properties, party.UnitId)) result.Add(new LiveProperty(side, e, p.Parameter, null, null, null));
        // "This model and its unit get X" (an Aura, Banner, Musician): one living carrier anywhere in the unit holds it for every party of the
        // unit, so a joined Hero's Aura reaches the weapons of the models it joined. Any other any entry ("its weapons get…") stays its party's.
        var living = Lookup.LivingModels(state, party.UnitId);
        var others = living.Except(models).ToList();
        var unitWide = others.Count == 0 ? [] : Lookup.ModelProperties(state, party.UnitId, others)
            .Where(x => ix.Properties.Entry(x.Property.Name) is { } e && IsUnitWide(e)).ToList();
        foreach (var group in Lookup.ModelProperties(state, party.UnitId, models).Concat(unitWide).GroupBy(x => x.Property.Name))
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
            .Select(g => WithFace(ix, g.First())).Concat(side == Side.Defender ? RemovedTokens(ix) : []);
    }

    /// <summary>The tokens the attacking team removed from the defender for this attack ("+AP(X)" / "+X to hit rolls … where X is the number of removed
    /// tokens"): per entry, one instance on the defender of its spent_marker row with the row's amount times the count. It names no grant, so nothing
    /// spends it; the tokens went when they were removed.</summary>
    static IEnumerable<LiveProperty> RemovedTokens(Interaction ix)
    {
        foreach (var (name, n) in ix.RemovedTokens ?? new Dictionary<string, int>())
            if (n > 0 && ix.Properties.Entry(name) is { Played: true } entry
                && entry.Effects.SelectMany(e => e.Kind == EffectKind.Roll ? e.Children : [e]).FirstOrDefault(e => e.Duration == EffectDuration.SpentMarker) is { } row)
                yield return new LiveProperty(Side.Defender, entry, null, null, null, null, row with { Amount = row.EffectiveAmount(null) * n, AmountFromParameter = false });
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
