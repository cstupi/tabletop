using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Client.Theming;
using Tabletop.Content;

namespace Tabletop.Client.Panels;

/// <summary>The Table tool: width × length with Set, then the board prints as 48 px swatches, the current one framed.</summary>
public partial class SetupTableTool : SetupToolBody
{
    SpinBox _width = null!, _length = null!; HFlowContainer _swatches = null!;
    readonly ButtonGroup _group = new() { AllowUnpress = false };
    Action? _onBuiltins;

    public SetupTableTool() { }
    public SetupTableTool(GameScreen screen) : base(screen) { }

    protected override void Build()
    {
        var size0 = SizeNow(S);   // the board may not have arrived yet
        var size = Row(); AddChild(size);
        _width = new SpinBox { MinValue = SetupLimits.MinEdge, MaxValue = SetupLimits.MaxEdge, Value = size0.Width, Suffix = "\"", TooltipText = SetupLimits.SizeHint, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _length = new SpinBox { MinValue = SetupLimits.MinEdge, MaxValue = SetupLimits.MaxEdge, Value = size0.Length, Suffix = "\"", TooltipText = SetupLimits.SizeHint, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        size.AddChild(_width); size.AddChild(new Label { Text = "×", ThemeTypeVariation = Variation.Dim }); size.AddChild(_length);
        size.AddChild(Btn("Set", () => Guarded.Send(S, SetupEdits.TableSize(_width.Value, _length.Value))));
        AddChild(_swatches = new HFlowContainer());
        _swatches.AddThemeConstantOverride("h_separation", Space.S); _swatches.AddThemeConstantOverride("v_separation", Space.S);
        FillSwatches();
        _onBuiltins = FillSwatches; BuiltinContent.Changed += _onBuiltins;
    }

    protected override void Gone() => BuiltinContent.Changed -= _onBuiltins;

    protected override void Refresh()
    {
        if (S.Draft is { } d && !_width.HasFocus() && !_length.HasFocus() && !_width.GetLineEdit().HasFocus() && !_length.GetLineEdit().HasFocus())
        { _width.SetValueNoSignal(d.Size.Width); _length.SetValueNoSignal(d.Size.Length); }
        ShowSwatches();
    }

    /// <summary>The board prints as 48 px swatches; they follow the catalog in use.</summary>
    void FillSwatches()
    {
        if (!IsInstanceValid(_swatches)) return;
        foreach (var c in _swatches.GetChildren()) { _swatches.RemoveChild(c); c.QueueFree(); }
        foreach (var name in BuiltinContent.Textures)
        {
            var swatch = new Button { ToggleMode = true, ButtonGroup = _group, TooltipText = SetupWords.Texture(name), CustomMinimumSize = new Vector2(56, 56),
                ExpandIcon = true, FocusMode = FocusModeEnum.None };
            swatch.SetMeta("texture", name);
            swatch.AddThemeStyleboxOverride("pressed", Framed());
            swatch.AddThemeStyleboxOverride("hover_pressed", Framed());
            var pick = name;
            swatch.Pressed += () => { if (S.Draft?.Texture.Builtin != pick) Guarded.Send(S, SetupEdits.Texture(pick)); };
            _swatches.AddChild(swatch);
            Guarded.Run(async () => { if (await Swatches.Of(pick) is { } texture && IsInstanceValid(swatch)) swatch.Icon = texture; }, _ => { });
        }
        ShowSwatches();
    }

    void ShowSwatches()
    {
        if (!IsInstanceValid(_swatches)) return;
        var current = S.Draft?.Texture.Builtin;
        foreach (var c in _swatches.GetChildren()) if (c is Button b) b.SetPressedNoSignal(b.GetMeta("texture").AsString() == current);
    }
}

/// <summary>A board print as a small swatch, fetched by hash as the slab fetches it (the packed file in a development run), its height channel dropped.</summary>
public static class Swatches
{
    static readonly Dictionary<string, Task<Texture2D?>> Made = new();

    public static Task<Texture2D?> Of(string name)
    {
        if (!Made.TryGetValue(name, out var task) || task.IsFaulted) Made[name] = task = Build(name);
        return task;
    }

    static async Task<Texture2D?> Build(string name)
    {
        var path = BuiltinCatalog.TexturePath(name);
        var file = BuiltinContent.File(path) ?? await BuiltinContent.FileAsync(path);
        if (file is null) return null;
        Image? image;
        if (file.StartsWith("res://", StringComparison.Ordinal))
        {
            if (!ResourceLoader.Exists(file) || ResourceLoader.Load<Texture2D>(file)?.GetImage() is not { } packed) return null;
            image = (Image)packed.Duplicate();
            if (image.IsCompressed()) image.Decompress();
        }
        else
        {
            var bytes = await Task.Run(() => System.IO.File.ReadAllBytes(file));
            image = new Image();
            if (image.LoadPngFromBuffer(bytes) != Error.Ok) return null;
        }
        image.Convert(Image.Format.Rgb8);   // the alpha is the print's height
        image.Resize(48, 48, Image.Interpolation.Lanczos);
        return ImageTexture.CreateFromImage(image);
    }
}
