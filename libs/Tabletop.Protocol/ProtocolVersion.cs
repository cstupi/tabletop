namespace Tabletop.Protocol;

/// <summary>Sent on every HTTP request and in Connect; management answers 426 when below its minimum.</summary>
public static class ProtocolVersion
{
    public const int Current = 4;
    public const string Header = "Tabletop-Client-Version";
    public const int UpgradeRequiredStatusCode = 426;
}
