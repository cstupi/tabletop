using System.Reflection;
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestProperties
{
    public const string ResourceName = "Tabletop.GameServer.Content.grimdark-future-core.properties.json";
    public static PropertySetDocument CoreDocument { get; } = PropertiesJson.Parse(ReadResource());
    public static PropertySet Core { get; } = PropertiesJson.ToSet(CoreDocument, Guid.CreateVersion7);

    public static PropertySet WithSpells(params EntryDocument[] spells) =>
        PropertiesJson.ToSet(CoreDocument with { Name = "Core + test spells", Entries = [.. CoreDocument.Entries, .. spells] }, Guid.CreateVersion7);

    static string ReadResource()
    {
        using var s = typeof(PropertySet).Assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidOperationException($"{ResourceName} is not embedded");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
