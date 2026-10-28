using Tabletop.Content;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Setup;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>
/// What an override command may carry (security review H4, L4). Nothing here is a rule: these are the bounds of a well-formed edit, so
/// that what the menu writes into the log can be read back by every later step. A name the engine looks up (an action, a grant's entry
/// and effect) must exist in the game's property set; a condition is a free name by design (game flow: "anything else the unit currently
/// is"), so it is bounded rather than looked up. A position is finite and near the table, as GfAnswers.Moves holds a move answer.
/// </summary>
public static class MenuLimits
{
    public const int Name = 64, Number = 10_000, Rounds = 100, Uses = 100;

    public static string? Breach(GameState state, Command command, PropertySet? properties) => command switch
    {
        SetCondition c when string.IsNullOrWhiteSpace(c.Condition) || c.Condition.Length > Name => $"a condition is 1–{Name} characters",
        RecordAction a when properties is not null && properties.Actions.All(x => x.Name != a.Action) => $"no action '{Clip(a.Action)}' in this game's rules",
        RecordAction a when a.Action.Length > Name => $"an action is at most {Name} characters",
        AddGrant g when properties is not null && properties.Entry(g.Grant.EntryName) is null => $"no rule '{Clip(g.Grant.EntryName)}' in this game's rules",
        AddGrant g when properties is not null && properties.EffectById(g.Grant.EffectId) is null => $"no effect {g.Grant.EffectId} in this game's rules",
        AddGrant g when !state.Projection.Units.ContainsKey(g.Grant.CasterUnitId) => $"no unit {g.Grant.CasterUnitId}",
        AddGrant g when g.Grant.RemainingUses is < 0 or > Uses => $"a grant has 0–{Uses} uses",
        MoveMarker m => OnTable(state, m.Position, m.Yaw) ? null : "a marker lands at a finite point near the table",
        MoveModels m when m.Moves.Any(x => x.To is OnTable t && !OnTable(state, t.Placement.Position, t.Placement.Yaw)) => "a model lands at a finite point near the table",
        SetWounds w when w.Wounds is < 0 or > Number => $"wounds are 0–{Number}",
        SetWoundsThisActivation w when w.Wounds is < 0 or > Number => $"wounds are 0–{Number}",
        SetSpellTokens t when t.Tokens is < 0 or > Number => $"tokens are 0–{Number}",
        SetScore s when s.Score is < -Number or > Number => $"a score is within ±{Number}",
        SetRound r when r.Round is < 0 or > Rounds => $"a round is 0–{Rounds}",
        _ => null,
    };

    /// <summary>Finite, and within the table's diagonal of its centre with a height no volume reaches past: GfAnswers.Moves' bound (no
    /// landing farther than the diagonal), taken from the table rather than the model, as a hand move may start on the shelf. Where on the
    /// table a placement is legal is the rules' question (PlacementBreach, enforcement), not this one.</summary>
    static bool OnTable(GameState state, Point3 p, Yaw yaw)
    {
        var size = state.Board.Table.Size;
        return double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z) && double.IsFinite(yaw.Degrees) && Math.Abs(p.Z) <= DraftLimits.MaxVolumeHeight * 2
            && p.Horizontal.DistanceTo(new Point2(size.Width / 2, size.Length / 2)) <= Math.Sqrt(size.Width * size.Width + size.Length * size.Length);
    }

    static string Clip(string s) => s.Length <= Name ? s : s[..Name] + "…";
}
