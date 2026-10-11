using Godot;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Hud;

/// <summary>Top right, in every status: the newest three messages stacked, each gone after 4 s.</summary>
public partial class Toasts : VBoxContainer
{
    const int Kept = 3;
    const double Seconds = 4;

    public Toasts() => MouseFilter = MouseFilterEnum.Ignore;

    public void Show(string text)
    {
        if (!IsInsideTree()) return;
        var toast = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        toast.AddChild(new Label { Text = text, ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        AddChild(toast);
        var kept = GetChildren().OfType<PanelContainer>().Where(t => !t.IsQueuedForDeletion()).ToList();
        foreach (var old in kept.Take(Math.Max(0, kept.Count - Kept))) { RemoveChild(old); old.QueueFree(); }
        GetTree().CreateTimer(Seconds).Timeout += () =>
        {
            if (!IsInstanceValid(toast) || toast.IsQueuedForDeletion()) return;
            if (toast.GetParent() is { } parent) parent.RemoveChild(toast);
            toast.QueueFree();
        };
    }
}
