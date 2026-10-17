using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>What Home offers on a selected game: management takes End and Delete from the game's creator alone, and End only means
/// something before the game has ended.</summary>
public sealed record GameRowActions(bool CanEnd, bool CanDelete)
{
    public static GameRowActions Of(GameSummary? row, Guid? me) =>
        row is null || me is null || row.CreatedBy != me ? new(false, false) : new(row.Status != GameStatus.Ended, true);
}
