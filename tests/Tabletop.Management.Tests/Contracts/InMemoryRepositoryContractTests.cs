using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Layouts;
using Tabletop.Management.Games;
using Tabletop.Management.Identity;

namespace Tabletop.Management.Tests.Contracts;

/// <summary>The in-memory fakes ManagementFactory registers.</summary>
public sealed class InMemoryRepositoryContractTests : RepositoryContract
{
    private readonly InMemoryRows _rows = new();

    public InMemoryRepositoryContractTests()
    {
        Accounts = new InMemoryAccountRepository(_rows);
        Assets = new InMemoryAssetRepository(_rows);
        Shared = new InMemorySharedAssetRepository(_rows);
        Folders = new InMemoryFolderRepository(_rows);
        Armies = new InMemoryArmyRepository(_rows);
        Layouts = new InMemoryLayoutRepository(_rows);
        Games = new InMemoryGameRepository(_rows);
        Kinds = new InMemoryCatalogKinds(_rows);
    }

    protected override IAccountRepository Accounts { get; }
    protected override IAssetRepository Assets { get; }
    protected override ISharedAssetRepository Shared { get; }
    protected override IFolderRepository Folders { get; }
    protected override IArmyRepository Armies { get; }
    protected override ILayoutRepository Layouts { get; }
    protected override IGameRepository Games { get; }
    protected override ICatalogKinds Kinds { get; }

    protected override Task SeedProperties(string hash, string name, string gameRules, DateTimeOffset seededAt) { _rows.SeedProperties(hash, name, gameRules, seededAt); return Task.CompletedTask; }
    protected override Task SeedCatalogEntry(string hash, string name, string kind, bool takesParameter, string ruleText, bool played) { _rows.SeedCatalogEntry(hash, name, kind, takesParameter, ruleText, played); return Task.CompletedTask; }
    protected override Task SeedEffect(string hash, string entryName, Guid id, int ordinal, string duration, Guid? parentId = null) { _rows.SeedEffect(hash, entryName, id, ordinal, duration, parentId); return Task.CompletedTask; }
    protected override Task SeedGameEvent(Guid gameId, long seq) { _rows.SeedGameEvent(gameId, seq); return Task.CompletedTask; }
    protected override Task<int> GameEventCount(Guid gameId) { lock (_rows.Gate) return Task.FromResult(_rows.GameEvents.Count(e => e.GameId == gameId)); }
    protected override Task PickArmy(Guid gameId, Guid accountId, Guid armyId) { _rows.PickArmy(gameId, accountId, armyId); return Task.CompletedTask; }
    protected override Task FreezeArmy(Guid gameId, Guid accountId, ArmySnapshot snapshot) { _rows.FreezeArmy(gameId, accountId, snapshot); return Task.CompletedTask; }
    protected override Task SeedGameTable(Guid gameId, Guid textureAssetId)
    {
        _rows.SeedGameTable(gameId, new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfAsset(textureAssetId), [], [], []));
        return Task.CompletedTask;
    }

    protected override Task BackdateBlob(Guid ownerId, string hash, TimeSpan age)
    {
        lock (_rows.Gate) _rows.Blobs[(ownerId, hash)].CreatedAt -= age;
        return Task.CompletedTask;
    }
}
