using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

/// <summary>
/// The management schema's rows in memory, shared by the in-memory repositories. Every read and write holds Gate, as one
/// transaction. The foreign keys the repositories lean on are checked (a missing account, a missing blob, a game still naming
/// its creator) and the cascades the migrations declare are applied by hand. Ids come from NextId, v7 and strictly increasing,
/// so creation order is id order.
/// The Seed and Pick members write the rows no management repository writes: the content seeder's Properties sets, and the
/// game server's lobby picks, frozen armies, game table and event log.
/// </summary>
public sealed class InMemoryRows
{
    public sealed class AccountRow { public required string Name; public bool StarterSeeded; }
    public sealed class BlobRow { public required string Key; public long ByteSize; public required string ContentType; public bool Uploaded; public DateTimeOffset CreatedAt = DateTimeOffset.UtcNow; }
    public sealed class AssetRow
    {
        public required Guid Owner; public required string Hash; public required string Name; public AssetKind Kind;
        public double Scale; public Orientation Orientation; public Base? ModelledBase;
        public List<TerrainVolume> Volumes = [];
    }
    public sealed class FolderRow { public required Guid Owner; public Guid? ParentId; public required string Name; }
    public sealed class ArmyRow { public required Guid Owner; public required Army Army; }
    public sealed class LayoutRow { public required Guid Owner; public required TableState State; }
    public sealed record PropertiesRow(string Name, string GameRules, DateTimeOffset SeededAt);
    public sealed record CatalogEntryRow(string Hash, string Name, string Kind, bool TakesParameter, string RuleText, bool Played);
    public sealed record EffectRow(string Hash, string EntryName, Guid Id, int Ordinal, string Duration, Guid? ParentId);
    public sealed class GameRow
    {
        public required string Name; public GameStatus Status; public required string GameRules; public required string PropertiesHash;
        public SchedulerShape SchedulerShape; public Enforcement Enforcement; public bool WaitsBetweenSteps; public long RngSeed;
        public Guid CreatedBy; public DateTimeOffset CreatedAt;
    }
    public sealed class OfferRow
    {
        public required Guid Sharer; public required Guid Recipient; public string? Folder; public string State = "open";
        public DateTimeOffset CreatedAt = DateTimeOffset.UtcNow; public DateTimeOffset? DecidedAt; public List<Guid> AssetIds = [];
    }
    public sealed class ParticipantRow { public Guid Id; public Guid GameId; public Guid AccountId; public Guid? ArmyId; public ArmySnapshot? Snapshot; }

    public readonly object Gate = new();
    private long _lastMillisecond;

    public readonly Dictionary<Guid, AccountRow> Accounts = new();
    public readonly Dictionary<(Guid Owner, string Hash), BlobRow> Blobs = new();
    public readonly Dictionary<Guid, AssetRow> Assets = new();
    public readonly Dictionary<Guid, FolderRow> Folders = new();
    /// <summary>asset_folder, in filing order.</summary>
    public readonly List<(Guid AssetId, Guid FolderId)> Filed = new();
    /// <summary>share_offer with its share_offer_asset rows.</summary>
    public readonly Dictionary<Guid, OfferRow> Offers = new();
    /// <summary>share_block.</summary>
    public readonly HashSet<(Guid Recipient, Guid Sharer)> Blocks = new();
    public readonly Dictionary<Guid, ArmyRow> Armies = new();
    public readonly Dictionary<Guid, LayoutRow> Layouts = new();
    public readonly Dictionary<string, PropertiesRow> Properties = new();
    public readonly List<CatalogEntryRow> CatalogEntries = new();
    public readonly List<EffectRow> Effects = new();
    public readonly Dictionary<Guid, GameRow> Games = new();
    public readonly List<ParticipantRow> Participants = new();
    /// <summary>A game's table_state: the draft the game server writes during setup.</summary>
    public readonly Dictionary<Guid, TableState> GameTables = new();
    public readonly List<(Guid GameId, long Seq)> GameEvents = new();

    public Guid NextId()
    {
        lock (Gate)
        {
            _lastMillisecond = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _lastMillisecond + 1);
            return Guid.CreateVersion7(DateTimeOffset.FromUnixTimeMilliseconds(_lastMillisecond));
        }
    }

    /// <summary>A foreign key to account(id).</summary>
    public void RequireAccount(Guid id)
    {
        if (!Accounts.ContainsKey(id)) throw new InvalidOperationException($"foreign key violation: no account {id}");
    }

    public void SeedProperties(string hash, string name, string gameRules, DateTimeOffset? seededAt = null)
    {
        lock (Gate)
        {
            if (Properties.ContainsKey(hash)) throw new InvalidOperationException($"duplicate key: properties {hash}");
            Properties[hash] = new PropertiesRow(name, gameRules, seededAt ?? DateTimeOffset.UtcNow);
        }
    }

    public void SeedCatalogEntry(string hash, string name, string kind, bool takesParameter, string ruleText, bool played = true)
    {
        lock (Gate)
        {
            if (!Properties.ContainsKey(hash)) throw new InvalidOperationException($"foreign key violation: no properties {hash}");
            if (CatalogEntries.Any(e => e.Hash == hash && e.Name == name)) throw new InvalidOperationException($"duplicate key: catalog entry {hash}/{name}");
            CatalogEntries.Add(new CatalogEntryRow(hash, name, kind, takesParameter, ruleText, played));
        }
    }

    public void SeedEffect(string hash, string entryName, Guid id, int ordinal, string duration, Guid? parentId = null)
    {
        lock (Gate)
        {
            if (!CatalogEntries.Any(e => e.Hash == hash && e.Name == entryName)) throw new InvalidOperationException($"foreign key violation: no catalog entry {hash}/{entryName}");
            Effects.Add(new EffectRow(hash, entryName, id, ordinal, duration, parentId));
        }
    }

    public void SeedGameEvent(Guid gameId, long seq)
    {
        lock (Gate)
        {
            if (GameEvents.Contains((gameId, seq))) throw new InvalidOperationException($"duplicate key: game event {gameId}/{seq}");
            GameEvents.Add((gameId, seq));
        }
    }

    /// <summary>The lobby's army pick.</summary>
    public void PickArmy(Guid gameId, Guid accountId, Guid? armyId)
    {
        lock (Gate)
        {
            if (armyId is { } a && !Armies.ContainsKey(a)) throw new InvalidOperationException($"foreign key violation: no army {a}");
            foreach (var p in Participants.Where(p => p.GameId == gameId && p.AccountId == accountId)) p.ArmyId = armyId;
        }
    }

    public void FreezeArmy(Guid gameId, Guid accountId, ArmySnapshot snapshot)
    {
        lock (Gate) foreach (var p in Participants.Where(p => p.GameId == gameId && p.AccountId == accountId)) p.Snapshot = snapshot;
    }

    public void SeedGameTable(Guid gameId, TableState table)
    {
        lock (Gate)
        {
            if (!Games.ContainsKey(gameId)) throw new InvalidOperationException($"foreign key violation: no game {gameId}");
            GameTables[gameId] = table;
        }
    }

    /// <summary>delete from asset: cascades its volumes, filings and offer rows.</summary>
    public void RemoveAsset(Guid id)
    {
        Assets.Remove(id);
        Filed.RemoveAll(f => f.AssetId == id);
        foreach (var o in Offers.Values) o.AssetIds.Remove(id);
    }

    /// <summary>delete from folder: cascades the folders below it and every filing.</summary>
    public void RemoveFolder(Guid id)
    {
        foreach (var child in Folders.Where(f => f.Value.ParentId == id).Select(f => f.Key).ToList()) RemoveFolder(child);
        Folders.Remove(id);
        Filed.RemoveAll(f => f.FolderId == id);
    }

    /// <summary>delete from army: a participant's pick is set null.</summary>
    public void RemoveArmy(Guid id)
    {
        Armies.Remove(id);
        foreach (var p in Participants.Where(p => p.ArmyId == id)) p.ArmyId = null;
    }

    /// <summary>delete from game: cascades participants and the game's table. The event log is a separate delete.</summary>
    public void RemoveGame(Guid id)
    {
        Games.Remove(id);
        Participants.RemoveAll(p => p.GameId == id);
        GameTables.Remove(id);
    }

    /// <summary>delete from account: cascades what hangs below it; refused while a game names it as creator.</summary>
    public void RemoveAccount(Guid id)
    {
        if (Games.Values.Any(g => g.CreatedBy == id)) throw new InvalidOperationException($"foreign key violation: game.created_by names account {id}");
        foreach (var a in Assets.Where(a => a.Value.Owner == id).Select(a => a.Key).ToList()) RemoveAsset(a);
        foreach (var k in Blobs.Keys.Where(k => k.Owner == id).ToList()) Blobs.Remove(k);
        foreach (var f in Folders.Where(f => f.Value.Owner == id).Select(f => f.Key).ToList()) if (Folders.ContainsKey(f)) RemoveFolder(f);
        foreach (var a in Armies.Where(a => a.Value.Owner == id).Select(a => a.Key).ToList()) RemoveArmy(a);
        foreach (var l in Layouts.Where(l => l.Value.Owner == id).Select(l => l.Key).ToList()) Layouts.Remove(l);
        Participants.RemoveAll(p => p.AccountId == id);
        foreach (var o in Offers.Where(o => o.Value.Sharer == id || o.Value.Recipient == id).Select(o => o.Key).ToList()) Offers.Remove(o);
        Blocks.RemoveWhere(b => b.Recipient == id || b.Sharer == id);
        Accounts.Remove(id);
    }
}
