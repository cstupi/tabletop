using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>
/// The side panel while deployment waits for players (WaitingView): one line per team it waits on, for everyone; a Join button on a team no one
/// holds, and a dropdown of my armies on a team of mine with no army, as in the lobby (the army is final once it joins, so picking sends it).
/// The screen calls Refresh on every game or board change; the rows are rebuilt only when the lines change (WaitingView.Changed).
/// </summary>
public partial class WaitingPanel : VBoxContainer
{
    IReadOnlyList<WaitingLine>? _shown;

    public override void _Ready() => Refresh();

    public void Refresh()
    {
        var s = Main.Instance.Session;
        if (s.Game is not { } game) return;
        var lines = WaitingView.Of(game, s.WaitingTeams, s.ParticipantId);
        if (!WaitingView.Changed(_shown, lines)) return;
        _shown = lines;
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        Build(s, lines);
    }

    void Build(Session s, IReadOnlyList<WaitingLine> lines)
    {
        AddChild(new Label { Text = "Waiting for players", ThemeTypeVariation = Variation.Accent });
        AddChild(Wrapping(WaitingView.Title));
        var pickers = new List<(ArmySeat Seat, OptionButton Box)>();
        foreach (var line in lines)
        {
            AddChild(Wrapping(line.Text, Variation.Warning));
            if (line.Join is { } join)
            {
                var b = new Button { Text = join.Text, TooltipText = join.Why }; b.Pressed += () => Guarded.Send(s, join.Edit); AddChild(b);
            }
            if (line.Army is { } seat)
            {
                var box = new OptionButton { Disabled = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; box.AddItem("Pick an army");
                AddChild(new Label { Text = seat.Label, ThemeTypeVariation = Variation.Dim }); AddChild(box);
                pickers.Add((seat, box));
            }
        }
        var error = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        AddChild(error);
        if (pickers.Count == 0) return;
        Guarded.Run(async () =>
        {
            var list = (await s.Management.ListArmies(null)).Items;
            foreach (var (seat, box) in pickers)
            {
                if (!IsInstanceValid(box) || !box.IsInsideTree()) continue;   // the rows were rebuilt while the list loaded
                if (list.Count == 0) { box.SetItemText(0, "No armies: import one in the web app"); continue; }
                foreach (var a in list) box.AddItem(a.Name);
                box.Disabled = false;
                box.ItemSelected += i => { if (i > 0) Guarded.Send(s, seat.Pick(list[(int)i - 1].Id)); };
            }
        }, text => { if (IsInstanceValid(error)) { error.Text = text; error.Visible = true; } });
    }

    static Label Wrapping(string text, string? variation = null) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = variation ?? "" };
}
