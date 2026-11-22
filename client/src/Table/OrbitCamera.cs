using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>Orbit, pan and zoom over the table; the top-down key (Home) snaps top-down, the table-level key (End) drops to table level for line-of-sight checks.</summary>
public partial class OrbitCamera : Camera3D
{
    Vector3 _target; float _distance = 60, _yaw = 0.6f, _pitch = 0.9f;
    TableSize _size = new(72, 48); Point3 _glideFrom, _glideTo; double _glide = double.MaxValue; ulong _movedAt;
    /// <summary>TableInput's "a unit is in hand": Q/E turn it, and follow waits.</summary>
    public Func<bool> Holding { get; set; } = () => false;
    /// <summary>Options → Camera and Accessibility: speeds, inversions, edge scrolling, reduce motion.</summary>
    public Func<ClientSettings> Settings { get; set; } = () => ClientSettings.Default;
    public float Pitch => _pitch;
    /// <summary>The camera's distance from its target, in inches.</summary>
    public float Distance { get => _distance; set { _distance = value; Apply(); } }

    public void FocusOn(TableSize size) { _size = size; _target = WorldFrame.ToGodot(new Point3(size.Width / 2, size.Length / 2, 0)); _distance = (float)Math.Max(size.Width, size.Length) * 0.9f; Apply(); }
    /// <summary>Look at a point, keeping the distance and angle: Tab, Space and the roster bring a unit to the centre of the screen.
    /// The player's own focus (byPlayer) counts as a camera move for follow but does not stop its own glide.</summary>
    public void Centre(Vector3 target, bool byPlayer = false)
    {
        if (byPlayer) _movedAt = Time.GetTicksMsec();
        if (Settings().ReduceMotion) { _target = target; _glide = double.MaxValue; Apply(); return; }   // reduce motion: no glide
        _glideFrom = WorldFrame.FromGodot(_target); _glideTo = WorldFrame.FromGodot(target); _glide = 0;
    }
    public void Follow(Vector3 target, bool enabled) { if (CameraFocus.Follows(enabled, (Time.GetTicksMsec() - _movedAt) / 1000.0, Holding())) Centre(target); }
    void Moved() { _movedAt = Time.GetTicksMsec(); _glide = double.MaxValue; }   // the player's own move stops a glide and holds follow off
    void Pan(Point2 to) { _target = new Vector3((float)to.X, _target.Y, (float)-to.Y); Apply(); }   // WorldFrame: Godot z = −wire Y

    /// <summary>The camera keys (CameraFocus.HeldIntents, as bound) read held every frame, and the window's edge with Edge scrolling. The heading is
    /// the camera's over the table: offset (sin yaw, cos yaw) in Godot x, z looks back along (−sin yaw, cos yaw) in the wire frame.</summary>
    public override void _Process(double delta)
    {
        if (_glide <= CameraFocus.GlideSeconds) { _glide += delta; _target = WorldFrame.ToGodot(CameraFocus.Glide(_glideFrom, _glideTo, _glide)); Apply(); }
        var s = Settings();
        var ctrl = Input.IsKeyPressed(Key.Ctrl);
        int forward = 0, right = 0, orbit = 0;
        HashSet<Intent>? held = null;
        foreach (var intent in CameraFocus.HeldIntents) if (HotkeyInput.Held(intent, ctrl)) (held ??= []).Add(intent);
        if (held is not null) (forward, right, orbit) = CameraFocus.Steer(held, Holding(), Typing());
        if (s.EdgeScroll && forward == 0 && right == 0 && GetWindow().HasFocus() && !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            var root = GetTree().Root; var size = root.GetVisibleRect().Size; var at = root.GetMousePosition();
            (forward, right) = CameraFocus.EdgePan(new Point2(at.X, at.Y), new Point2(size.X, size.Y));
        }
        if (forward == 0 && right == 0 && orbit == 0) return;
        Moved(); _yaw += orbit * (s.InvertOrbit ? -1 : 1) * (float)(CameraFocus.OrbitRadiansPerSecond * s.OrbitSpeed / 100.0 * delta);
        Pan(CameraFocus.Pan(WorldFrame.FromGodot(_target).Horizontal, new Point2(-Mathf.Sin(_yaw), Mathf.Cos(_yaw)), forward, right, _distance, delta * s.PanSpeed / 100.0, _size));
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
            case InputEventMouseMotion m when Input.IsMouseButtonPressed(MouseButton.Right):
                Moved(); var turn = 0.005f * Settings().OrbitSpeed / 100f;
                _yaw -= m.Relative.X * turn * (Settings().InvertOrbit ? -1 : 1); _pitch = Mathf.Clamp(_pitch + m.Relative.Y * turn, 0.05f, 1.55f); Apply(); break;
            case InputEventMouseMotion m when Input.IsMouseButtonPressed(MouseButton.Middle):
                Moved();
                var right = GlobalTransform.Basis.X; var forward = new Vector3(-GlobalTransform.Basis.Z.X, 0, -GlobalTransform.Basis.Z.Z).Normalized();
                var offset = (-right * m.Relative.X + forward * m.Relative.Y) * _distance * 0.001f;
                Pan(CameraFocus.Clamp(WorldFrame.FromGodot(_target + offset).Horizontal, _size)); break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } when OverHud(): break;   // the wheel over a Hud part scrolls it, never the table
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } w:
                Moved(); _distance = Mathf.Clamp(_distance * (float)CameraFocus.ZoomStep(w.ButtonIndex == MouseButton.WheelUp, Settings().ZoomSpeed, Settings().InvertZoom), 4, 300); Apply(); break;
            case InputEventKey { Pressed: true, Echo: false } k when HotkeyInput.Of(k) == Intent.CameraTop && !Typing(): Moved(); _pitch = 1.55f; Apply(); break;
            case InputEventKey { Pressed: true, Echo: false } k when HotkeyInput.Of(k) == Intent.CameraLevel && !Typing(): Moved(); _pitch = 0.05f; _target.Y = 1.0f; Apply(); break;
        }
    }

    void Apply()
    {
        var offset = new Vector3(Mathf.Cos(_pitch) * Mathf.Sin(_yaw), Mathf.Sin(_pitch), Mathf.Cos(_pitch) * Mathf.Cos(_yaw)) * _distance;
        Position = _target + offset;
        LookAt(_target, Vector3.Up);
    }
}
