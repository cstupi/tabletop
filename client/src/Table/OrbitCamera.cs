using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>Orbit, pan and zoom over the table; Home snaps top-down, End drops to table level for line-of-sight checks.</summary>
public partial class OrbitCamera : Camera3D
{
    Vector3 _target; float _distance = 60, _yaw = 0.6f, _pitch = 0.9f;
    TableSize _size = new(72, 48); Point3 _glideFrom, _glideTo; double _glide = double.MaxValue; ulong _movedAt;
    /// <summary>TableInput's "a unit is in hand": Q/E turn it, and follow waits.</summary>
    public Func<bool> Holding { get; set; } = () => false;
    public float Pitch => _pitch;
    /// <summary>The camera's distance from its target, in inches.</summary>
    public float Distance { get => _distance; set { _distance = value; Apply(); } }

    public void FocusOn(TableSize size) { _size = size; _target = WorldFrame.ToGodot(new Point3(size.Width / 2, size.Length / 2, 0)); _distance = (float)Math.Max(size.Width, size.Length) * 0.9f; Apply(); }
    /// <summary>Look at a point, keeping the distance and angle: Tab, Space and the roster bring a unit to the centre of the screen.
    /// The player's own focus (byPlayer) counts as a camera move for follow but does not stop its own glide.</summary>
    public void Centre(Vector3 target, bool byPlayer = false)
    {
        if (byPlayer) _movedAt = Time.GetTicksMsec();
        _glideFrom = WorldFrame.FromGodot(_target); _glideTo = WorldFrame.FromGodot(target); _glide = 0;
    }
    public void Follow(Vector3 target, bool enabled) { if (CameraFocus.Follows(enabled, (Time.GetTicksMsec() - _movedAt) / 1000.0, Holding())) Centre(target); }
    void Moved() { _movedAt = Time.GetTicksMsec(); _glide = double.MaxValue; }   // the player's own move stops a glide and holds follow off
    void Pan(Point2 to) { _target = new Vector3((float)to.X, _target.Y, (float)-to.Y); Apply(); }   // WorldFrame: Godot z = −wire Y

    /// <summary>WASD and Q/E read held every frame. The heading is the camera's over the table: offset (sin yaw, cos yaw) in Godot x, z looks back along (−sin yaw, cos yaw) in the wire frame.</summary>
    static readonly Key[] HeldGodotKeys = CameraFocus.HeldKeys.Select(k => Enum.Parse<Key>(k.ToString())).ToArray();

    public override void _Process(double delta)
    {
        if (_glide <= CameraFocus.GlideSeconds) { _glide += delta; _target = WorldFrame.ToGodot(CameraFocus.Glide(_glideFrom, _glideTo, _glide)); Apply(); }
        var ctrl = Input.IsKeyPressed(Key.Ctrl);
        if (!HeldGodotKeys.Any(Input.IsKeyPressed)) return;   // most frames: nothing held, nothing allocated
        var held = CameraFocus.HeldKeys.Where((_, i) => Input.IsKeyPressed(HeldGodotKeys[i])).Select(k => Hotkeys.Of(k, false, ctrl)).OfType<Intent>().ToHashSet();
        if (held.Count == 0) return;
        var (forward, right, orbit) = CameraFocus.Steer(held, Holding(), Typing());
        if (forward == 0 && right == 0 && orbit == 0) return;
        Moved(); _yaw += orbit * (float)(CameraFocus.OrbitRadiansPerSecond * delta);
        Pan(CameraFocus.Pan(WorldFrame.FromGodot(_target).Horizontal, new Point2(-Mathf.Sin(_yaw), Mathf.Cos(_yaw)), forward, right, _distance, delta, _size));
    }

    /// <summary>The pointer is over a Hud part, not the table's own container: the root's hovered control is something else.</summary>
    bool OverHud() => GetTree().Root.GuiGetHoveredControl() is { } c && c is not SubViewportContainer;

    /// <summary>A text field has focus in the root viewport (Hud, settings) or a dialog: the root's embedded subwindows, wherever
    /// they sit in the tree, and any Window directly under the root; a Window is its own viewport with its own focus. A SpinBox
    /// counts whether it or its inner LineEdit owns focus.</summary>
    bool Typing()
    {
        static bool In(Viewport v) => v.GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;
        var root = GetTree().Root;
        if (In(root)) return true;
        foreach (var w in root.GetEmbeddedSubwindows()) if (w.Visible && In(w)) return true;
        foreach (var c in root.GetChildren()) if (c is Window { Visible: true } w && In(w)) return true;
        return false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m when Input.IsMouseButtonPressed(MouseButton.Right): Moved(); _yaw -= m.Relative.X * 0.005f; _pitch = Mathf.Clamp(_pitch + m.Relative.Y * 0.005f, 0.05f, 1.55f); Apply(); break;
            case InputEventMouseMotion m when Input.IsMouseButtonPressed(MouseButton.Middle):
                Moved();
                var right = GlobalTransform.Basis.X; var forward = new Vector3(-GlobalTransform.Basis.Z.X, 0, -GlobalTransform.Basis.Z.Z).Normalized();
                var offset = (-right * m.Relative.X + forward * m.Relative.Y) * _distance * 0.001f;
                Pan(CameraFocus.Clamp(WorldFrame.FromGodot(_target + offset).Horizontal, _size)); break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } when OverHud(): break;   // the wheel over a Hud part scrolls it, never the table
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }: Moved(); _distance = Mathf.Max(4, _distance * 0.9f); Apply(); break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }: Moved(); _distance = Mathf.Min(300, _distance * 1.1f); Apply(); break;
            case InputEventKey { Pressed: true, Keycode: Key.Home }: Moved(); _pitch = 1.55f; Apply(); break;
            case InputEventKey { Pressed: true, Keycode: Key.End }: Moved(); _pitch = 0.05f; _target.Y = 1.0f; Apply(); break;
        }
    }

    void Apply()
    {
        var offset = new Vector3(Mathf.Cos(_pitch) * Mathf.Sin(_yaw), Mathf.Sin(_pitch), Mathf.Cos(_pitch) * Mathf.Cos(_yaw)) * _distance;
        Position = _target + offset;
        LookAt(_target, Vector3.Up);
    }
}
