using Tabletop.Management.Content.Assets;
using Tabletop.Management.Games;

namespace Tabletop.Management.Identity;

/// <summary>Deletion is total. Cascades handle what hangs below the account; games hang above it and are deleted one by one, log included.</summary>
public sealed class AccountDeleter(IAccountRepository accounts, IGameRepository games, IBlobStore blobs, StytchClient stytch, ILogger<AccountDeleter> log)
{
    public async Task<bool> Delete(Guid accountId)
    {
        if (await accounts.Get(accountId) is null) return false;
        var keys = await accounts.BlobKeys(accountId);
        // Games it plays in, plus games it created and has since left: game.created_by has no on-delete action, so a game it created must go too.
        foreach (var gameId in await accounts.GameIds(accountId)) await games.Delete(gameId);
        await accounts.Delete(accountId);
        if (keys.Count > 0) await blobs.Delete(keys);
        try { await stytch.DeleteUser(accountId.ToString()); }
        catch (StytchException e) when (e.Status == 404) { log.LogWarning("Stytch had no user for account {AccountId}", accountId); }
        return true;
    }
}
