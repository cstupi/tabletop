using Tabletop.Management.Content.Assets;
using Tabletop.Management.Games;

namespace Tabletop.Management.Identity;

/// <summary>
/// Cascades handle what hangs below the account. The games it hosted (game.created_by, which has no on-delete action) are
/// deleted one by one, log included; the games it joined lose it as /leave would, and the last leave ends one. Its blob rows cascade;
/// an S3 object goes only when no other account's blob row (a copy of a shared asset) still names its key.
/// </summary>
public sealed class AccountDeleter(IAccountRepository accounts, IGameRepository games, IAssetRepository assets, IBlobStore blobs, StytchClient stytch, ILogger<AccountDeleter> log)
{
    public async Task<bool> Delete(Guid accountId)
    {
        if (await accounts.Get(accountId) is null) return false;
        var keys = await accounts.BlobKeys(accountId);
        foreach (var gameId in await accounts.HostedGameIds(accountId)) await games.Delete(gameId);
        foreach (var gameId in await accounts.JoinedGameIds(accountId)) await games.Leave(gameId, accountId);
        await accounts.Delete(accountId);
        keys = await assets.Unreferenced(keys);
        if (keys.Count > 0) await blobs.Delete(keys);
        try { await stytch.DeleteUser(accountId.ToString()); }
        catch (StytchException e) when (e.Status == 404) { log.LogWarning("Stytch had no user for account {AccountId}", accountId); }
        return true;
    }
}
