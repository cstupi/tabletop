namespace Tabletop.Content;

/// <summary>The Grimdark Future edition an army was imported under. Served is what an Army Forge import stamps.</summary>
public static class ArmyEditions
{
    public const string V35 = "3.5";
    public const string V36 = "3.6";
    public const string Served = V36;
    public static readonly IReadOnlyList<string> All = [V35, V36];
}
