using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>Orbit, pan and zoom over the table; Home snaps top-down, End drops to table level for line-of-sight checks.</summary>
public partial class OrbitCamera : Camera3D
{
    Vector3 _target; float _distance = 60, _yaw = 0.6f, _pitch = 0.9f;

    public void FocusOn(TableSize size) { _target = WorldFrame.ToGodot(new Point3(size.Width / 2, size.Length / 2, 0)); _distance = (float)Math.Max(size.Width, size.Length) * 0.9f; Apply(); }
    /// <summary>Look at a point, keeping the distance and angle: Tab and Space bring a unit to the centre of the screen.</summary>
    public void Centre(Vector3 target) { _target = target; Apply(); }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m when Input.IsMouseButtonPressed(MouseButton.Right): _yaw -= m.Relative.X * 0.005f; _pitch = Mathf.Clamp(_pitch + m.Relative.Y * 0.005f, 0.05f, 1.55f); Apply(); break;
            case InputEventMouseMotion m when Input.IsMouseButtonPressed(MouseButton.Middle):
                var right = GlobalTransform.Basis.X; var forward = new Vector3(-GlobalTransform.Basis.Z.X, 0, -GlobalTransform.Basis.Z.Z).Normalized();
                _target += (-right * m.Relative.X + forward * m.Relative.Y) * _distance * 0.001f; Apply(); break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }: _distance = Mathf.Max(4, _distance * 0.9f); Apply(); break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }: _distance = Mathf.Min(300, _distance * 1.1f); Apply(); break;
            case InputEventKey { Pressed: true, Keycode: Key.Home }: _pitch = 1.55f; Apply(); break;
            case InputEventKey { Pressed: true, Keycode: Key.End }: _pitch = 0.05f; _target.Y = 1.0f; Apply(); break;
        }
    }

    void Apply()
    {
        var offset = new Vector3(Mathf.Cos(_pitch) * Mathf.Sin(_yaw), Mathf.Sin(_pitch), Mathf.Cos(_pitch) * Mathf.Cos(_yaw)) * _distance;
        Position = _target + offset;
        LookAt(_target, Vector3.Up);
    }
}
