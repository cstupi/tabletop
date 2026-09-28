using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>The activate Ask's option id names the unit and the action, so an out-of-set answer still says what it meant. Declared here
/// with the seam because GfHooks.OnTurnEnded (Task 11) reads Idle; the scheduler plan's Task 1 tests it and adds nothing.</summary>
public static class ActivationOption
{
    /// <summary>Not an action row: a Shaken unit's only activation (page 10), resolved by the scheduler with no sequence.</summary>
    public const string Idle = "Idle";

    public static string Id(Guid unitId, string action) => $"{unitId:N}/{action}";

    public static bool TryParse(string id, out Guid unitId, out string action)
    {
        unitId = default; action = "";
        var slash = id.IndexOf('/');
        if (slash != 32 || slash == id.Length - 1) return false;
        if (!Guid.TryParseExact(id[..slash], "N", out unitId)) return false;
        action = id[(slash + 1)..];
        return true;
    }

    public static AskOption For(UnitInPlay unit, string action) => new(Id(unit.Id, action), $"{unit.Definition.Name}: {action}", [unit.Id]);
}
