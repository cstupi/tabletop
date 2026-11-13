using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

public sealed record DieContext(string Roll, int Raw, int Modified, bool Success);

/// <summary>The thirteen predicates. A subject-bearing predicate tests the side it names relative to the holder; relational ones test the interaction.
/// The live set, when the caller has one, is what weapon_range reads the source weapon's range from.</summary>
public static class Conditions
{
    /// <summary>roll_result's optional third value: the die must have succeeded (`success`) or failed (`fail`), "successful unmodified results of 5-6".</summary>
    public const string Success = "success", Fail = "fail";

    public static bool Hold(Interaction ix, Side holder, Effect effect, LiveProperty source, DieContext? die, LiveSet? live = null) =>
        effect.Conditions.All(c => Holds(ix, holder, c, source, die, live));

    public static bool Holds(Interaction ix, Side holder, EffectCondition c, LiveProperty source, DieContext? die, LiveSet? live = null)
    {
        var side = c.Subject == EffectSubject.Opponent ? Interaction.Other(holder) : holder;
        switch (c.Kind)
        {
            case ConditionKind.ActionTaken: return ix.ActionTaken is { } a && Membership(c, a);
            case ConditionKind.AttackKind: return ix.Attack is { } k && Membership(c, k switch { AttackKind.Shooting => "shooting", AttackKind.Melee => "melee", _ => "spell" });
            // p.5 measures base to base: the measured distance is compared as it is, never rounded ("over 9\"" holds at 9.41").
            case ConditionKind.Range: return ix.RangeInches is { } r && c.Compare(r);
            // "Over half of this weapon's range away": the measured range against a percentage of the source weapon's range. A source with no weapon
            // reads a spell's hits' reach (its printed range, the caster's distance), and fails outside one.
            case ConditionKind.WeaponRange:
                if (c.Number is not { } percent) return false;
                if (source.WeaponId is not { } weapon) return ix.SpellReach is { } reach && Compare(c.Comparison, reach.Distance * 100, percent * reach.Range);
                return ix.RangeInches is { } measured && WeaponRange(ix, source.Holder, weapon, live) is { } range && Compare(c.Comparison, measured * 100, percent * range);
            case ConditionKind.RollResult:
                if (die is null || c.Values.Count is not (2 or 3) || die.Roll != c.Values[0]) return false;
                if (c.Values.Count == 3 && die.Success != (c.Values[2] == Success)) return false;
                return c.Compare(c.Values[1] == "raw" ? die.Raw : die.Modified);
            // A subject-bearing predicate on a side the interaction lacks (the defender of a lone roll, a charge's reach) cannot hold.
            case ConditionKind.ActorState or ConditionKind.HoldsProperty or ConditionKind.MostModels or ConditionKind.RemainingSize when side == Side.Defender && ix.Defender is null: return false;
            case ConditionKind.ActorState:
            {
                var unit = ix.State.Projection.Units[ix.Of(side).UnitId];
                var present = c.Values.Any(v => unit.Conditions.Contains(v) || (source.ModelId is { } m && ix.Of(side).ModelIds.Contains(m) && ix.State.Projection.Models[m].Conditions.Contains(v)));
                return c.Comparison == Comparison.Neq ? !present : present;
            }
            // The terrain list describes the defender, or the lone party when there is no defender (a resolve check, a move, a hazard roll).
            // The attacker's own terrain, where the interaction reads it (an attack: "has most of them within 1\" of terrain, they get +1 to hit").
            case ConditionKind.PositionTerrain when side == Side.Attacker && ix.Defender is not null && ix.AttackerTerrain is { } own: return Membership(c, own, true);
            case ConditionKind.PositionTerrain: return Membership(c, ix.DefenderTerrain, ix.Defender is null || side == Side.Defender);
            case ConditionKind.RoundNumber: return c.Compare(ix.Round);
            case ConditionKind.UsageCount: return c.Compare(Usage(ix, source, c));
            // A grant row's at_point said when its rule was used; the grant is that use, so wherever the grant is read the point has passed
            // (army-book rules spec → the pick). Any other row holds only at its point.
            case ConditionKind.AtPoint: return source.GrantRow is not null || (ix.Point is { } point && Membership(c, point));
            // The side holds the entry under its own aggregation, a granted instance included; a suppression does not unhold (spell library spec).
            // "If this model has X" is judged over the side's party, the models whose rules the live set gathered: an attack's carriers, so a joined
            // Hero without X leaves the squad's Enhanced rule standing.
            case ConditionKind.HoldsProperty: return c.Values.Count == 1 && Lookup.PartyHas(ix.State, ix.Properties, ix.Of(side), c.Values[0]);
            case ConditionKind.MostModels: return c.Values.Count == 1 && MostModels(ix, side, c);
            // Remaining size against starting size, both as Lookup.RemainingSize counts them; the value names the rule that is one model's size.
            case ConditionKind.RemainingSize:
            {
                if (c.Values.Count != 1 || c.Number is not { } share) return false;
                var (remaining, starting) = Lookup.RemainingSize(ix.State, ix.Of(side).UnitId, c.Values[0]);
                return Compare(c.Comparison, remaining * 100.0, (double)share * starting);
            }
            default: return false;
        }
    }

    /// <summary>"Most models have ...": more than half the side's living models meet the comparison. A model stat compares its target number, read off
    /// the slot or the unit; a rule compares its parameter, and a model without the rule fails.</summary>
    static bool MostModels(Interaction ix, Side side, EffectCondition c)
    {
        var state = ix.State; var unit = state.Projection.Units[ix.Of(side).UnitId]; var name = c.Values[0];
        var models = Lookup.LivingModels(state, unit.Id);
        if (models.Count == 0) return false;
        var isStat = ix.Properties.Stat(StatLevel.Model, name) is not null || ix.Properties.Stat(StatLevel.Unit, name) is not null;
        int? Value(Guid m)
        {
            if (!isStat) return Lookup.ModelParameter(state, m, name);
            var text = state.Projection.Models[m].Slot.Stats.TryGetValue(name, out var own) ? own : unit.Definition.Stats.GetValueOrDefault(name);
            return text is null ? null : Tabletop.Content.StatValue.Read(Tabletop.Content.StatKind.TargetNumber, text).TargetNumber;
        }
        return models.Count(m => Value(m) is { } v && c.Compare(v)) * 2 > models.Count;
    }

    /// <summary>The source weapon's range as the live set leaves it (StatReader.PeekWeaponRange), or as printed when there is none (pass one). Null for a weapon with no range.</summary>
    static double? WeaponRange(Interaction ix, Side holder, Guid weaponId, LiveSet? live)
    {
        if (live is not null) return StatReader.PeekWeaponRange(live, holder, weaponId);
        var weapon = ix.State.Projection.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => w.Id == weaponId);
        return weapon is not null && weapon.Stats.TryGetValue("Range", out var text) ? Tabletop.Content.StatValue.Read(Tabletop.Content.StatKind.Distance, text).Inches : null;
    }

    static bool Compare(Comparison cmp, double left, double right) => cmp switch
    {
        Comparison.Eq => left == right, Comparison.Neq => left != right, Comparison.Gte => left >= right,
        Comparison.Lte => left <= right, Comparison.Gt => left > right, Comparison.Lt => left < right, _ => false,
    };

    static bool Membership(EffectCondition c, string actual) => c.Comparison == Comparison.Neq ? !c.Values.Contains(actual) : c.Values.Contains(actual);
    static bool Membership(EffectCondition c, IReadOnlyList<string> actual, bool applies) => applies && (c.Comparison == Comparison.Neq ? !actual.Intersect(c.Values).Any() : actual.Intersect(c.Values).Any());

    /// <summary>How many times the rule has been used. A weapon rule with no scope counts the sequences that used the weapon (Limited, "once per game").
    /// A scope of `round` or `activation` counts the sequences this rule started for its holder's unit (its name is their Action) since the
    /// last round_started, or since the unit's activation began (its last action_recorded) (army-rule hooks spec), or with `game` over the whole log ("once per game").</summary>
    static int Usage(Interaction ix, LiveProperty source, EffectCondition c)
    {
        var log = EventApplier.EffectiveLog(ix.State.Applier.Log).ToList();
        if (c.Values.FirstOrDefault() is "round" or "activation" or "game")
        {
            var unit = ix.Of(source.Holder).UnitId;
            var from = c.Values[0] switch
            {
                "round" => log.FindLastIndex(e => e.Payload is RoundStarted),
                "activation" => log.FindLastIndex(e => e.Payload is ActionRecorded a && a.UnitId == unit),
                _ => 0,
            };
            return log.Skip(Math.Max(0, from)).Count(e => e.Payload is SequenceStarted s && s.Context.Action == source.Entry.Name && s.Context.ActorUnitId == unit);
        }
        return source.WeaponId is not { } w ? 0 : log.Count(e => e.Payload is SequenceStarted s && s.Context.WeaponIds.Contains(w));
    }
}
