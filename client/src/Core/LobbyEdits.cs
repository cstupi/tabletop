using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The lobby edits the panels and Home send that no other Core type builds. The browser's lobby page and rulesOptions.ts build the same.</summary>
public static class LobbyEdits
{
    public static PickTeam Watch() => new(null);
    public static CreateTeam CreateTeam() => new();
    public static StartSetup StartSetup() => new();
    public static EndSetup EndSetup() => new();

    /// <summary>Apply configuration: the trimmed name, the game's properties hash, WaitsBetweenSteps false, the options map without defaults.</summary>
    public static SetConfiguration Configure(GameInfo game, string name, SchedulerShape shape, Enforcement enforcement,
        RulesOptionCatalog catalog, IReadOnlyDictionary<string, string> chosen) =>
        new(name.Trim(), game.PropertiesHash, shape, enforcement, WaitsBetweenSteps: false, RulesOptions.MapOf(catalog, chosen));

    /// <summary>The army chosen in Create game, picked once the creator is connected: no team named.</summary>
    public static PickArmy ArmyFromCreate(Guid armyId) => new(armyId);
}
