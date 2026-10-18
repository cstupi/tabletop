namespace Tabletop.Protocol;

/// <summary>Sent on every HTTP request and in Connect; management answers 426 when below its minimum.</summary>
public static class ProtocolVersion
{
    public const int Current = 4;
    public const string Header = "Tabletop-Client-Version";
    public const int UpgradeRequiredStatusCode = 426;
    /// <summary>On management's responses once a built-in catalog is published: its version (builtin content spec → Serving).</summary>
    public const string BuiltinHeader = "Tabletop-Builtin-Version";
}
