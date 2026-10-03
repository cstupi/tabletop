using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Effects;

public sealed record DieContext(string Roll, int Raw, int Modified);

/// <summary>The eight predicates. A subject-bearing predicate tests the side it names relative to the holder; relational ones test the interaction.</summary>
public static class Conditions
{
    public static bool Hold(Interaction ix, Side holder, Effect effect, LiveProperty source, DieContext? die) =>
        effect.Conditions.All(c => Holds(ix, holder, c, source, die));

    public static bool Holds(Interaction ix, Side holder, EffectCondition c, LiveProperty source, DieContext? die)
    {
        var side = c.Subject == EffectSubject.Opponent ? Interaction.Other(holder) : holder;
        switch (c.Kind)
        {
            case ConditionKind.ActionTaken: return ix.ActionTaken is { } a && Membership(c, a);
            case ConditionKind.AttackKind: return ix.Attack is { } k && Membership(c, k switch { AttackKind.Shooting => "shooting", AttackKind.Melee => "melee", _ => "spell" });
            case ConditionKind.Range: return ix.RangeInches is { } r && c.Compare((int)Math.Floor(r));
            case ConditionKind.RollResult:
                if (die is null || c.Values.Count != 2 || die.Roll != c.Values[0]) return false;
                return c.Compare(c.Values[1] == "raw" ? die.Raw : die.Modified);
            case ConditionKind.ActorState:
            {
                var unit = ix.State.Projection.Units[ix.Of(side).UnitId];
                var present = c.Values.Any(v => unit.Conditions.Contains(v) || (source.ModelId is { } m && ix.Of(side).ModelIds.Contains(m) && ix.State.Projection.Models[m].Conditions.Contains(v)));
                return c.Comparison == Comparison.Neq ? !present : present;
            }
            // The terrain list describes the defender, or the lone party when there is no defender (a resolve check, a move, a hazard roll).
            case ConditionKind.PositionTerrain: return Membership(c, ix.DefenderTerrain, ix.Defender is null || side == Side.Defender);
            case ConditionKind.RoundNumber: return c.Compare(ix.Round);
            case ConditionKind.UsageCount: return c.Compare(Usage(ix, source));
            default: return false;
        }
    }

    static bool Membership(EffectCondition c, string actual) => c.Comparison == Comparison.Neq ? !c.Values.Contains(actual) : c.Values.Contains(actual);
    static bool Membership(EffectCondition c, IReadOnlyList<string> actual, bool applies) => applies && (c.Comparison == Comparison.Neq ? !actual.Intersect(c.Values).Any() : actual.Intersect(c.Values).Any());

    /// <summary>How many sequences in the live log have used this weapon: the usage record a per-use limit reads.</summary>
    static int Usage(Interaction ix, LiveProperty source) => source.WeaponId is not { } w ? 0
        : EventApplier.EffectiveLog(ix.State.Applier.Log).Count(e => e.Payload is SequenceStarted s && s.Context.WeaponIds.Contains(w));
}
