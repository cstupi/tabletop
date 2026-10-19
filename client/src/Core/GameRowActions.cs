using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>What Home offers on a selected game: management takes End and Delete from the game's creator alone, and End only means
/// something before the game has ended. Replay: a game this account is in that has a log, running or ended.</summary>
public sealed record GameRowActions(bool CanEnd, bool CanDelete, bool CanReplay = false)
{
    public static GameRowActions Of(GameSummary? row, Guid? me)
    {
        if (row is null || me is null) return new(false, false);
        var replay = row.Joined && row.Status is GameStatus.Playing or GameStatus.Ended;
        return row.CreatedBy != me ? new(false, false, replay) : new(row.Status != GameStatus.Ended, true, replay);
    }
}
