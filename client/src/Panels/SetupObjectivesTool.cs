using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>The Objectives tool: + Objective in the heading, then a row per marker with its position and ▸ Remove.</summary>
public partial class SetupObjectivesTool : SetupToolBody
{
    VBoxContainer _rows = null!;
    IReadOnlyList<ObjectiveRow>? _shown;

    public SetupObjectivesTool() { }
    public SetupObjectivesTool(GameScreen screen) : base(screen) { }

    protected override void Build()
    {
        var add = Gated(new Button { Text = "Objective", Icon = Ui.Icon("add"), TooltipText = "Add an objective beside the others" }, SetupLimits.CannotAddMarker);
        add.AddThemeConstantOverride("icon_max_width", 16);
        add.Pressed += () => { if (S.Draft is { } d) Guarded.Send(S, SetupEdits.Objective(d)); };
        Heading.AddChild(add);
        AddChild(_rows = new VBoxContainer());
        _rows.AddThemeConstantOverride("separation", Space.XS);
    }

    protected override void Refresh()
    {
        var rows = ObjectiveRows.Of(S.Draft);
        if (_shown is { } was && was.SequenceEqual(rows)) return;
        _shown = rows;
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }
        if (rows.Count == 0) { _rows.AddChild(new Label { Text = "No objectives", ThemeTypeVariation = Variation.Dim }); return; }
        foreach (var row in rows)
        {
            var line = Row(); _rows.AddChild(line);
            line.AddChild(Icon("objective", Ui.Accent, 16));
            line.AddChild(new Label { Text = row.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
            line.AddChild(new Label { Text = row.Position, ThemeTypeVariation = Variation.Dim });
            var menu = new MenuButton { Text = "▸", Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = FocusModeEnum.None, TooltipText = row.Label };
            menu.GetPopup().AddItem("Remove", 0);
            var id = row.Id;
            menu.GetPopup().IdPressed += _ => Guarded.Send(S, SetupEdits.RemoveMarker(id));
            line.AddChild(menu);
        }
    }
}
