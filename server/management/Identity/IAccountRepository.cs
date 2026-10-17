namespace Tabletop.Management.Identity;

/// <summary>The account row. AccountRepository is the Postgres implementation.</summary>
public interface IAccountRepository
{
    Task<AccountRow?> Get(Guid id);
    /// <summary>A no-op when the row exists.</summary>
    Task Insert(Guid id, string name);
    Task<bool> Rename(Guid id, string name);
    /// <summary>The S3 keys of every blob the account owns.</summary>
    Task<IReadOnlyList<string>> BlobKeys(Guid accountId);
    /// <summary>Every game the account plays in, plus every game it created and has since left.</summary>
    Task<IReadOnlyList<Guid>> GameIds(Guid accountId);
    /// <summary>Deletes the row and what hangs below it: armies, assets, blobs, folders, layouts, its participant rows.
    /// A game it created must be deleted first: game.created_by has no on-delete action.</summary>
    Task Delete(Guid accountId);
}
