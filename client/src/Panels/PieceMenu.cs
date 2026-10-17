using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>The right-click menu: every property editable, each item one ordinary command (in setup, one setup edit). No admin mode, no confirmation dialog — the log is the accountability.</summary>
public partial class PieceMenu : PopupMenu
{
    readonly Session _s; readonly Action<string> _report; IReadOnlyList<MenuItem> _items = [];
    public event Action<Action<Guid>>? PickPieceRequested;    // the table resolves the next click to a piece id
    public event Action<Action<Point3>>? PickPointRequested;  // the table resolves the next click to a wire-frame point
    /// <summary>Fired for every chosen item before its command is built; the owner handles the items that are not commands (Leave).</summary>
    public event Action<MenuItem>? ItemChosen;

    public PieceMenu(Session s, Action<string> report) { _s = s; _report = report; IdPressed += OnPressed; }

    public void Open(IReadOnlyList<MenuItem> items, Vector2 screen)
    {
        if (items.Count == 0) return;
        Clear(); _items = items;
        for (var i = 0; i < items.Count; i++) AddItem(items[i].Label, i);
        Position = (Vector2I)screen; Popup();
    }

    void OnPressed(long id)
    {
        var item = _items[(int)id];
        ItemChosen?.Invoke(item);
        switch (item.Input)
        {
            case MenuInput.None: Send(item, null); break;
            case MenuInput.Integer: AskInteger(this, item.Label, v => Send(item, v.ToString())); break;
            case MenuInput.Text: AskText(this, item.Label, t => Send(item, t)); break;
            case MenuInput.PieceId: PickPieceRequested?.Invoke(picked => Send(item, picked.ToString())); break;
            case MenuInput.TablePoint: PickPointRequested?.Invoke(at => Send(item, FormattableString.Invariant($"{at.X},{at.Y},{at.Z}"))); break;
            case MenuInput.Choice: AskChoice(this, item.Label, item.Choices, c => Send(item, c)); break;
        }
    }

    /// <summary>One of a list, which may be long (a property set's grantable effects): a filter field with the focus over the list; a double-click,
    /// or Enter on a selected row, confirms; Esc cancels.</summary>
    public static void AskChoice(Node host, string title, IReadOnlyList<string> choices, Action<string> then)
    {
        var d = new ConfirmationDialog { Title = title };
        var box = new VBoxContainer(); d.AddChild(box);
        var filter = new LineEdit { PlaceholderText = "filter" }; box.AddChild(filter);
        var list = new ItemList { CustomMinimumSize = new Vector2(320, 280) }; box.AddChild(list);
        void Show(string text) { list.Clear(); foreach (var c in choices) if (text.Length == 0 || c.Contains(text, StringComparison.OrdinalIgnoreCase)) list.AddItem(c); if (list.ItemCount > 0) list.Select(0); }
        filter.TextChanged += Show; Show("");
        string? Picked() => list.GetSelectedItems() is { Length: > 0 } sel ? list.GetItemText(sel[0]) : null;
        d.RegisterTextEnter(filter);
        d.Confirmed += () => { if (Picked() is { } c) then(c); };
        list.ItemActivated += _ => { if (Picked() is { } c) { d.Hide(); then(c); } };
        d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        host.AddChild(d); d.PopupCentered(); filter.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>A setup item sends its edit; any other its command.</summary>
    void Send(MenuItem item, string? input)
    {
        if (item.Edit is { } edit) { if (edit(input) is { } e) Guarded.Send(_s, e); }
        else if (item.Build(input) is { } command) Guarded.Command(_s, command, _report);
    }

    /// <summary>The field has the focus, Enter confirms and Esc cancels.</summary>
    public static void AskInteger(Node host, string title, Action<int> then)
    {
        var d = new ConfirmationDialog { Title = title }; var spin = new SpinBox { MinValue = -99, MaxValue = 99 }; d.AddChild(spin);
        d.RegisterTextEnter(spin.GetLineEdit());
        d.Confirmed += () => { spin.Apply(); then((int)spin.Value); }; d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        host.AddChild(d); d.PopupCentered(); spin.GetLineEdit().CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>A label or condition name, held to the servers' 64 characters (TextLimits.Label); the field has the focus, Enter
    /// confirms, Esc cancels.</summary>
    public static void AskText(Node host, string title, Action<string> then)
    {
        var d = new ConfirmationDialog { Title = title }; var line = new LineEdit { MaxLength = TextLimits.Label, PlaceholderText = $"at most {TextLimits.Label} characters" }; d.AddChild(line);
        d.RegisterTextEnter(line);
        d.Confirmed += () => then(line.Text); d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        host.AddChild(d); d.PopupCentered(new Vector2I(320, 0)); line.CallDeferred(Control.MethodName.GrabFocus);
    }
}
