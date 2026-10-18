namespace Tabletop.Management.Http;

/// <summary>Size and count limits on what an account writes, and the text check every stored name goes through.</summary>
public static class Limits
{
    /// <summary>Account, folder, asset, game, layout, army, unit, slot and weapon names.</summary>
    public const int Name = 64;
    /// <summary>Volume notes and other free text.</summary>
    public const int Notes = 2000;
    /// <summary>An army's Army Forge source (share link or list id).</summary>
    public const int Source = 512;
    /// <summary>Armies, layouts and folders per account, each.</summary>
    public const int PerAccount = 200;
    /// <summary>Assets one bulk request names (file, unfile, delete).</summary>
    public const int Bulk = 200;

    /// <summary>Null when a bulk request names 1 to <see cref="Bulk"/> assets, else the problem detail.</summary>
    public static string? BulkIds(IReadOnlyList<Guid>? ids) =>
        ids is null || ids.Count == 0 ? "assetIds is required." : ids.Count > Bulk ? $"A request names at most {Bulk} assets." : null;

    /// <summary>Null when the text is acceptable, else the problem detail. Control characters (Cc) and bidirectional
    /// formatting characters are refused everywhere; free text (multiline) admits line feeds and tabs.</summary>
    public static string? Text(string? value, string field, int max, bool required = true, bool multiline = false)
    {
        if (value is null) return required ? $"{field} is required." : null;
        if (required && string.IsNullOrWhiteSpace(value)) return $"{field} is required.";
        if (value.Length > max) return $"{field} is at most {max} characters.";
        foreach (var ch in value)
        {
            if (multiline && ch is '\n' or '\r' or '\t') continue;
            if (char.IsControl(ch) || IsBidiFormat(ch)) return $"{field} contains a control or bidirectional formatting character.";
        }
        return null;
    }

    /// <summary>LRM, RLM, ALM, the embeddings and overrides (U+202A–U+202E) and the isolates (U+2066–U+2069).</summary>
    public static bool IsBidiFormat(char ch) => ch is '‎' or '‏' or '؜' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');
}
