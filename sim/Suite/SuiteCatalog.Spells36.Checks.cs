using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The 3.6.0 spells group's checks of the effect kinds and granted rules the 3.6 books add: Shifting Form's remove_spell_tokens, and per weapon rule a
/// spell's hits carry or a spell grants (Pierce, Smash, Decimate, Scourge, Destructive, Enhanced Wolfborn, Enhanced Rapid Blink, Indirect), a check on a
/// spell that prints it, quoting the spell and the rule as the 3.6 books print them.
/// </summary>
public static partial class SuiteCatalog
{
    public const string RemoveTokensCheck36 = "remove_spell_tokens (Shifting Form: \"Pick one enemy unit within 18\\\" in line of sight, which loses 2 spell tokens. Note that the target may not spend those spell tokens to prevent this effect.\")";
    public const string PierceCheck36 = "grant_property Pierce (Death Bolt: \"which takes 4 hits with AP(1) and Pierce\"; Pierce: \"Ignores all positive Defense modifiers on the target\")";
    public const string SmashCheck36 = "grant_property Smash (Cerebral Trauma: \"which takes 6 hits with AP(1) and Smash\"; Smash: \"When attacking units over half of this weapon's range away, the target must re-roll unmodified Defense results of 6.\")";
    public const string DecimateCheck36 = "grant_property Decimate (Rapid Putrefaction: \"which takes 2 hits with AP(1) and Decimate\"; Decimate: \"When attacking units with more than half of their total size or Tough value (for single-model units), the target must re-roll unmodified Defense results of 6.\")";
    public const string ScourgeCheck36 = "grant_property Scourge (Smiting Rune: \"which takes 4 hits with Scourge\"; Scourge: \"For each unmodified result of 1 to block hits, enemies count as having taken 2 extra wounds\")";
    public const string DestructiveCheck36 = "grant_property Destructive (Light Fragments: \"which takes 6 hits with Destructive\"; Destructive: \"When attacking units with more than half of their total size or Tough value (for single-model units), those hits get AP(+1).\")";
    public const string WolfbornCheck36 = "grant_property Enhanced Wolfborn (Righteous Fury: \"Pick up to three friendly units within 18\\\", which get Enhanced Wolfborn once (next time the effect would apply).\"; Enhanced Wolfborn: \"If this model has Wolfborn, it may move by up to 2D3\\\" from Wolfborn (instead of only D3\\\").\")";
    public const string RapidBlinkCheck36 = "grant_property Enhanced Rapid Blink (Shadow Dance: \"Pick up to three friendly units within 18\\\", which get Enhanced Rapid Blink once (next time the effect would apply).\"; Enhanced Rapid Blink: \"If this model has Rapid Blink, it may move by up to 2D3\\\" from Rapid Blink (instead of only D3\\\").\")";
    public const string IndirectCheck36 = "grant_property Indirect (Triangulation Bots: \"Pick one enemy unit within 18\\\" in line of sight, which friendly units gets Indirect on their weapons against once (next time the effect would apply).\")";

    /// <summary>The spell tokens a model held before a seq: the last SpellTokensSet on it, 0 before any.</summary>
    static int TokensBefore(LogQueries q, Guid model, long seq) =>
        q.Events.TakeWhile(e => e.Seq < seq).SelectMany(e => LogQueries.ChangesOf(e.Payload)).OfType<SpellTokensSet>().LastOrDefault(t => t.ModelId == model)?.Tokens ?? 0;

    /// <summary>
    /// Shifting Form at a target army of Caster(2) models: each resolved cast's effects step takes 2 tokens off its target unit, or every token it holds
    /// when it holds fewer, and touches no model of another unit; at least one target held 2 or more.
    /// </summary>
    static string? ShiftingForm(LogQueries q)
    {
        var cases = SpellGames.Casts(q, "Shifting Form").Where(c => c.Passed).SelectMany(c => c.Frame.Steps.Where(s => s.Name == "effects")
            .SelectMany(s => c.Targets.Select(t => (Step: s, Target: t)))).ToList();
        if (!cases.Any(c => q.Final.Units[c.Target].Definition.Models.Sum(m => TokensBefore(q, m.Id, c.Step.Seq)) >= 2))
            return "no Shifting Form target held 2 spell tokens";
        return LogQueries.Each(cases, "Shifting Form target", c =>
        {
            var models = q.Final.Units[c.Target].Definition.Models.Select(m => m.Id).ToHashSet();
            var sets = c.Step.Step.Changes.OfType<SpellTokensSet>().ToList();
            if (sets.Any(t => !models.Contains(t.ModelId))) return "tokens set on a model outside the target unit";
            var before = models.Sum(m => TokensBefore(q, m, c.Step.Seq));
            var after = models.Sum(m => sets.LastOrDefault(t => t.ModelId == m)?.Tokens ?? TokensBefore(q, m, c.Step.Seq));
            return before - after == Math.Min(2, before) ? null : $"the target held {before} tokens and lost {before - after}";
        });
    }

    /// <summary>
    /// Death Bolt at targets with Sturdy and Enhanced Sturdy ("they always get +1 to Defense rolls from Sturdy"): every block of its hits carries no positive
    /// modifier and lists one ignored by Pierce, at the spell's AP(1).
    /// </summary>
    static string? DeathBolt(LogQueries q) => LogQueries.Each(SpellHits(q, "Death Bolt").SelectMany(h => h.Blocks), "Death Bolt block", r =>
    {
        var plus = r.Roll.Modifiers.Where(m => m.Amount > 0).ToList();
        if (plus.Count > 0) return $"a block under {string.Join(", ", plus.Select(m => $"{m.Source} +{m.Amount}"))}";
        if (!(r.Roll.Ignored ?? []).Any(i => i.By == "Pierce" && i.Amount > 0)) return "no positive Defense modifier ignored by Pierce";
        return r.Roll.Modifiers.Where(m => m.Source == "AP").Select(m => m.Amount).SequenceEqual([-1]) ? null : $"blocks under AP [{string.Join(",", r.Roll.Modifiers.Where(m => m.Source == "AP").Select(m => m.Amount))}]";
    });

    /// <summary>
    /// Cerebral Trauma: its hits are a spell's, no weapon's, so "over half of this weapon's range away" has no range to read (the `weapon_range` predicate fails
    /// a source with no weapon) and no 6 is re-rolled; every block is at the spell's AP(1); at least one block die showed a 6.
    /// </summary>
    static string? CerebralTrauma(LogQueries q)
    {
        var blocks = SpellHits(q, "Cerebral Trauma").SelectMany(h => h.Blocks).ToList();
        if (!blocks.Any(r => r.Roll.Dice.Any(d => d.Raw == 6))) return "no Cerebral Trauma block rolled a 6";
        return LogQueries.Each(blocks, "Cerebral Trauma block", r =>
            r.Rerolls.Count > 0 ? $"re-rolled {LogQueries.Show(r)}"
            : r.Roll.Modifiers.Where(m => m.Source == "AP").Select(m => m.Amount).SequenceEqual([-1]) ? null : "a block not at AP(1)");
    }

    /// <summary>
    /// "More than half of their total size or Tough value (for single-model units)" at a single-model Tough(8) target: more than 4 of its 8 wounds left
    /// as the block is rolled.
    /// </summary>
    static bool OverHalf8(LogQueries q, LoggedFrame hits, LoggedRoll block)
    {
        var model = q.Final.Units[hits.Context.TargetUnitIds[0]].Definition.Models.Single().Id;
        return (8 - q.WoundsOn(model, block.Step.Seq)) * 100 > 50 * 8;
    }

    /// <summary>
    /// Rapid Putrefaction from six casters at single-model Tough(8) targets: a block at a target over half re-rolls exactly its unmodified 6s as rolled,
    /// one at or under half (target unit 1 after the two fixed blocks' four wounds) re-rolls none; at least one block of each, and one over half with a 6.
    /// </summary>
    static string? RapidPutrefaction(LogQueries q)
    {
        var cases = SpellHits(q, "Rapid Putrefaction").SelectMany(h => h.Blocks.Select(b => (Block: b, Over: OverHalf8(q, h.Hits, b)))).ToList();
        if (!cases.Any(x => x.Over && x.Block.Roll.Dice.Any(d => d.Raw == 6))) return "no block over half rolled a 6";
        if (!cases.Any(x => !x.Over)) return "no block at a target at or under half";
        return LogQueries.Each(cases, "Rapid Putrefaction block", x =>
        {
            var expected = x.Block.Roll.Dice.Select((d, i) => (d, i)).Where(p => x.Over && p.d.Raw == 6).Select(p => p.i).ToList();
            var rerolled = x.Block.Rerolls.SelectMany(r => r.Replaced).Select(r => r.Index).Order().ToList();
            return expected.SequenceEqual(rerolled) ? null : $"re-rolled [{string.Join(",", rerolled)}] of [{string.Join(" ", x.Block.Roll.Dice.Select(d => d.Raw))}] at {(x.Over ? "over" : "at or under")} half";
        });
    }

    /// <summary>
    /// Smiting Rune: every block step maps Scourge's count as 2 wounds for a die showing 1 and for no other face (`counted_&lt;roll&gt;_&lt;face&gt;_&lt;entry&gt;`; a
    /// spell's hits are neither melee nor shooting, so neither reader the text names counts them), and its counted wounds as rolled are 2 per unmodified 1.
    /// </summary>
    static string? SmitingRune(LogQueries q) => LogQueries.Each(SpellHits(q, "Smiting Rune").SelectMany(h => h.Blocks.Select(b => b.Step)).Distinct(), "Smiting Rune block", s =>
    {
        var map = (s.Step.Values ?? new Dictionary<string, int>()).Where(kv => kv.Key.StartsWith("counted_", StringComparison.Ordinal) && kv.Key != "counted_wounds")
            .Select(kv => $"{kv.Key}={kv.Value}").ToList();
        if (!map.SequenceEqual(["counted_0_1_Scourge=2"])) return $"counts [{string.Join(", ", map)}], not 2 for a 1";
        var ones = s.Step.Rolls.Where(r => r.Roll == "defense").Sum(r => r.Dice.Count(d => d.Raw == 1));
        return s.Value("counted_wounds") == 2 * ones ? null : $"counted {s.Value("counted_wounds")} as rolled for {ones} 1s";
    });

    /// <summary>
    /// Light Fragments from six casters at single-model Tough(8) targets: every block at a target over half is at AP(1), at one at or under half (target
    /// unit 1 after the fixed block's six wounds) at no AP; at least one of each.
    /// </summary>
    static string? LightFragments(LogQueries q)
    {
        var cases = SpellHits(q, "Light Fragments").SelectMany(h => h.Blocks.Select(b => (Block: b, Over: OverHalf8(q, h.Hits, b)))).ToList();
        if (!cases.Any(x => x.Over)) return "no block at a target over half";
        if (!cases.Any(x => !x.Over)) return "no block at a target at or under half";
        return LogQueries.Each(cases, "Light Fragments block", x =>
        {
            var ap = x.Block.Roll.Modifiers.Where(m => m.Source == "AP" && m.Amount != 0).Select(m => m.Amount).ToList();
            return ap.SequenceEqual(x.Over ? [-1] : []) ? null : $"AP [{string.Join(",", ap)}] at {(x.Over ? "over" : "at or under")} half";
        });
    }

    /// <summary>
    /// A spell granting a "+D3" boost to a rolled move every unit of the book's army carries: each of the move's rolls is 2 dice while the unit holds the
    /// spell's grant and 1 die otherwise; a boosted move that moved a model spends the grant ("once"), so the unit holds none after it; at least one
    /// boosted move that moved and one unboosted move.
    /// </summary>
    static string? BoostedMove(LogQueries q, string spell, string move)
    {
        var moves = q.FramesOf("reposition").Where(f => f.Context.Action == move && f.Steps.Count > 0).Select(f => (Frame: f, Unit: f.Context.ActorUnitId!.Value,
            Held: Held(q, f.Context.ActorUnitId!.Value, spell, f.Seq), Dice: f.Steps.SelectMany(s => s.Rolls(move)).Sum(r => r.Dice.Count),
            Moved: f.Steps.Any(s => s.Step.Changes.OfType<ModelsMoved>().Any()), After: f.Steps[^1].Seq + 1)).ToList();
        if (!moves.Any(m => m.Held && m.Moved)) return $"no {move} move made while holding {spell}";
        if (!moves.Any(m => !m.Held)) return $"no {move} move made without {spell}";
        return LogQueries.Each(moves, $"{move} move", m =>
            m.Dice != (m.Held ? 2 : 1) ? $"{m.Dice} dice {(m.Held ? "holding" : "not holding")} {spell}"
            : m.Held && m.Moved && Held(q, m.Unit, spell, m.After) ? $"{spell} still held after the boosted move" : null);
    }

    /// <summary>A once-grant spell: each resolved cast writes one grant of the spell on each target not already holding one, and none on a target that does.</summary>
    static string? GrantWritten(LogQueries q, string spell) => LogQueries.Each(
        SpellGames.Casts(q, spell).Where(c => c.Passed).SelectMany(c => c.Targets.Select(t => (Cast: c, Target: t, Held: Held(q, t, spell, c.Frame.Seq)))),
        $"{spell} target", c =>
        {
            var written = c.Cast.Grants.Count(g => g.UnitId == c.Target && g.Grant.EntryName == spell);
            return written == (c.Held ? 0 : 1) ? null : $"{written} grants on a target that {(c.Held ? "already held" : "did not hold")} it";
        });
}
