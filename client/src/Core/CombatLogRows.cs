using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>What a log row shows besides its words.</summary>
public static class CombatLogRows
{
    /// <summary>The team ordinal whose band leads a block's header: its actor's team; 0 for a block with no actor of a known team and any other entry.</summary>
    public static int Band(LogEntry e, Names n, GameInfo game) =>
        e is ActivationBlock { Actor: { } unit } && n.TeamOfUnit(unit) is { } team && game.Teams.FirstOrDefault(t => t.Id == team) is { } t ? t.Ordinal : 0;
}
