using Godot;
using Tabletop.Client.Panels;

namespace Tabletop.Client.Screens;

public partial class GameScreen
{
    AskPanel? _askPanel;

    partial void ShowPlayPanels(bool playing)
    {
        if (!playing) { _askPanel?.QueueFree(); _askPanel = null; return; }
        if (_askPanel is not null) return;
        _askPanel = new AskPanel(); _side.AddChild(_askPanel); _askPanel.Bind(_session, Table, Input);
    }
}
