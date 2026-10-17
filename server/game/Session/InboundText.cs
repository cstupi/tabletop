using System.Text.Json;

namespace Tabletop.GameServer.Session;

/// <summary>
/// Every string a peer sends is shown to other players (labels, notes, conditions, names) or written to a row. C0 controls (NUL above
/// all: Postgres text refuses it) and the bidirectional overrides and isolates (U+202A–202E, U+2066–2069), which can make a label read
/// as something else, are refused wherever they appear in a frame, property names included.
/// </summary>
public static class InboundText
{
    public static bool IsForbidden(char c) => c < ' ' || c == '\u007F' || c is >= '‪' and <= '‮' || c is >= '⁦' and <= '⁩';

    /// <summary>True when no string or property name in the JSON holds a forbidden character. JSON this reader cannot walk (too deep,
    /// malformed) is not clean: the frame is refused rather than passed unchecked. A string converter on the wire options was not used,
    /// because the same options read the server's own frames on the client, where rule text legitimately holds line breaks.</summary>
    public static bool IsClean(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 64 });
        try
        {
            while (reader.Read())
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName && (reader.ValueIsEscaped || HasHighOrControl(reader.ValueSpan)))
                    foreach (var c in reader.GetString() ?? "") if (IsForbidden(c)) return false;
        }
        catch (JsonException) { return false; }
        return true;
    }

    /// <summary>A forbidden character in raw UTF-8 is a control byte or a multi-byte sequence; plain ASCII text needs no decoding.</summary>
    static bool HasHighOrControl(ReadOnlySpan<byte> raw)
    {
        foreach (var b in raw) if (b < 0x20 || b >= 0x7F) return true;
        return false;
    }
}
