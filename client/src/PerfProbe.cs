using System.Globalization;
using System.Text;
using Godot;
using Time_ = Godot.Time;
using Tabletop.Client.Core;
using Tabletop.Client.Table;

namespace Tabletop;

/// <summary>
/// A development build's frame recorder (spec 2026-10-25-performance-and-network.md → The Godot client): with
/// <c>TABLETOP_PERF_OUT</c> set, one CSV row per frame of frame time, the viewports' measured render times, Godot's monitors (draw calls,
/// objects, primitives, nodes, video memory), the managed heap, allocation and collections, and the session's batches. <c>TABLETOP_PERF_GAME</c> enters that game at start (<c>TABLETOP_PERF_REPLAY=1</c> as a
/// replay, which <c>TABLETOP_PERF_REPLAY_FROM</c> moves to that position and <c>TABLETOP_PERF_REPLAY_STEP</c> plays forward an action
/// every that many seconds, animated; <c>TABLETOP_PERF_ZOOM</c> sets the table camera's distance as it moves); <c>TABLETOP_PERF_SECONDS</c> quits after that long; <c>TABLETOP_PERF_BENCH</c> runs <see cref="Bench"/>; <see cref="Triggers"/> lets a script mark and photograph. A release build
/// never reads them.
/// </summary>
public partial class PerfProbe : Node
{
    public static string? OutPath => BuildInfo.Current.IsRelease ? null : System.Environment.GetEnvironmentVariable("TABLETOP_PERF_OUT") is { Length: > 0 } p ? p : null;
    public static Guid? GameId => BuildInfo.Current.IsRelease ? null : Guid.TryParse(System.Environment.GetEnvironmentVariable("TABLETOP_PERF_GAME"), out var g) ? g : null;
    public static bool Replay => System.Environment.GetEnvironmentVariable("TABLETOP_PERF_REPLAY") == "1";

    StreamWriter _out = null!;
    readonly StringBuilder _row = new();
    ulong _last, _start;
    long _alloc, _allocAll, _frames; int _gc0, _gc1, _gc2; TimeSpan _pause;
    int _batches, _events; double _quitAfter = double.MaxValue;
    readonly List<(Rid Rid, Viewport Node)> _viewports = new();
    string _screen = "";
    Bench? _bench;
    int? _replayFrom; double _replayStep, _nextStep = 8, _played;

    public override void _Ready()
    {
        On = true;
        AddChild(new Tail());
        ProcessPriority = int.MinValue;   // first in the frame, so each row's interval is frame start to frame start
        _out = new StreamWriter(OutPath!, append: false) { AutoFlush = false };
        _out.WriteLine("frame,wall_ms,dt_ms,gpu_ms,cpu_render_ms,draw_calls,objects,primitives,nodes,orphans,managed_mb,video_mb,texture_mb,buffer_mb,alloc_main_kb,alloc_all_kb,gc0,gc1,gc2,gc_pause_ms,batches,events,screen,size,bench,compiles_canvas,compiles_mesh,compiles_surface,compiles_draw,compiles_spec,process_ms,physics_ms,script_ms,probes");
        if (double.TryParse(System.Environment.GetEnvironmentVariable("TABLETOP_PERF_SECONDS"), CultureInfo.InvariantCulture, out var s)) _quitAfter = s;
        if (double.TryParse(System.Environment.GetEnvironmentVariable("TABLETOP_PERF_BENCH"), CultureInfo.InvariantCulture, out var b)) _bench = new Bench(b);
        if (int.TryParse(System.Environment.GetEnvironmentVariable("TABLETOP_PERF_REPLAY_FROM"), out var from)) _replayFrom = from;
        if (double.TryParse(System.Environment.GetEnvironmentVariable("TABLETOP_PERF_REPLAY_STEP"), CultureInfo.InvariantCulture, out var step)) _replayStep = step;
        var session = Main.Instance.Session;
        session.EventsApplied += e => { _batches++; _events += e.Count; };
        GetTree().NodeAdded += n => { if (n is SubViewport v) Measure(v); };
        Measure(GetViewport());
        _start = _last = Time_.GetTicksUsec();
        _alloc = GC.GetAllocatedBytesForCurrentThread(); _allocAll = GC.GetTotalAllocatedBytes(false);
        (_gc0, _gc1, _gc2, _pause) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration());
        if (PerfScript.Name is not null) AddChild(new PerfScript());
        var flush = new Godot.Timer { WaitTime = 1, Autostart = true }; flush.Timeout += () => _out.Flush(); AddChild(flush);
    }

    /// <summary>Named main-thread timings a measuring patch adds (<see cref="Time"/>), summed per frame into the probes column as
    /// <c>name=ms;</c>. Empty unless the probe runs.</summary>
    static readonly Dictionary<string, double> Probes = new();
    public static bool On { get; private set; }
    static ulong _head, _tail;

    /// <summary>Last in every frame's process: the probe's row reads the main thread's _Process time as from its own end to this.</summary>
    sealed partial class Tail : Node
    {
        public override void _Ready() => ProcessPriority = int.MaxValue;
        public override void _Process(double delta) => _tail = Time_.GetTicksUsec();
    }

    /// <summary><c>using var _ = PerfProbe.Time("rings");</c> around a suspect: its milliseconds count against this frame's row.</summary>
    public static Timing Time(string name) => new(On ? name : null, On ? Time_.GetTicksUsec() : 0);
    /// <summary>An expression timed as <see cref="Time"/> times a block: <c>var x = PerfProbe.Timed("regions", () => MoveRegions.Of(…));</c>.</summary>
    public static T Timed<T>(string name, Func<T> work) { using var _ = Time(name); return work(); }
    public readonly struct Timing(string? name, ulong start) : IDisposable
    {
        public void Dispose() { if (name is not null) Probes[name] = Probes.GetValueOrDefault(name) + (Time_.GetTicksUsec() - start) / 1000.0; }
    }

    string _mark = "";
    /// <summary>A driving script's two files beside the CSV: <c>.mark</c> names the frames while it exists (an action being measured),
    /// <c>.shot</c> asks for a screenshot (<c>.shot.png</c>) and is removed.</summary>
    void Triggers()
    {
        try { _mark = File.Exists(OutPath + ".mark") ? File.ReadAllText(OutPath + ".mark").Trim() : ""; } catch (IOException) { }
        if (!File.Exists(OutPath + ".shot")) return;
        File.Delete(OutPath + ".shot");
        GetViewport().GetTexture().GetImage().SavePng(OutPath + ".shot.png");
    }

    void Measure(Viewport viewport) { RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), true); _viewports.Add((viewport.GetViewportRid(), viewport)); }

    public override void _Process(double delta)
    {
        var now = Time_.GetTicksUsec();
        var dt = (now - _last) / 1000.0; _last = now;
        double gpu = 0, cpu = 0;
        _viewports.RemoveAll(v => !IsInstanceValid(v.Node) || v.Node.IsQueuedForDeletion());
        foreach (var (v, _) in _viewports) { gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(v); cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(v); }
        var alloc = GC.GetAllocatedBytesForCurrentThread(); var allocAll = GC.GetTotalAllocatedBytes(false);
        var (g0, g1, g2, pause) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration());
        static double M(Performance.Monitor m) => Performance.GetMonitor(m);
        if (_frames++ % 30 == 0) _screen = GetParent()?.GetChildren().LastOrDefault(c => c is not PerfProbe && c is not Godot.Timer)?.Name.ToString() ?? "";
        var size = GetWindow().Size;
        var bench = _bench?.Step(this, (now - _start) / 1e6) ?? "";
        DriveReplay(_played += delta);
        if (_frames % 3 == 0) Triggers();
        if (bench.Length == 0) bench = _mark.Length > 0 ? _mark : PerfScript.Phase;
        _row.Clear().Append(CultureInfo.InvariantCulture, $"{_frames},").Append(CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},{dt:0.###},{gpu:0.###},{cpu:0.###},")
            .Append(CultureInfo.InvariantCulture, $"{M(Performance.Monitor.RenderTotalDrawCallsInFrame)},{M(Performance.Monitor.RenderTotalObjectsInFrame)},{M(Performance.Monitor.RenderTotalPrimitivesInFrame)},")
            .Append(CultureInfo.InvariantCulture, $"{M(Performance.Monitor.ObjectNodeCount)},{M(Performance.Monitor.ObjectOrphanNodeCount)},{GC.GetTotalMemory(false) / 1048576.0:0.#},")
            .Append(CultureInfo.InvariantCulture, $"{M(Performance.Monitor.RenderVideoMemUsed) / 1048576:0.#},{M(Performance.Monitor.RenderTextureMemUsed) / 1048576:0.#},{M(Performance.Monitor.RenderBufferMemUsed) / 1048576:0.#},")
            .Append(CultureInfo.InvariantCulture, $"{(alloc - _alloc) / 1024.0:0.#},{(allocAll - _allocAll) / 1024.0:0.#},{g0 - _gc0},{g1 - _gc1},{g2 - _gc2},{(pause - _pause).TotalMilliseconds:0.###},")
            .Append(CultureInfo.InvariantCulture, $"{_batches},{_events},{_screen},{size.X}x{size.Y},{bench},")
            .Append(CultureInfo.InvariantCulture, $"{M(Performance.Monitor.PipelineCompilationsCanvas)},{M(Performance.Monitor.PipelineCompilationsMesh)},{M(Performance.Monitor.PipelineCompilationsSurface)},{M(Performance.Monitor.PipelineCompilationsDraw)},{M(Performance.Monitor.PipelineCompilationsSpecialization)},{M(Performance.Monitor.TimeProcess) * 1000:0.###},{M(Performance.Monitor.TimePhysicsProcess) * 1000:0.###},{(_tail > _head ? (_tail - _head) / 1000.0 : 0):0.###},");
        foreach (var (name, ms) in Probes) _row.Append(CultureInfo.InvariantCulture, $"{name}={ms:0.###};");
        Probes.Clear();
        _out.WriteLine(_row);
        _head = Time_.GetTicksUsec();
        (_alloc, _allocAll, _gc0, _gc1, _gc2, _pause, _batches, _events) = (alloc, allocAll, g0, g1, g2, pause, 0, 0);
        if ((now - _start) / 1e6 > _quitAfter) { _out.Flush(); GetTree().Quit(); }
    }

    public override void _ExitTree() => _out.Dispose();

    void Zoom()
    {
        if (float.TryParse(System.Environment.GetEnvironmentVariable("TABLETOP_PERF_ZOOM"), CultureInfo.InvariantCulture, out var zoom))
            foreach (var view in GetTree().Root.FindChildren("*", "", true, false).OfType<TableView>()) view.Camera.Distance = zoom;
    }

    /// <summary>On the game's clock (a movie's fixed frame rate included), from 8 s after start (the table loaded): the replay moved to <c>TABLETOP_PERF_REPLAY_FROM</c>, then an action forward,
    /// animated as the replay bar's A› plays it, every <c>TABLETOP_PERF_REPLAY_STEP</c> seconds.</summary>
    void DriveReplay(double seconds)
    {
        var session = Main.Instance.Session;
        if (session.Replay is not { } replay || seconds < _nextStep) return;
        if (_replayFrom is { } from)
        {
            session.SeekReplay(Math.Min(from, replay.Count)); _replayFrom = null; _nextStep = seconds + 2;
            Zoom();
            return;
        }
        if (_replayStep <= 0 || replay.AtEnd) return;
        Zoom();
        session.SeekReplay(replay.Next(ReplayUnit.Action), animate: true);
        _nextStep = seconds + _replayStep;
    }

    /// <summary>
    /// From <c>TABLETOP_PERF_BENCH</c> seconds after start, the table as it stands under one change at a time, each held for
    /// <see cref="Hold"/> seconds and named in the bench column (its first second left out to settle), then the settings as they
    /// were: what each graphics feature and each part of the scene costs.
    /// </summary>
    sealed class Bench(double start)
    {
        const double Hold = 4;
        static readonly (string Name, Action<Scene> Apply)[] Steps =
        [
            ("base", _ => { }),
            ("no-ssao", s => s.Env.SsaoEnabled = false),
            ("no-soft-shadow", s => s.Key.LightAngularDistance = 0),
            ("no-shadows", s => s.Key.ShadowEnabled = false),
            ("splits-2", s => s.Key.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits),
            ("splits-1", s => s.Key.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal),
            ("msaa-0", s => s.Table.Msaa3D = Viewport.Msaa.Disabled),
            ("msaa-2", s => s.Table.Msaa3D = Viewport.Msaa.Msaa2X),
            ("msaa-4", s => s.Table.Msaa3D = Viewport.Msaa.Msaa4X),
            ("scale-75", s => { s.Table.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr; s.Table.Scaling3DScale = 0.75f; }),
            ("scale-50", s => { s.Table.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr; s.Table.Scaling3DScale = 0.5f; }),
            ("no-fill", s => s.Fill.Visible = false),
            ("lod-2", s => s.Table.MeshLodThreshold = 2),
            ("lod-4", s => s.Table.MeshLodThreshold = 4),
            ("lod-8", s => s.Table.MeshLodThreshold = 8),
            ("no-miniatures", s => s.Meshes(false)),
            ("miniatures-no-shadow", s => s.MeshShadows(false)),
            ("no-terrain", s => s.Terrain(false)),
            ("no-hud", s => s.Hud(false)),
            ("all-off", s => { s.Env.SsaoEnabled = false; s.Key.ShadowEnabled = false; s.Table.Msaa3D = Viewport.Msaa.Disabled; s.Hud(false); }),
        ];
        int _step = -1; Scene? _scene; bool _shot;

        public string Step(Node probe, double t)
        {
            if (t < start || _step >= Steps.Length) return "";
            var i = (int)((t - start) / Hold);
            if (i >= Steps.Length) { _scene?.Restore(); _step = Steps.Length; return ""; }
            if (i != _step)
            {
                if ((_scene ??= Scene.Find(probe.GetTree().Root)) is not { } scene) return "";
                scene.Restore(); _step = i; _shot = false; Steps[i].Apply(scene);
            }
            if (!_shot && t - start - i * Hold > 2 && OutPath is { } path)
            {
                _shot = true;
                probe.GetViewport().GetTexture().GetImage().SavePng($"{path}.{Steps[i].Name}.png");   // what the step looks like
            }
            return t - start - i * Hold < 1 ? "" : Steps[i].Name;
        }
    }

    /// <summary>The parts of the table a bench step changes, found by type, and how to put them back.</summary>
    sealed class Scene
    {
        public required Godot.Environment Env; public required DirectionalLight3D Key, Fill; public required SubViewport Table;
        required public Node Root;
        bool _ssao, _shadow; float _angular, _scale, _lod; DirectionalLight3D.ShadowMode _mode; Viewport.Msaa _msaa; Viewport.Scaling3DModeEnum _scaling;
        readonly List<CanvasItem> _hidden = new();

        public static Scene? Find(Node root)
        {
            var all = All(root).ToList();
            if (all.OfType<WorldEnvironment>().FirstOrDefault() is not { } w || all.OfType<SubViewport>().FirstOrDefault() is not { } v) return null;
            var lights = all.OfType<DirectionalLight3D>().ToList();
            var s = new Scene { Env = w.Environment, Key = lights.First(l => l.Name == "Key"), Fill = lights.First(l => l.Name != "Key"), Table = v, Root = root };
            (s._ssao, s._shadow, s._angular, s._mode) = (s.Env.SsaoEnabled, s.Key.ShadowEnabled, s.Key.LightAngularDistance, s.Key.DirectionalShadowMode);
            (s._msaa, s._scaling, s._scale, s._lod) = (s.Table.Msaa3D, s.Table.Scaling3DMode, s.Table.Scaling3DScale, s.Table.MeshLodThreshold);
            return s;
        }

        static IEnumerable<Node> All(Node n) { yield return n; foreach (var c in n.GetChildren()) foreach (var d in All(c)) yield return d; }

        /// <summary>Each model's mesh (the scene under its piece that is neither the proxy nor the base ring).</summary>
        public void Meshes(bool on)
        {
            foreach (var p in All(Table).OfType<PieceNode>())
                foreach (var c in p.GetChildren()) if (c is Node3D m && c is not MeshInstance3D && c.Name != "BaseRing") m.Visible = on;
        }
        public void MeshShadows(bool on)
        {
            foreach (var p in All(Table).OfType<PieceNode>())
                foreach (var g in All(p).OfType<GeometryInstance3D>()) if (g.Name != "BaseRing" && g.GetParent()?.Name != "BaseRing" && g.GetParent() != p)
                    g.CastShadow = on ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
        }
        public void Terrain(bool on) { foreach (var t in All(Table).OfType<TerrainNode>()) t.Visible = on; }

        /// <summary>Every visible Control outside the table's container and its ancestors.</summary>
        public void Hud(bool on)
        {
            if (on) { foreach (var c in _hidden) if (GodotObject.IsInstanceValid(c)) c.Visible = true; _hidden.Clear(); return; }
            var keep = new HashSet<Node>();
            for (Node? n = Table; n is not null; n = n.GetParent()) keep.Add(n);
            foreach (var n in All(Root))
                if (n is Control c && c.Visible && !keep.Contains(c) && c.GetParent() is { } parent && keep.Contains(parent) && c is not SubViewportContainer) { c.Visible = false; _hidden.Add(c); }
        }

        public void Restore()
        {
            (Env.SsaoEnabled, Key.ShadowEnabled, Key.LightAngularDistance, Key.DirectionalShadowMode) = (_ssao, _shadow, _angular, _mode);
            (Table.Msaa3D, Table.Scaling3DMode, Table.Scaling3DScale, Table.MeshLodThreshold) = (_msaa, _scaling, _scale, _lod);
            Fill.Visible = true; Meshes(true); MeshShadows(true); Terrain(true); Hud(true);
        }
    }
}
