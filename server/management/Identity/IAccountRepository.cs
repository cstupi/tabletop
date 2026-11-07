namespace Tabletop.Management.Identity;

public enum UsernameResult { Set, Taken, Missing }

/// <summary>The account row. AccountRepository is the Postgres implementation.</summary>
public interface IAccountRepository
{
    Task<AccountRow?> Get(Guid id);
    /// <summary>A no-op when the row exists.</summary>
    Task Insert(Guid id, string name);
    Task<bool> Rename(Guid id, string name);
    /// <summary>Sets or (null) clears the username. Taken when another account holds it in any case; Missing for no such account.</summary>
    Task<UsernameResult> SetUsername(Guid id, string? username);
    /// <summary>The S3 keys of every blob the account owns.</summary>
    Task<IReadOnlyList<string>> BlobKeys(Guid accountId);
    /// <summary>Every game the account created (game.created_by), whether or not it still plays in it.</summary>
    Task<IReadOnlyList<Guid>> HostedGameIds(Guid accountId);
    /// <summary>Every game the account plays in and did not create.</summary>
    Task<IReadOnlyList<Guid>> JoinedGameIds(Guid accountId);
    /// <summary>Deletes the row and what hangs below it: armies, assets, blobs, folders, layouts, its participant rows.
    /// A game it created must be deleted first: game.created_by has no on-delete action.</summary>
    Task Delete(Guid accountId);
}
