using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>Lobby edits: written to the game's rows, broadcast, never logged. Shown at lobby and setup.</summary>
public partial class LobbyPanel : VBoxContainer
{
    Label _error = null!; Button? _endSetup; Session _session = null!; Action? _onBoard;

    public override void _ExitTree()
    {
        if (_session is null || _onBoard is null) return;
        _session.BoardChanged -= _onBoard;
    }

    static string EndText(Session s)
    {
        var off = s.Draft is { } d ? d.Terrain.Count(t => !Tabletop.Geometry.Table.ContainsPoint(d.Size, t.Position.Horizontal)) : 0;   // the game server's DropOffSlab rule
        return off == 0 ? "End setup" : $"End setup ({off} off-slab pieces will be dropped)";
    }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session; var game = s.Game!;
        AddChild(new Label { Text = "Lobby" });
        var teams = new HBoxContainer(); AddChild(teams);
        foreach (var t in game.Teams) teams.AddChild(Btn($"Join team {t.Ordinal}", () => Guarded.Send(s, new PickTeam(t.Id))));
        teams.AddChild(Btn("Watch", () => Guarded.Send(s, new PickTeam(null))));
        teams.AddChild(Btn("Create team", () => Guarded.Send(s, new CreateTeam())));
        var armies = new OptionButton(); armies.AddItem("No army"); AddChild(armies);
        AddChild(_error = new Label { Modulate = Colors.Orange });
        Guarded.Run(async () =>
        {
            var list = (await s.Management.ListArmies(null)).Items;
            if (!IsInstanceValid(this)) return;
            foreach (var a in list) armies.AddItem(a.Name);
            var mine = game.Participants.FirstOrDefault(p => p.Id == s.ParticipantId)?.ArmyId;
            if (mine is not null) { var at = list.ToList().FindIndex(a => a.Id == mine); if (at >= 0) armies.Select(at + 1); }
            armies.ItemSelected += i => Guarded.Send(s, new PickArmy(i == 0 ? null : list[(int)i - 1].Id));
        }, e => { if (IsInstanceValid(this)) _error.Text = e; });
        var config = new VBoxContainer(); AddChild(config);
        var name = new LineEdit { Text = game.Name }; config.AddChild(name);
        var shape = new OptionButton(); shape.AddItem("Alternating activation"); shape.AddItem("Player turn"); shape.Select(game.SchedulerShape == SchedulerShape.PlayerTurn ? 1 : 0); config.AddChild(shape);
        var enforcement = new OptionButton(); foreach (var e in Enum.GetNames<Enforcement>()) enforcement.AddItem(e); enforcement.Select((int)game.Enforcement); config.AddChild(enforcement);
        var waits = new CheckBox { Text = "Wait between steps", ButtonPressed = game.WaitsBetweenSteps }; config.AddChild(waits);
        config.AddChild(Btn("Apply configuration", () => Guarded.Send(s, new SetConfiguration(name.Text, game.PropertiesHash,
            shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, (Enforcement)enforcement.Selected, waits.ButtonPressed))));
        if (s.IsCreator && game.Status == GameStatus.Lobby) AddChild(Btn("Start setup", () => Guarded.Send(s, new StartSetup())));
        if (s.IsCreator && game.Status == GameStatus.Setup)
        {
            AddChild(_endSetup = Btn(EndText(s), () => Guarded.Send(s, new EndSetup())));
            _onBoard = () => { if (IsInstanceValid(_endSetup)) _endSetup!.Text = EndText(s); };
            s.BoardChanged += _onBoard;
        }
    }

    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }
}
