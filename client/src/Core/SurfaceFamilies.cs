namespace Tabletop.Client.Core;

/// <summary>What a built-in terrain surface is made of, for its surface grain (spec 2026-10-19-tabletop-look-design.md → Terrain).</summary>
public enum SurfaceFamily { Stone, Ground, Foliage, Wood, Metal, Liquid, Plain }

public static class SurfaceFamilies
{
    static readonly (SurfaceFamily Family, string[] Words)[] Words =
    [
        (SurfaceFamily.Plain, ["glass", "lit", "smog", "haze", "spore", "glow", "warning", "sign"]),
        (SurfaceFamily.Liquid, ["water", "sludge", "goo", "murk", "poison", "shallows", "bubble", "foam"]),
        (SurfaceFamily.Foliage, ["leaf", "leaves", "grass", "ivy", "fern", "needle", "crop", "crown", "canopy", "reed", "moss", "shrub", "plant",
            "tuft", "bush", "vine", "frond", "palm", "stalk", "blade", "thorn", "flower", "lily", "fruit", "lichen", "weed", "foliage"]),
        (SurfaceFamily.Wood, ["bark", "timber", "wood", "stake", "crate", "plank", "log", "branch", "root", "trunk", "beam", "twig"]),
        (SurfaceFamily.Metal, ["metal", "iron", "steel", "bronze", "rust", "rebar", "wire", "mine", "drum", "pipe", "girder", "panel", "trim-metal"]),
        (SurfaceFamily.Ground, ["earth", "dirt", "soil", "ground", "sand", "mud", "yard", "floor", "mat", "bank", "dust", "gravel", "flock",
            "crust", "stain", "scum", "ash", "litter", "base", "bed", "dune", "pebble", "scree", "bag"]),
    ];

    /// <summary>By the glTF material's name; a metallic surface (0.3 and up) is metal, an alpha-blended one liquid unless it is haze.
    /// A name nothing matches is stone.</summary>
    public static SurfaceFamily Of(string name, float metallic, bool blended)
    {
        var lower = name.ToLowerInvariant();
        foreach (var (family, words) in Words)
        {
            if (family == SurfaceFamily.Ground && metallic >= 0.3f) break;
            if (words.Any(lower.Contains)) return family;
        }
        if (blended) return SurfaceFamily.Liquid;
        return metallic >= 0.3f ? SurfaceFamily.Metal : SurfaceFamily.Stone;
    }
}
