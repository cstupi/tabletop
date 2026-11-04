using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfAttack
{
    public static Sequence DealWounds() => new(GfNames.DealWounds, [new AssignStep(), new GfReserves.SplitStep(), new DisgorgeStep(), new GfSpellSupport.TransferStep(), new GfMarkers.DestroyedStep(), new GfRuleHits.RetaliationStep()]);

    public static Interaction AttackInteraction(StepContext ctx, Guid? actor, Guid target, IReadOnlyList<Guid> weapons, AttackKind? kind, string? action) =>
        actor is { } a && kind == AttackKind.Spell && weapons.Count == 0
            ? AttackInteraction(ctx.State, ctx.Properties, ctx.Frame, actor, target, weapons, kind, action, GfCasting.WeaponRules(ctx, a, target)) with { SpellReach = GfCasting.Reach(ctx, target) }
            : AttackInteraction(ctx.State, ctx.Properties, ctx.Frame, actor, target, weapons, kind, action);

    /// <summary>
    /// The attack's interaction from state, properties and, inside a running sequence, its frame: the frame gives a charge's recorded range and the
    /// nearest shooting or melee frame's faces. With no frame (a question asked outside Advance, such as who can shoot) the range is the units'
    /// distance and there are no faces. `spellRules` is the spell's weapon rules, which only a step context can read.
    /// </summary>
    public static Interaction AttackInteraction(GameState state, PropertySet properties, FrameView? frame, Guid? actor, Guid target, IReadOnlyList<Guid> weapons,
        AttackKind? kind, string? action, IReadOnlyList<(string Entry, int? Parameter)>? spellRules = null)
    {
        if (actor is not { } a) return new Interaction(state, properties, Party.Whole(state, target), null, kind, action, null, Battlefield.TerrainUnder(state, target));
        var models = weapons.Count > 0 ? state.Projection.Models.Values.Where(m => m.Slot.Weapons.Any(w => weapons.Contains(w.Id))).Select(m => m.Id).ToList() : Lookup.LivingModels(state, a);
        return new Interaction(state, properties, Party.Of(state, a, models, weapons), Party.Whole(state, target), kind, action, ChargeRange(frame) ?? Battlefield.UnitDistance(state, a, target), Battlefield.TerrainUnder(state, target),
            Faces: FacesFor(frame, a, target), SpellRules: spellRules, AttackerTerrain: Battlefield.TerrainUnder(state, a));
    }

    /// <summary>"Charges enemies over 9" away", "charged from over 9" away": in the melee of a Charge, the distance the charge-target step recorded
    /// before the move (spell library, Grammar additions). Null outside a charge.</summary>
    internal static double? ChargeRange(FrameView? frame)
    {
        for (var f = frame; f is not null; f = f.Parent)
            if (f.Frame.Sequence == GfNames.ChargeSeq && f.Values.TryGetValue(GfNames.ChargeRangeKey, out var hundredths)) return hundredths / 100.0;
        return null;
    }

    /// <summary>p.8's steps; under GfEdition.DefenderChoice the defender's `attacked` choice follows determine-attacks (GfChoice.DefenderChoiceStep).</summary>
    public static Sequence AttackSequence(GfEdition e) => new(GfNames.AttackSequence, [new DetermineAttacksStep(), .. e.DefenderChoice ? [new GfChoice.DefenderChoiceStep()] : (IStep[])[], new PickTargetModelStep(), new RollToHitStep(), new ScoreHitsStep(), new PushStep("resolve", GfNames.ResolveHits), new SelfWoundsStep(), new AttackEndStep()]);

    /// <summary>The block and the wounds; under GfEdition.HasParry the Parry roll opens it (GfParry.ParryStep).</summary>
    public static Sequence ResolveHits(GfEdition e) => new(GfNames.ResolveHits, [.. e.HasParry ? [new GfParry.ParryStep()] : (IStep[])[], new RollToBlockStep(), new PushStep("deal", GfNames.DealWounds)]);

    /// <summary>The Limited weapons an attack-sequence used before the given frame started (the whole log without one): p.14 "may only be used once per game".</summary>
    public static HashSet<Guid> FiredLimited(GameState state, Guid? before)
    {
        var used = new HashSet<Guid>();
        foreach (var e in EventApplier.EffectiveLog(state.Applier.Log))
        {
            if (e.Payload is not SequenceStarted s) continue;
            if (s.FrameId == before) break;
            if (s.Sequence == GfNames.AttackSequence) used.UnionWith(s.Context.WeaponIds);
        }
        used.IntersectWith(state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).Where(w => w.Properties.Any(p => p.Name == "Limited")).Select(w => w.Id));
        return used;
    }

    /// <summary>A weapon an import added for a rule of its model is named after that rule (Takedown Shot, Takedown Strike): its attack is the rule acting.</summary>
    public static void ListRuleWeapons(StepContext ctx, IReadOnlyList<Guid> weapons)
    {
        foreach (var m in ctx.Projection.Models.Values)
            foreach (var w in m.Slot.Weapons.Where(w => weapons.Contains(w.Id) && m.Slot.Properties.Any(p => p.Name == w.Name)))
                ctx.Applied.Add(w.Name);
    }

    /// <summary>p.14 Takedown is a weapon property, so a group carries it when one of its weapons does. GfGroups reads this for WeaponGroup.Takedown, and splits such a group so each holds one model's weapon.</summary>
    public static bool IsTakedownGroup(GameState state, IReadOnlyList<Guid> weapons) =>
        state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).Where(w => weapons.Contains(w.Id)).Any(w => w.Properties.Any(p => p.Name == GfNames.Takedown));

    /// <summary>The attack kind of the frame's group: a weapon-less group under a spell-hits frame is a spell's hits (spell library → `spell`); a
    /// weapon-less group elsewhere is Impact's, which is melee; an attack-sequence the melee sequence pushed is melee, whatever its weapons
    /// (3.6.0 p.13 Assault strikes with a weapon that has a Range); otherwise the weapons decide.</summary>
    public static AttackKind KindOf(StepContext ctx)
    {
        for (var f = ctx.Frame; f is not null; f = f.Parent)
        {
            if (ctx.Frame.Context.WeaponIds.Count == 0 && f.Frame.Sequence == GfNames.SpellHitsSeq) return AttackKind.Spell;
            if (f.Frame.Sequence == GfNames.AttackSequence && f.Parent?.Frame.Sequence == GfNames.Melee) return AttackKind.Melee;
        }
        return KindOf(ctx.State, ctx.Frame.Context.WeaponIds);
    }

    /// <summary>The kind the weapons alone give: Shooting when one has a Range. A frame's kind is `KindOf(ctx)`, which knows a melee strike with an Assault weapon.</summary>
    public static AttackKind KindOf(GameState state, IReadOnlyList<Guid> weapons) =>
        state.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => weapons.Contains(w.Id))?.Stats.ContainsKey(GfNames.Range) == true ? AttackKind.Shooting : AttackKind.Melee;

    /// <summary>The attack-sequence frame was pushed by a melee: a melee frame was open when it started. Walks the effective log's frame stack.</summary>
    public static bool StartedInMelee(GameState state, Guid frameId)
    {
        var open = new List<(Guid Id, string Sequence)>();
        foreach (var e in EventApplier.EffectiveLog(state.Applier.Log))
            switch (e.Payload)
            {
                case SequenceStarted s when s.FrameId == frameId: return open.Count > 0 && open[^1].Sequence == GfNames.Melee;
                case SequenceStarted s: open.Add((s.FrameId, s.Sequence)); break;
                case SequenceFinished f: open.RemoveAll(x => x.Id == f.FrameId); break;
                case SequenceAbandoned a when open.FindIndex(x => x.Id == a.FrameId) is var i and >= 0: open.RemoveRange(i, open.Count - i); break;
            }
        return false;
    }

    /// <summary>p.9 who can strike, model to model: 3.5.1 "within 2” horizontally and 4” vertically"; 3.6.0 p.9 "All models that are within 3” of an
    /// enemy model from the target unit (horizontally or vertically), may attack it." Read from the game's edition.</summary>
    internal static bool WithinStrike(GameState state, Guid model, Guid enemy)
    {
        var edition = GfEdition.Of(state);
        return Battlefield.Distance(state, model, enemy) <= edition.MeleeHorizontal && Battlefield.Vertical(state, model, enemy) <= edition.MeleeVertical;
    }

    /// <summary>
    /// Attack counts keyed by the Quality value they are rolled at, read back off a frame's `Values`: `&lt;prefix&gt;&lt;n&gt;` holds the
    /// attacks tested against `n`+. `determine-attacks` writes `attacks_q&lt;n&gt;` on its own frame and `fire-groups` writes
    /// `attacks_&lt;i&gt;_q&lt;n&gt;` on the pushing one, so one reader serves both.
    /// </summary>
    public static IReadOnlyList<(int Quality, int Attacks)> ByQuality(IReadOnlyDictionary<string, int> values, string prefix) =>
        values.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(kv.Key.AsSpan(prefix.Length), out _))
              .Select(kv => (Quality: int.Parse(kv.Key[prefix.Length..]), Attacks: kv.Value))
              .OrderBy(x => x.Quality).ToList();

    public static int HitsFrom(FrameView parent)
    {
        var hitRolls = parent.Frame.Steps.SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.Hit).ToList();
        if (hitRolls.Count == 0) return (GfMelee.ImpactHits(parent) ?? parent.Value("hits")) * Math.Max(1, parent.Value("hit_multiplier", 1));
        return (hitRolls.Sum(Dice.RollOutcome.Successes) + parent.Value("extra_hits")) * Math.Max(1, parent.Value("hit_multiplier", 1));
    }

    /// <summary>The sequences an attack's once grants are held under (StepContext.Consume) and released at the end of.</summary>
    public static bool IsAttackRoot(string sequence) => sequence is GfNames.AttackSequence or GfNames.SpellHitsSeq or GfNames.RuleHitsSeq;

    /// <summary>
    /// "Next time the effect would apply" is the attack: every grant a step of this frame or of a frame it pushed read and held (`held_&lt;unit&gt;_&lt;grant&gt;`),
    /// with this run's own, released by Consume's rule (RollEffects.Release), so every row of a once entry applies across the attack and the entry goes once.
    /// </summary>
    public static IReadOnlyList<StateChange> ReleaseHeld(StepContext ctx)
    {
        var log = EventApplier.EffectiveLog(ctx.State.Applier.Log).ToList();
        var start = log.FindIndex(e => e.Payload is SequenceStarted s && s.FrameId == ctx.Frame.Id);
        var keys = log.Skip(Math.Max(0, start)).Select(e => e.Payload).OfType<StepCompleted>().SelectMany(s => s.Values?.Keys ?? []).Concat(ctx.Held.Keys);
        var held = new List<(Guid, Guid)>();
        foreach (var key in keys.Where(k => k.StartsWith(StepContext.HeldPrefix, StringComparison.Ordinal)).Distinct())
        {
            var ids = key[StepContext.HeldPrefix.Length..].Split('_');
            if (ids.Length == 2 && Guid.TryParseExact(ids[0], "N", out var unit) && Guid.TryParseExact(ids[1], "N", out var grant)) held.Add((unit, grant));
        }
        return RollEffects.Release(ctx.State, ctx.Properties, held);
    }

    /// <summary>What an attack's close writes: its held once grants go (ReleaseHeld), and the defender's `attacked` choice is cleared (GfChoice.ClearedAtPoint),
    /// so the next attack at it is asked again.</summary>
    public static IReadOnlyList<StateChange> Closing(StepContext ctx) =>
        [.. ReleaseHeld(ctx), .. ctx.Frame.Context.TargetUnitIds is [var defender, ..] && ctx.Projection.Units.ContainsKey(defender) ? GfChoice.ClearedAtPoint(ctx.State, ctx.Properties, defender, GfNames.Attacked) : []];

    /// <summary>Closes `attack-sequence`, `spell-hits` and `rule-hits` (Closing). A frame that finishes early closes in the finishing step.</summary>
    public sealed class AttackEndStep : IStep
    {
        public string Name => "attack-end";
        public StepOutcome Run(StepContext ctx) => new Complete(Closing(ctx));
    }

    /// <summary>Pushes a child once and completes when it has finished. Values pass through the frame stack, not through this step.</summary>
    public sealed class PushStep(string name, string sequence, Func<StepContext, SequenceContext>? context = null) : IStep
    {
        public string Name => name;
        public StepOutcome Run(StepContext ctx) => ctx.History.Children.Any(ch => ch.Finished || ch.Abandoned) ? new Complete([]) : new Push(sequence, context?.Invoke(ctx) ?? ctx.Frame.Context);
    }

    /// <summary>
    /// p.8's first step. The counts are not this frame's to compute when a pushing step already settled them: `fire-groups` fixes
    /// every group's attacks before the first shot, because p.8 fires a unit's weapons simultaneously, and melee's striking steps
    /// fix them before the first strike, because p.9 sums "how many attacks the unit has in total for this melee"; this reads
    /// `attacks_&lt;i&gt;_q&lt;n&gt;` back off the pushing frame. Only a push that recorded nothing is counted here and now.
    /// `eligible_models` is the live figure either way, so a recorded count standing over an empty eligible set is visible in the log.
    ///
    /// The counts are per Quality value, one key each, because p.4 tests every attack against *"the model's Quality value"* and a
    /// joined hero brings its own. `attacks` stays the total, which is what the log line and the readers that do
    /// not care about the split read. `defender_models` is the defending unit's living models, settled at the same moment and for
    /// the same reason, and carried here so `roll-to-hit` can cap `Blast` with it (p.13) and `roll-to-block` can decide whether
    /// p.14's joined hero is alone. `in_cover` is p.11's majority, settled at that same moment and carried here for
    /// `roll-to-block`. A melee push settles no `in_cover`, which melee does not read; a push that settled nothing falls back to the live reading.
    /// </summary>
    /// <summary>The attack's range as its interaction measured it, in hundredths of an inch, on determine-attacks (GfEdition.AttackRangeRecorded).</summary>
    public const string RangeKey = "range_hundredths";

    /// <summary>`granted_&lt;entry&gt;_&lt;amount&gt;`: the entry may hold underscores, the amount never does, so the split is at the last one.</summary>
    public static (string Entry, int Amount) ParseGrantKey(string key)
    {
        var cut = key.LastIndexOf('_');
        return (key["granted_".Length..cut], int.TryParse(key[(cut + 1)..], out var amount) ? amount : 0);
    }

    /// <summary>`granted_&lt;entry&gt;_&lt;amount&gt;` for one per-die grant on a hit; a hit carrying several is keyed by the whole set, `granted_&lt;a&gt;_&lt;n&gt;|&lt;b&gt;_&lt;m&gt;`,
    /// in name order, so it is counted in one key and blocked once. Null when the die carries none.</summary>
    public static string? GrantKey(IEnumerable<(string Entry, int? Amount)> grants) =>
        grants.Select(g => $"{g.Entry}_{g.Amount ?? 0}").Distinct().Order(StringComparer.Ordinal).ToList() is { Count: > 0 } set ? "granted_" + string.Join('|', set) : null;

    /// <summary>The grants a `granted_` key names: one for a single grant, several for a set.</summary>
    public static IReadOnlyList<(string Entry, int Amount)> ParseGrantSet(string key) =>
        key["granted_".Length..].Split('|').Select(m => ParseGrantKey("granted_" + m)).ToList();
}
