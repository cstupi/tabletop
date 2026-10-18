using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>
/// The side panel while deployment waits for players (WaitingView): one line per team it waits on, for everyone; a Join button on a team no one
/// holds, and an army picker on a team of mine with no army. The screen rebuilds it when the game or the board changes.
/// </summary>
public partial class WaitingPanel : VBoxContainer
{
    Label _error = null!;

    public override void _Ready()
    {
        var s = Main.Instance.Session; var game = s.Game!;
        AddChild(new Label { Text = "Waiting for players", ThemeTypeVariation = Variation.Accent });
        AddChild(Wrapping(WaitingView.Title));
        var pickers = new List<(ArmySeat Seat, OptionButton Box)>();
        foreach (var line in WaitingView.Of(game, s.WaitingTeams, s.ParticipantId))
        {
            AddChild(Wrapping(line.Text, Variation.Warning));
            if (line.Join is { } join)
            {
                var b = new Button { Text = join.Text, TooltipText = join.Why }; b.Pressed += () => Guarded.Send(s, join.Edit); AddChild(b);
            }
            if (line.Army is { } seat)
            {
                var box = new OptionButton(); box.AddItem("Pick an army");
                AddChild(new Label { Text = seat.Label, ThemeTypeVariation = Variation.Dim }); AddChild(box);
                pickers.Add((seat, box));
            }
        }
        AddChild(_error = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });
        if (pickers.Count == 0) return;
        Guarded.Run(async () =>
        {
            var list = (await s.Management.ListArmies(null)).Items;
            if (!IsInstanceValid(this)) return;
            foreach (var (seat, box) in pickers)
            {
                foreach (var a in list) box.AddItem(a.Name);
                box.ItemSelected += i => { if (i > 0) Guarded.Send(s, seat.Pick(list[(int)i - 1].Id)); };
            }
        }, text => { if (IsInstanceValid(_error)) { _error.Text = text; _error.Visible = true; } });
    }

    static Label Wrapping(string text, string? variation = null) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = variation ?? "" };
}
