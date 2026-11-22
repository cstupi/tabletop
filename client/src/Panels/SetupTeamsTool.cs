using Tabletop.Client.Screens;

namespace Tabletop.Client.Panels;

/// <summary>The Teams tool (spec 2026-11-21-setup-screen-layout-design.md → Teams tool): the compact team columns with their ▸ moves, drag, army
/// pickers and the band's + (Join's add-a-seat), Watching while someone watches, and the creator's + Team; behaviour as
/// spec 2026-11-07-lobby-and-setup-redesign-design.md.</summary>
public partial class SetupTeamsTool : SetupToolBody
{
    public SetupTeamsTool() { }
    public SetupTeamsTool(GameScreen screen) : base(screen) { }

    protected override void Build() => AddChild(new TeamColumnsPanel(compact: true) { SizeFlagsHorizontal = SizeFlags.ExpandFill, Report = Screen.Toast });
}

/// <summary>The Game tool: the game settings, the lobby's controls; only the creator's are enabled.</summary>
public partial class SetupGameTool : SetupToolBody
{
    public SetupGameTool() { }
    public SetupGameTool(GameScreen screen) : base(screen) { }

    protected override void Build() => AddChild(new LobbyPanel { SizeFlagsHorizontal = SizeFlags.ExpandFill });
}
