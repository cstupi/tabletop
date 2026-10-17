using Tabletop.Protocol;

namespace Tabletop.Management.Games;

/// <summary>Games, their participants, and the pinned Properties sets they are created from. GameRepository is the Postgres implementation.</summary>
public interface IGameRepository
{
    /// <summary>One set per (game rules, name): the most recently seeded.</summary>
    Task<IReadOnlyList<PropertiesSummary>> ListProperties();
    /// <summary>Every catalog entry of a pinned set, by name; null when no set has this hash.</summary>
    Task<IReadOnlyList<CatalogRuleText>?> RuleTexts(string hash);
    /// <summary>A lobby game with the creator as its only participant and a fresh dice seed; null for an unknown properties hash.</summary>
    Task<GameInfo?> Create(Guid creator, CreateGameRequest r);
    /// <summary>Every game, descending by id, before the cursor.</summary>
    Task<IReadOnlyList<GameSummary>> List(Http.Keyset page);
    Task<GameInfo?> Get(Guid id);
    Task<bool> IsParticipant(Guid id, Guid accountId);
    /// <summary>Stays true after the creator leaves.</summary>
    Task<bool> IsCreator(Guid id, Guid accountId);
    /// <summary>A participant row with no team, at any status; joining twice is the same row. Null for an unknown game.</summary>
    Task<GameInfo?> Join(Guid id, Guid accountId);
    /// <summary>False for an unknown game or a non-participant. The last leave ends the game, under concurrent leaves too.</summary>
    Task<bool> Leave(Guid id, Guid accountId);
    Task<bool> End(Guid id);
    /// <summary>The game, everything below it, and its event log.</summary>
    Task<bool> Delete(Guid id);
    /// <summary>Every uploaded asset in play (the participants' picked armies' slots, their frozen snapshots, and the game's table) that a participant of the game owns.</summary>
    Task<IReadOnlyList<ManifestRow>> ManifestAssets(Guid id);
}
