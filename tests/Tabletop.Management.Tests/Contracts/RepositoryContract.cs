using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Layouts;
using Tabletop.Management.Games;
using Tabletop.Management.Identity;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests.Contracts;

/// <summary>The repositories' contracts, run against the in-memory fakes (unit) and the Postgres repositories (E2E).
/// A backend shares its rows across a class's tests, so each test makes its own accounts and never assumes an empty table.
/// The Seed and Pick members write the rows no repository writes: the content seeder's Properties sets, and the game server's
/// lobby picks, frozen armies, game table and event log.</summary>
public abstract partial class RepositoryContract
{
    protected abstract IAccountRepository Accounts { get; }
    protected abstract IAssetRepository Assets { get; }
    protected abstract IFolderRepository Folders { get; }
    protected abstract IArmyRepository Armies { get; }
    protected abstract ILayoutRepository Layouts { get; }
    protected abstract IGameRepository Games { get; }
    protected abstract ICatalogKinds Kinds { get; }

    protected abstract Task SeedProperties(string hash, string name, string gameRules, DateTimeOffset seededAt);
    /// <summary>A model entry carries aggregation "all"; the others none.</summary>
    protected abstract Task SeedCatalogEntry(string hash, string name, string kind, bool takesParameter, string ruleText, bool played);
    /// <summary>A top-level effect row of a seeded entry, unless a parent is named.</summary>
    protected abstract Task SeedEffect(string hash, string entryName, Guid id, int ordinal, string duration, Guid? parentId = null);
    protected abstract Task SeedGameEvent(Guid gameId, long seq);
    protected abstract Task<int> GameEventCount(Guid gameId);
    protected abstract Task PickArmy(Guid gameId, Guid accountId, Guid armyId);
    protected abstract Task FreezeArmy(Guid gameId, Guid accountId, ArmySnapshot snapshot);
    protected abstract Task SeedGameTable(Guid gameId, Guid textureAssetId);
    /// <summary>Moves the blob row's created_at back by age, as time passing would.</summary>
    protected abstract Task BackdateBlob(Guid ownerId, string hash, TimeSpan age);

    protected async Task<Guid> NewAccount(string name = "A")
    {
        var id = Guid.CreateVersion7();
        await Accounts.Insert(id, name);
        return id;
    }

    protected async Task<string> NewProperties(string gameRules = "grimdark-future")
    {
        var hash = "sha256:contract-" + Guid.NewGuid().ToString("N");
        await SeedProperties(hash, "Set " + hash[^6..], gameRules, DateTimeOffset.UtcNow);
        return hash;
    }

    protected static CreateGameRequest GameRequest(string hash, string name = "G") => new(name, hash, SchedulerShape.PlayerTurn, Enforcement.Warn, false);

    protected static CreateAssetRequest Mini(string? hash = null, string name = "Mini", long size = 100) =>
        new(name, AssetKind.Miniature, hash ?? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray())), size, "model/gltf-binary", 1, new Orientation(0, 0, 0), Base.Circle(1));

    protected static CreateAssetRequest Texture(string name = "Board") => Mini(name: name) with { Kind = AssetKind.Texture, ContentType = "image/png", ModelledBase = null };

    protected static Army SmallArmy(string name = "Army") => new(Guid.CreateVersion7(), name, "grimdark-future", "test",
    [
        new Unit(Guid.CreateVersion7(), 0, "Squad", new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "5+" }, [new PropertyInstance("Fearless", null)],
        [
            new ModelSlot(Guid.CreateVersion7(), 0, "Sergeant", new Dictionary<string, string> { ["Tough"] = "1" }, [new PropertyInstance("Hero", null)], Base.Circle(1), 1.5, null,
                [new Weapon(Guid.CreateVersion7(), "Rifle", new Dictionary<string, string> { ["Range"] = "24", ["Attacks"] = "1" }, [new PropertyInstance("AP", 1)])], "book"),
            new ModelSlot(Guid.CreateVersion7(), 1, "Trooper", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, [], "book"),
        ]),
        new Unit(Guid.CreateVersion7(), 1, "Walker", new Dictionary<string, string>(), [],
            [new ModelSlot(Guid.CreateVersion7(), 0, "Walker", new Dictionary<string, string>(), [], Base.Oval(2, 1.5), 3, null, [], null)]),
        new Unit(Guid.CreateVersion7(), 2, "Swarm", new Dictionary<string, string> { ["Quality"] = "5+", ["Defense"] = "6+" }, [],
            [new ModelSlot(Guid.CreateVersion7(), 0, "Swarm", new Dictionary<string, string>(), [], Base.Circle(1), 1.5, null, [], "book")], new UnitReserve("Spawn", Guid.CreateVersion7())),
    ]);

    protected static TableState SmallLayout(string? name = "Ruins") => new(Guid.CreateVersion7(), name, new TableSize(72, 48), VisualRef.OfBuiltin("grass"),
        [new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill"), new Point3(12, 12, 0), new Yaw(45), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 2, TerrainDeclarations.Cover, "hill"))])],
        [new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(36, 24, 0), new Yaw(0), 3, "centre", VisualRef.OfBuiltin("ring"))],
        [new DeploymentZone(Guid.CreateVersion7(), "frontline/2", 2), new DeploymentZone(Guid.CreateVersion7(), "frontline/1", 1)]);
}
