using Tabletop.Content;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Games;
using Tabletop.Management.Http;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public sealed class InMemoryGameRepository(InMemoryRows rows) : IGameRepository
{
    private List<InMemoryRows.ParticipantRow> Seated(Guid gameId) => rows.Participants.Where(p => p.GameId == gameId).OrderBy(p => p.Id).ToList();

    private GameInfo? Info(Guid id)
    {
        if (!rows.Games.TryGetValue(id, out var g)) return null;
        return new GameInfo(id, g.Name, g.Status, g.CreatedBy, g.GameRules, g.PropertiesHash, g.SchedulerShape, g.Enforcement, g.WaitsBetweenSteps, g.RngSeed, [],
            Seated(id).Select(p => new Participant(p.Id, p.AccountId, rows.Accounts[p.AccountId].Name, null, p.ArmyId)).ToList())
        { RuleSet = RuleSetName(g.PropertiesHash) };
    }

    private string? RuleSetName(string hash) => rows.Properties.TryGetValue(hash, out var p) ? p.Name : null;

    public Task<IReadOnlyList<PropertiesSummary>> ListProperties()
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyList<PropertiesSummary>>(rows.Properties
                .GroupBy(p => (p.Value.GameRules, p.Value.Name))
                .Select(g => g.MaxBy(p => p.Value.SeededAt))
                .OrderByDescending(p => p.Value.SeededAt)
                .Select(p => new PropertiesSummary(p.Key, p.Value.Name, p.Value.GameRules, p.Value.GameRules == RuleSets.Default)).ToList());
    }

    public Task<IReadOnlyList<CatalogRuleText>?> RuleTexts(string hash)
    {
        lock (rows.Gate)
        {
            if (!rows.Properties.ContainsKey(hash)) return Task.FromResult<IReadOnlyList<CatalogRuleText>?>(null);
            return Task.FromResult<IReadOnlyList<CatalogRuleText>?>(rows.CatalogEntries.Where(e => e.Hash == hash).OrderBy(e => e.Name, StringComparer.Ordinal)
                .Select(e => new CatalogRuleText(e.Name, e.Kind, e.TakesParameter, e.RuleText, e.Played)).ToList());
        }
    }

    public Task<IReadOnlyList<GrantableEffect>?> GrantableEffects(string hash)
    {
        lock (rows.Gate)
        {
            if (!rows.Properties.ContainsKey(hash)) return Task.FromResult<IReadOnlyList<GrantableEffect>?>(null);
            return Task.FromResult<IReadOnlyList<GrantableEffect>?>(rows.Effects.Where(e => e.Hash == hash && e.ParentId is null && e.Duration != "interaction")
                .OrderBy(e => e.EntryName, StringComparer.Ordinal).ThenBy(e => e.Ordinal).Select(e => new GrantableEffect(e.EntryName, e.Id, e.Duration)).ToList());
        }
    }

    public Task<GameInfo?> Create(Guid creator, CreateGameRequest r)
    {
        lock (rows.Gate)
        {
            if (!rows.Properties.TryGetValue(r.PropertiesHash, out var properties)) return Task.FromResult<GameInfo?>(null);
            rows.RequireAccount(creator);
            var id = rows.NextId();
            rows.Games.Add(id, new InMemoryRows.GameRow
            {
                Name = r.Name, Status = GameStatus.Lobby, GameRules = properties.GameRules, PropertiesHash = r.PropertiesHash, SchedulerShape = r.SchedulerShape,
                Enforcement = r.Enforcement, WaitsBetweenSteps = r.WaitsBetweenSteps, RngSeed = Random.Shared.NextInt64(), CreatedBy = creator, CreatedAt = DateTimeOffset.UtcNow,
            });
            rows.Participants.Add(new InMemoryRows.ParticipantRow { Id = rows.NextId(), GameId = id, AccountId = creator });
            return Task.FromResult(Info(id));
        }
    }

    public Task<IReadOnlyList<GameSummary>> List(Keyset page, Guid viewer, bool joinedOnly = false)
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyList<GameSummary>>(rows.Games
                .Where(g => page.After is null || g.Key.CompareTo(page.After.Value) < 0)
                .Where(g => !joinedOnly || Seated(g.Key).Any(p => p.AccountId == viewer))
                .OrderByDescending(g => g.Key).Take(page.Limit)
                .Select(g =>
                {
                    var seated = Seated(g.Key);
                    var accounts = seated.Select(p => p.AccountId).Distinct().ToList();   // an account holding several teams is one participant to the browser
                    var teams = rows.Teams.Where(t => t.GameId == g.Key).ToList();
                    var ready = teams.Count(t => seated.Any(p => p.TeamId == t.Id && p.ArmyId is not null));
                    return new GameSummary(g.Key, g.Value.Name, g.Value.Status, g.Value.CreatedBy, g.Value.CreatedAt, accounts.Count, accounts.Select(a => rows.Accounts[a].Name).ToList(), accounts.Contains(viewer),
                        RuleSetName(g.Value.PropertiesHash), teams.Count, ready, g.Value.Progress);
                }).ToList());
    }

    public Task<GameInfo?> Get(Guid id)
    {
        lock (rows.Gate) return Task.FromResult(Info(id));
    }

    public Task<bool> IsParticipant(Guid id, Guid accountId)
    {
        lock (rows.Gate) return Task.FromResult(rows.Participants.Any(p => p.GameId == id && p.AccountId == accountId));
    }

    public Task<bool> IsCreator(Guid id, Guid accountId)
    {
        lock (rows.Gate) return Task.FromResult(rows.Games.TryGetValue(id, out var g) && g.CreatedBy == accountId);
    }

    public Task<GameInfo?> Join(Guid id, Guid accountId)
    {
        lock (rows.Gate)
        {
            if (rows.Games.ContainsKey(id) && !rows.Participants.Any(p => p.GameId == id && p.AccountId == accountId))
            {
                rows.RequireAccount(accountId);
                rows.Participants.Add(new InMemoryRows.ParticipantRow { Id = rows.NextId(), GameId = id, AccountId = accountId });
            }
            return Task.FromResult(Info(id));
        }
    }

    public Task<bool> Leave(Guid id, Guid accountId)
    {
        lock (rows.Gate)
        {
            if (!rows.Games.TryGetValue(id, out var g)) return Task.FromResult(false);
            if (rows.Participants.RemoveAll(p => p.GameId == id && p.AccountId == accountId) == 0) return Task.FromResult(false);
            if (!rows.Participants.Any(p => p.GameId == id)) g.Status = GameStatus.Ended;
            return Task.FromResult(true);
        }
    }

    public Task<bool> End(Guid id)
    {
        lock (rows.Gate)
        {
            if (!rows.Games.TryGetValue(id, out var g)) return Task.FromResult(false);
            g.Status = GameStatus.Ended;
            return Task.FromResult(true);
        }
    }

    public Task<bool> Delete(Guid id)
    {
        lock (rows.Gate)
        {
            rows.GameEvents.RemoveAll(e => e.GameId == id);
            if (!rows.Games.ContainsKey(id)) return Task.FromResult(false);
            rows.RemoveGame(id);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<ManifestRow>> ManifestAssets(Guid id)
    {
        lock (rows.Gate)
        {
            var ids = new HashSet<Guid>();
            foreach (var p in rows.Participants.Where(p => p.GameId == id))
            {
                if (p.Snapshot is { } snapshot) ids.UnionWith(snapshot.Units.SelectMany(u => u.Models).Where(m => m.AssetId is not null).Select(m => m.AssetId!.Value));
                else if (p.ArmyId is { } armyId && rows.Armies.TryGetValue(armyId, out var army))
                    ids.UnionWith(army.Army.Units.SelectMany(u => u.Models).Where(m => m.AssetId is not null).Select(m => m.AssetId!.Value));
            }
            if (rows.GameTables.TryGetValue(id, out var table))
            {
                if (table.Texture.AssetId is { } t) ids.Add(t);
                ids.UnionWith(table.Terrain.Where(p => p.Mesh.AssetId is not null).Select(p => p.Mesh.AssetId!.Value));
                ids.UnionWith(table.Markers.Where(m => m.Visual.AssetId is not null).Select(m => m.Visual.AssetId!.Value));
            }
            return Task.FromResult<IReadOnlyList<ManifestRow>>(ids
                .Where(a => rows.Assets.TryGetValue(a, out var asset) && rows.Blobs[(asset.Owner, asset.Hash)].Uploaded
                    && rows.Participants.Any(p => p.GameId == id && p.AccountId == asset.Owner))
                .Select(a => rows.Assets[a] is var x ? new ManifestRow(a, x.Owner, AssetRules.Kind(x.Kind), x.Hash, rows.Blobs[(x.Owner, x.Hash)].Key, x.Scale, x.Orientation.Pitch, x.Orientation.Yaw, x.Orientation.Roll) : null!)
                .ToList());
        }
    }
}
