using Godot;

namespace Tabletop;

/// <summary>
/// Entry point for the game. Right now it only proves the C# assembly is wired up and
/// running; game systems get added here (or extracted into their own scenes) as each
/// module is planned.
/// </summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        GD.Print($"Tabletop booting on Godot {Engine.GetVersionInfo()["string"]}");
    }
}
