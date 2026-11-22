using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A team's chip in the top bar: its colour by ordinal, its title, its state and the state's icon; the tooltip names its players.</summary>
public sealed record TeamChip(Guid TeamId, int Ordinal, string Title, ColumnState State, string Icon, string Tooltip);

/// <summary>
/// The top bar of lobby and setup (spec 2026-11-21-setup-screen-layout-design.md → Top bar): a chip per team, the Still to do lines that lack a
/// player or an army with their count (null at none), and the creator's one action (Start setup, End setup with the pieces off the table it will
/// remove) or, for everyone else, who takes it.
/// </summary>
public sealed record TopBarView(IReadOnlyList<TeamChip> Teams, IReadOnlyList<ToDoLine> ToDo, string? ToDoText, string? Action, string? ActionTip,
    LobbyEdit? Edit, string? Waiting)
{
    /// <summary>Null outside lobby and setup.</summary>
    public static TopBarView? Of(GameInfo game, bool isCreator, TableState? draft)
    {
        if (game.Status is not (GameStatus.Lobby or GameStatus.Setup)) return null;
        var columns = TeamColumns.Of(game, null, isCreator).Where(c => c.TeamId is not null);
        var chips = columns.Select(c => new TeamChip(c.TeamId!.Value, c.Ordinal, c.Title, c.State, c.State == ColumnState.Ready ? "ready" : "warning",
            c.Members.Count == 0 ? $"{c.Title}: {c.StateText}" : $"{c.Title}: {c.StateText} · {TeamColumns.Line(c)}")).ToList();
        var todo = TeamColumns.StillToDo(game, isCreator).Where(l => l.Missing).ToList();
        var who = TeamColumns.CreatorName(game);
        var lobby = game.Status == GameStatus.Lobby;
        string? action = null, tip = null, waiting = null; LobbyEdit? edit = null;
        if (isCreator && lobby) { action = tip = "Start setup"; edit = LobbyEdits.StartSetup(); }
        else if (isCreator)
        {
            var off = draft is { } d ? d.Terrain.Count(t => !Tabletop.Geometry.Table.ContainsPoint(d.Size, t.Position.Horizontal)) : 0;
            action = off == 0 ? "End setup" : $"End setup ({off} off the table)";
            tip = LobbyView.EndSetupText(draft); edit = LobbyEdits.EndSetup();
        }
        else waiting = lobby ? $"{who} starts setup" : $"{who} ends setup";
        return new(chips, todo, todo.Count == 0 ? null : $"{todo.Count} to do", action, tip, edit, waiting);
    }
}

/// <summary>The game menu's items, top to bottom.</summary>
public enum GameMenuItem { Resume, Options, Leave, Home }

/// <summary>What an Esc press does in lobby, setup and play: nothing here (Pass), open the game menu, or close it.</summary>
public enum EscPress { Pass, Open, Close }

/// <summary>Everything that may use an Esc before the game menu: a text field with the key, an overlay (Options, the key overlay, the Game
/// over message), the open Ask's Cancel (clear targets, reset a move or a deploy, widen a narrowed choice), the table's selection, held
/// pieces (a drag, a box), picked models and a measurement.</summary>
public sealed record EscState(bool MenuOpen = false, bool TextFocus = false, bool Overlay = false, bool AskCancel = false, bool Selection = false,
    bool Holding = false, bool Picks = false, bool Measuring = false);

/// <summary>The game menu (spec → Game menu): ☰, and Esc when nothing else takes Esc.</summary>
public static class GameMenuView
{
    /// <summary>Leave game only for a participant: it gives up the seat. Not while watching (a replay, an account with no team).</summary>
    public static bool CanLeave(bool participant, bool watching) => participant && !watching;

    /// <summary>Leave game only when <see cref="CanLeave"/>.</summary>
    public static IReadOnlyList<GameMenuItem> Items(bool canLeave) =>
        canLeave ? [GameMenuItem.Resume, GameMenuItem.Options, GameMenuItem.Leave, GameMenuItem.Home] : [GameMenuItem.Resume, GameMenuItem.Options, GameMenuItem.Home];

    public static string Text(GameMenuItem item) => item switch
    {
        GameMenuItem.Resume => "Resume", GameMenuItem.Options => "Options", GameMenuItem.Leave => "Leave game", _ => "Home",
    };

    /// <summary>The open menu closes on Esc; a closed one opens only when the Esc would do nothing else.</summary>
    public static EscPress OnEsc(EscState s) =>
        s.MenuOpen ? EscPress.Close
        : s.TextFocus || s.Overlay || s.AskCancel || s.Selection || s.Holding || s.Picks || s.Measuring ? EscPress.Pass
        : EscPress.Open;
}
