using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>The Layouts tool: Save… in the heading, then the predefined tables and mine as rows with ▸ Load and, for mine, Delete. Loading
/// replaces the whole draft, size included.</summary>
public partial class SetupLayoutsTool : SetupToolBody
{
    VBoxContainer _rows = null!;

    public SetupLayoutsTool() { }
    public SetupLayoutsTool(GameScreen screen) : base(screen) { }

    protected override void Build()
    {
        var s = S;
        var save = new Button { Text = "Save…", TooltipText = "Save this table as a layout" };
        save.Pressed += () => PieceMenu.AskText(this, "Save the table as", name => Guarded.Run(async () =>
        {
            if (TextLimits.NameProblem(name, "A layout name") is { } problem) { Screen.Toast(problem); return; }
            await s.Management.SaveLayout((s.Draft ?? throw new InvalidOperationException("The table has not arrived yet.")) with { Name = name.Trim() });
            Screen.Toast($"Layout {name.Trim()} saved.");
            await Fill();
        }, Screen.Toast));
        Heading.AddChild(save);
        AddChild(_rows = new VBoxContainer());
        _rows.AddThemeConstantOverride("separation", Space.XS);
        Guarded.Run(Fill, Screen.Toast);
    }

    async Task Fill()
    {
        var s = S;
        var (builtin, saved) = (s.Management.BuiltinLayouts(), s.Management.ListLayouts());
        var choices = LayoutChoices.Of(await builtin, await saved);
        if (!IsInstanceValid(_rows)) return;
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }
        foreach (var choice in choices)
        {
            var line = Row(); _rows.AddChild(line);
            line.AddChild(Icon("layout", choice.Builtin is null ? Ui.Accent : Ui.DimText, 16));
            line.AddChild(new Label { Text = choice.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Pass });   // wraps: the panel has the height, a cut name hides the size and piece count
            var menu = new MenuButton { Text = "▸", Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = FocusModeEnum.None };
            menu.GetPopup().AddItem("Load", 0);
            if (choice.SavedId is not null) menu.GetPopup().AddItem("Delete", 1);
            var pick = choice;
            menu.GetPopup().IdPressed += id => Guarded.Run(async () =>
            {
                if (id == 0) await s.SendEditAsync(SetupEdits.Load(pick.Builtin ?? await s.Management.GetLayout(pick.SavedId!.Value)));
                else Confirm($"Delete {pick.Label}?", "Delete", $"Delete {pick.Label}?",
                    () => Guarded.Run(async () => { await s.Management.DeleteLayout(pick.SavedId!.Value); await Fill(); }, Screen.Toast));
            }, Screen.Toast);
            line.AddChild(menu);
        }
    }
}
