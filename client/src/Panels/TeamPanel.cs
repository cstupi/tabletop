using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

public partial class TeamPanel : PanelContainer
{
    public Team Team { get; }
    readonly Label _text = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    public event Action<Vector2>? RightClicked;
    public TeamPanel(Team team) { Team = team; AddChild(_text); Recolour(); }

    /// <summary>The panel, not its text, tinted by the team colour; lightened so a light panel keeps the theme's dark text readable.</summary>
    public void Recolour() => SelfModulate = Palette.Team(Team.Ordinal).Lightened(0.6f);

    public void Refresh(Session s)
    {
        var score = s.Projection?.Scores.GetValueOrDefault(Team.Id);
        var held = s.Projection?.Control.Count(kv => kv.Value == Team.Id) ?? 0;
        var active = s.Projection?.Scheduler.ActiveTeamId == Team.Id ? "  ◀ active" : "";
        _text.Text = $"Team {Team.Ordinal}{(score is null ? "" : $"  score {score}")}{(held == 0 ? "" : $"  held {held}")}{active}\n{(s.Game is { } g ? LobbyView.Members(g, Team.Id) : "")}";
    }

    public override void _GuiInput(InputEvent e) { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) RightClicked?.Invoke(b.GlobalPosition); }
}
