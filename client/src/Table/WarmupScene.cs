using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>
/// The shader warm-up's table (spec 2026-10-25-loading-design.md → Shader warm-up): in a viewport of its own, set up as the game's
/// table viewport is, with the table's lights and environment, every kind of thing a table draws, made by the same code a game
/// uses: the board and the table under it, every built-in terrain mesh on hand (cached or packed; it fetches none) dressed in its
/// grains, a stand-in dressed in each grain for a release's first start with none on hand, models in each state (hitbox, ring,
/// highlight, warning, ghost) with a miniature of each kind of glTF material (<see cref="WarmupGlb"/>), markers, zones, a move area,
/// rulers, sight and hover lines, the battle effects' flashes, bolts, sparks and beams, the Shaken ring and the floating labels. Each Quality is applied in turn and drawn a
/// few frames, so its shaders and pipelines compile here. The built-in meshes' templates and the prints stay loaded for the game.
/// </summary>
public partial class WarmupScene : Node
{
    const int FramesPerQuality = 4, MaxFramesPerQuality = 120;
    SubViewport _viewport = null!; DirectionalLight3D _key = null!; Godot.Environment _environment = null!;
    Node3D _root = null!;

    /// <summary>The bar's steps: each built-in terrain mesh on hand as it loads, the rest of the table, then each Quality.</summary>
    public async Task Run(GraphicsQuality current, Action<LoadingLine> progress)
    {
        var names = BuiltinContent.Terrain.GroupBy(t => t.Value.Mesh).Select(g => g.First().Key)
            .Where(n => BuiltinContent.File(BuiltinContent.MeshPathOf(n)!) is not null).ToList();
        var steps = names.Count + 1; var done = 0;
        void Step() => progress(GraphicsWarmup.Line(++done, steps));
        progress(GraphicsWarmup.Line(0, steps));
        _viewport = new SubViewport { Size = new Vector2I(640, 360), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, OwnWorld3D = true };
        AddChild(_viewport);
        _viewport.AddChild(_root = new Node3D());
        (_key, _environment) = TableLighting.AddTo(_root);
        var camera = new Camera3D { Current = true, Fov = 50 };
        _root.AddChild(camera);
        camera.LookAtFromPosition(new Vector3(12, 16, 14), new Vector3(12, 0, -8));
        await Build(names, Step);
        Step();
        var qualities = GraphicsWarmup.Qualities(current);
        for (var i = 0; i < qualities.Count; i++)
        {
            var p = GraphicsSettings.Profile(qualities[i], null);
            GraphicsApplier.ApplyViewport(_viewport, p); GraphicsApplier.ApplyKeyLight(_key, p); GraphicsApplier.ApplyEnvironment(_environment, p); GraphicsApplier.ApplyAtlas(p);
            await Settle();
            Step();
        }
    }

    /// <summary>Draws until the renderer's pipeline compilations (which Godot also runs on worker threads) have stood still for a few
    /// frames, at least <see cref="FramesPerQuality"/> frames and at most <see cref="MaxFramesPerQuality"/>. The Compatibility renderer
    /// counts none and draws the minimum.</summary>
    async Task Settle()
    {
        double Compiled() => Performance.GetMonitor(Performance.Monitor.PipelineCompilationsCanvas) + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsMesh)
            + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSurface) + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw)
            + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSpecialization);
        var last = Compiled(); var still = 0;
        for (var f = 0; f < MaxFramesPerQuality && (f < FramesPerQuality || still < FramesPerQuality); f++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var now = Compiled();
            still = now == last ? still + 1 : 0; last = now;
        }
    }

    static Vector3 Cell(int i) => new(1.5f + i % 12 * 2f, 0, -1.5f - i / 12 * 2.5f);

    async Task Build(IReadOnlyList<string> names, Action step)
    {
        var slab = new SlabNode(); _root.AddChild(slab);
        // The board shader needs a print: a built-in one on hand, else the table's wood, which draws through the same shader.
        var print = BuiltinContent.Textures.Select(t => BuiltinContent.File(BuiltinCatalog.TexturePath(t))).FirstOrDefault(f => f is not null);
        var board = slab.SetPrint(new TableSize(24, 16), print ?? SlabNode.TableWood);

        // Every built-in terrain mesh, one terrain of each, dressed as a game dresses it; then a terrain piece's hitboxes.
        var meshes = new MeshLoader(Main.Instance.Cache, Main.Instance.Session.Management, Guid.Empty);
        var terrain = (await Task.WhenAll(names.Select(async n => { var scene = await meshes.LoadBuiltinAsync(n, fetch: false); step(); return scene; })))
            .Concat(await Dressed()).ToList();
        var i = 0;
        foreach (var scene in terrain)
        {
            if (scene is null) continue;
            var holder = new Node3D { Position = Cell(i++), Scale = Vector3.One * 1.6f }; holder.AddChild(scene); _root.AddChild(holder);
        }
        if (BuiltinContent.Terrain.Keys.FirstOrDefault() is { } first)
        {
            var piece = new TerrainNode { PlacementId = Guid.NewGuid() }; piece.Build(BuiltinContent.Place(first, new Point3(2, 12, 0), new Yaw(0)));
            piece.SetLook(true, true); _root.AddChild(piece);
            var faint = new TerrainNode { PlacementId = Guid.NewGuid() }; faint.Build(BuiltinContent.Place(first, new Point3(5, 12, 0), new Yaw(0)));
            faint.SetLook(false, false); _root.AddChild(faint);
        }

        // Models: each kind of glTF material, as a model with its ring, highlighted, warned and as a ghost; bare hitboxes of each base.
        var png = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8); png.Fill(new Color(0.7f, 0.6f, 0.5f));
        var pngBytes = png.SavePngToBuffer();
        var j = 0;
        foreach (var variant in Enum.GetValues<WarmupGlb.Variant>())
        {
            for (var look = 0; look < 4; look++)
            {
                if (Miniature(variant, pngBytes) is not { } mini) continue;
                var n = new PieceNode { ModelId = Guid.NewGuid() };
                _root.AddChild(n);
                n.SetBody(Base.Circle(1.26), 1.5); n.Position = new Vector3(1 + j % 12 * 2f, 0, -9 - j / 12 * 2f); j++;
                if (look == 3) n.SetGhost(Palette.Ghost);
                n.SetMesh(mini);
                n.SetShowHitboxes(look == 0);
                if (look == 1) n.SetHighlight(Palette.Selected);
                if (look == 2) n.SetWarning(Palette.WoundDot);
            }
        }
        foreach (var b in new[] { Base.Circle(1), Base.Rect(2, 1), Base.Oval(2, 1) })
        {
            var n = new PieceNode { ModelId = Guid.NewGuid() }; _root.AddChild(n);
            n.SetBody(b, 1); n.Position = new Vector3(1 + j % 12 * 2f, 0, -9 - j / 12 * 2f); j++;
            n.SetTint(Palette.Tinted(1));
        }

        // Markers, a zone, a move area, rulers, lines and labels: the overlays a game draws over the table.
        var z = new Vector3(0, 0, -14);
        foreach (var label in new[] { "1", (string?)null })
        {
            var m = new MarkerNode { MarkerId = Guid.NewGuid() }; m.Build(new Marker(Guid.NewGuid(), MarkerKind.Objective, new Point3(18, 14, 0), new Yaw(0), 1.5, label, VisualRef.OfBuiltin("objective-ring")), label is null ? Palette.Team(1) : null, 3);
            m.Position = z + new Vector3(18, 0, 0); _root.AddChild(m);
        }
        // The warm-up has no game, so the zone takes the 3.5.1 legs.
        var zone = new ZoneNode(); zone.Build(new DeploymentZone(Guid.NewGuid(), "frontline/1", 1), new TableSize(24, 16), ZoneLegs.Default, Palette.Team(1) with { A = 0.35f }); _root.AddChild(zone);
        var region = new MoveRegionNode(); _root.AddChild(region); region.Show([new Point2(14, 4), new Point2(20, 4), new Point2(20, 8), new Point2(14, 8)], Palette.Hovered);
        var ruler = new RulerNode(); _root.AddChild(ruler); ruler.Show(new Point3(14, 10, 0.1), new Point3(20, 10, 0.1), new RulerReadout(6, 2, false), Palette.Selected);
        var lines = new MeshInstance3D { Mesh = LineMesh.Of([(new Vector3(14, 0.5f, -6), new Vector3(20, 0.5f, -6), Palette.Hovered)]), MaterialOverride = LineMesh.Material() }; _root.AddChild(lines);
        foreach (var fx in BattleFx.WarmupNodes()) { fx.Position += new Vector3(22, 1, -4); _root.AddChild(fx); }
        var shaken = ModelMarkNodes.Ring(Base.Circle(1)); shaken.Position = new Vector3(22, 0.05f, -2); _root.AddChild(shaken);
        var dots = ModelMarkNodes.Dots(3, Palette.WoundDot); dots.Position = new Vector3(22, 2, -2); _root.AddChild(dots);
        foreach (var (fixedSize, depth) in new[] { (true, true), (false, true), (false, false) })
            _root.AddChild(new Label3D { Text = "Warm 6\"", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, PixelSize = fixedSize ? Palette.ScreenPixel : 0.01f,
                FixedSize = fixedSize, OutlineSize = depth ? 10 : 0, NoDepthTest = depth, Position = new Vector3(16, 2, -2 - (fixedSize ? 1 : 0) - (depth ? 0 : 2)) });
        // The Hud parts the table's own viewport draws over it (unit markers, floating results): themed controls in a canvas layer.
        var layer = new CanvasLayer(); _viewport.AddChild(layer);
        var panel = new PanelContainer { Theme = Theming.Ui.Theme, Position = new Vector2(8, 8) }; layer.AddChild(panel);
        var row = new HBoxContainer(); panel.AddChild(row);
        row.AddChild(Hud.Roster.Chip("Shaken"));
        row.AddChild(new Label { Text = "6\" 3 of 5" });
        if (Theming.Ui.Icon("shaken") is { } icon) row.AddChild(new TextureRect { Texture = icon, CustomMinimumSize = new Vector2(16, 16), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize });
        row.AddChild(new ProgressBar { CustomMinimumSize = new Vector2(64, 6), Value = 50, ShowPercentage = false });
        row.AddChild(new Button { Text = "Undo (Ctrl+Z)" });
        row.AddChild(new Button { Text = "Log", Disabled = true });
        foreach (var v in new[] { Theming.Variation.Banner, Theming.Variation.BannerDetail, Theming.Variation.Display, Theming.Variation.Numerals, Theming.Variation.Dim })
            row.AddChild(new Label { Text = "Round 1", ThemeTypeVariation = v });
        row.AddChild(new PanelContainer { ThemeTypeVariation = Theming.Variation.Badge, CustomMinimumSize = new Vector2(20, 20) });
        row.AddChild(new PanelContainer { ThemeTypeVariation = Theming.Variation.DieFace, CustomMinimumSize = new Vector2(20, 20) });
        await board;
    }

    /// <summary>Vertex-coloured miniatures dressed as built-in terrain is (TerrainLook), one per grain by a material name of that family,
    /// foliage also cut out, so the dressed materials compile with no built-in mesh on hand.</summary>
    static async Task<IReadOnlyList<Node3D?>> Dressed()
    {
        var png = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8); png.Fill(new Color(0.5f, 0.5f, 0.45f));
        var bytes = png.SavePngToBuffer();
        var dressed = new List<Node3D?>();
        foreach (var (variant, word) in new[] { (WarmupGlb.Variant.VertexColour, "rock"), (WarmupGlb.Variant.VertexColour, "dirt"), (WarmupGlb.Variant.VertexColour, "leaf"),
                     (WarmupGlb.Variant.CutOut, "leaf"), (WarmupGlb.Variant.VertexColour, "bark"), (WarmupGlb.Variant.VertexColour, "metal") })
        {
            if (Miniature(variant, bytes) is not { } scene) continue;
            Rename(scene, word);
            await TerrainLook.ApplyAsync(scene);
            dressed.Add(scene);
        }
        return dressed;
    }

    static void Rename(Node node, string name)
    {
        if (node is MeshInstance3D { Mesh: { } mesh })
            for (var i = 0; i < mesh.GetSurfaceCount(); i++) if (mesh.SurfaceGetMaterial(i) is { } m) m.ResourceName = name;
        foreach (var child in node.GetChildren()) Rename(child, name);
    }

    /// <summary>One warm-up miniature, imported as a player's file is and settled the same way (MeshLoader).</summary>
    static Node3D? Miniature(WarmupGlb.Variant variant, byte[] png)
    {
        var doc = new GltfDocument(); var state = new GltfState();
        if (doc.AppendFromBuffer(WarmupGlb.Build(variant, png), "", state) != Error.Ok) return null;
        foreach (var mesh in state.GetMeshes()) mesh.Mesh?.GenerateLods(60, 0, []);
        if (doc.GenerateScene(state) is not Node3D scene) return null;
        MeshLoader.Settle(scene);
        scene.Scale = Vector3.One * 1.2f;
        return scene;
    }
}
