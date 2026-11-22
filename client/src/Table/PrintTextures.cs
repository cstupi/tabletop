using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Table;

/// <summary>A print with its height in alpha (a board, the table's wood, a surface grain), loaded once per file: the colour with
/// mipmaps, the normal map <see cref="HeightNormals"/> derives from the height, and the mean colour. A packed file comes through
/// the resource loader (its lossless import), a served one from its PNG bytes once its header is within 4096 a side. Decoding,
/// the normal map and the mipmaps run on the thread pool; the textures are made on the main thread the caller awaits on.</summary>
public static class PrintTextures
{
    public sealed record Print(ImageTexture Albedo, ImageTexture Normal, Color Mean);

    static readonly Dictionary<(string File, float Strength), Task<Print?>> Loaded = new();

    /// <summary>At exit: drops the textures before Godot's servers shut down.</summary>
    public static void Clear()
    {
        foreach (var t in Loaded.Values) if (t.IsCompletedSuccessfully && t.Result is { } p) { p.Albedo.Dispose(); p.Normal.Dispose(); }
        Loaded.Clear();
    }

    /// <summary>Null when the file does not load. <paramref name="strength"/> is the slope of a full height rise across one pixel. Main thread.</summary>
    public static Task<Print?> LoadAsync(string file, float strength)
    {
        if (!Loaded.TryGetValue((file, strength), out var task) || task.IsFaulted) Loaded[(file, strength)] = task = Build(file, strength);
        return task;
    }

    static async Task<Print?> Build(string file, float strength)
    {
        var made = await Task.Run(() => Decode(file, strength));
        if (made is not var (albedo, normal, mean)) return null;
        return new Print(ImageTexture.CreateFromImage(albedo), ImageTexture.CreateFromImage(normal), mean);
    }

    static (Image Albedo, Image Normal, Color Mean)? Decode(string file, float strength)
    {
        if (Read(file) is not { } image) return null;
        image.Convert(Image.Format.Rgba8);
        var (w, h) = (image.GetWidth(), image.GetHeight());
        var bytes = image.GetData();
        var normal = Image.CreateFromData(w, h, false, Image.Format.Rgba8, HeightNormals.FromHeights(bytes, w, h, strength));
        normal.GenerateMipmaps();
        var (r, g, b) = HeightNormals.Mean(bytes);
        image.GenerateMipmaps();
        return (image, normal, new Color(r, g, b));
    }

    static Image? Read(string file)
    {
        if (file.StartsWith("res://", StringComparison.Ordinal))
            return ResourceLoader.Exists(file) && ResourceLoader.Load<Texture2D>(file)?.GetImage() is { } packed ? Decompressed(packed) : null;
        try
        {
            var bytes = System.IO.File.ReadAllBytes(file);
            if (ImageHeader.Size(bytes) is not { } size || size.Width > 4096 || size.Height > 4096) { GD.PushWarning($"texture {file} refused"); return null; }
            var image = new Image();
            return image.LoadPngFromBuffer(bytes) == Error.Ok ? image : null;
        }
        catch (System.IO.IOException e) { GD.PushWarning($"texture {file}: {e.Message}"); return null; }
    }

    static Image Decompressed(Image image)
    {
        if (image.IsCompressed()) image.Decompress();
        if (image.HasMipmaps()) image.ClearMipmaps();
        return image;
    }
}
