using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A team band on the loading curtain: its ordinal (the colour) and "Team 1 · Clay, Sam".</summary>
public sealed record CurtainTeam(int Ordinal, string Text);

/// <summary>The curtain's game line: the name, the status chip's word, the teams.</summary>
public sealed record CurtainHeadView(string Name, string Status, IReadOnlyList<CurtainTeam> Teams);

/// <summary>Spec 2026-11-07-ux-redesign-1-client-shell.md → Loading: the curtain's game name line carries the status chip and the team bands, so
/// the first thing a player sees is who plays. Null before the game's info arrives.</summary>
public static class CurtainHead
{
    public static string StatusWord(GameStatus status) => status switch
    {
        GameStatus.Lobby => "Lobby", GameStatus.Setup => "Setup", GameStatus.Playing => "Playing", _ => "Ended",
    };

    public static CurtainHeadView? Of(GameInfo? game) => game is null ? null : new(game.Name, StatusWord(game.Status),
        game.Teams.OrderBy(t => t.Ordinal).Select(t =>
        {
            var names = game.Participants.Where(p => p.TeamId == t.Id).Select(p => p.Name).Distinct().ToList();
            return new CurtainTeam(t.Ordinal, names.Count == 0 ? $"Team {t.Ordinal}" : $"Team {t.Ordinal} · {string.Join(", ", names)}");
        }).ToList());
}
