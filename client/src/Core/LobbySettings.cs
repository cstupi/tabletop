using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The lobby's Game settings as the creator edits them (spec 2026-11-21-setup-screen-layout-design.md → Lobby): local edits over the
/// game's values, kept by the game screen across the lobby's rebuilds (a join or an army pick rebuilds it). The creator's Start setup sends
/// <see cref="Pending"/> first; the Rule set applies on its own as soon as it is picked (<see cref="RuleSet"/>).</summary>
public sealed class LobbySettings
{
    readonly Dictionary<string, string> _chosen = new();

    public string? Name { get; set; }
    public SchedulerShape? Shape { get; set; }
    public Enforcement? Enforcement { get; set; }

    public string NameOf(GameInfo game) => Name ?? game.Name;
    public SchedulerShape ShapeOf(GameInfo game) => Shape ?? game.SchedulerShape;
    public Enforcement EnforcementOf(GameInfo game) => Enforcement ?? game.Enforcement;

    /// <summary>Every option of the catalog: my pick, else the game's value.</summary>
    public Dictionary<string, string> ChosenOf(GameInfo game, RulesOptionCatalog catalog) =>
        catalog.Options.ToDictionary(o => o.Key, o => _chosen.TryGetValue(o.Key, out var v) ? v : RulesOptions.ValueOf(o, game.OptionalRules));

    public void Choose(string key, string value) => _chosen[key] = value;

    /// <summary>A new rule set brings its own catalog: my option picks start over.</summary>
    public void ClearOptions() => _chosen.Clear();

    public void Clear() { Name = null; Shape = null; Enforcement = null; _chosen.Clear(); }

    /// <summary>The configuration Start setup sends before starting: null when nothing differs from the game's.</summary>
    public SetConfiguration? Pending(GameInfo game, RulesOptionCatalog catalog)
    {
        var chosen = ChosenOf(game, catalog);
        var differs = NameOf(game).Trim() != game.Name || ShapeOf(game) != game.SchedulerShape || EnforcementOf(game) != game.Enforcement
            || catalog.Options.Any(o => chosen[o.Key] != RulesOptions.ValueOf(o, game.OptionalRules));
        return differs ? LobbyEdits.Configure(game, NameOf(game), ShapeOf(game), EnforcementOf(game), catalog, chosen) : null;
    }

    /// <summary>A picked rule set, sent at once with the game's other values (the edition decides which armies every picker offers); my
    /// other edits stay local.</summary>
    public static SetConfiguration RuleSet(GameInfo game, RulesOptionCatalog catalog, string propertiesHash) =>
        LobbyEdits.Configure(game, game.Name, game.SchedulerShape, game.Enforcement, catalog,
            catalog.Options.ToDictionary(o => o.Key, o => RulesOptions.ValueOf(o, game.OptionalRules)), propertiesHash);

    /// <summary>The Rule set fold's heading: the rule set, activation and enforcement on one line.</summary>
    public static string FoldTitle(string ruleSet, SchedulerShape shape, Enforcement enforcement) =>
        $"{ruleSet} · {(shape == SchedulerShape.PlayerTurn ? "Player turn" : "Alternating")} · {enforcement}";
}
