using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

public partial class TeamPanel : PanelContainer
{
    public Team Team { get; }
    readonly Label _text = new();
    public event Action<Vector2>? RightClicked;
    public TeamPanel(Team team) { Team = team; AddChild(_text); SelfModulate = Palette.Team(team.Ordinal); }

    public void Refresh(Session s)
    {
        var members = s.Game?.Participants.Where(p => p.TeamId == Team.Id).Select(p => p.Name + (p.ArmyId is null ? "" : " ⚔")) ?? [];
        var score = s.Projection?.Scores.GetValueOrDefault(Team.Id);
        var held = s.Projection?.Control.Count(kv => kv.Value == Team.Id) ?? 0;
        var active = s.Projection?.Scheduler.ActiveTeamId == Team.Id ? "  ◀ active" : "";
        _text.Text = $"Team {Team.Ordinal}{(score is null ? "" : $"  score {score}")}{(held == 0 ? "" : $"  held {held}")}{active}\n{string.Join(", ", members)}";
    }

    public override void _GuiInput(InputEvent e) { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) RightClicked?.Invoke(b.GlobalPosition); }
}
