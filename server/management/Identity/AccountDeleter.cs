using Dapper;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Games;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Identity;

/// <summary>Deletion is total. Cascades handle what hangs below the account; games hang above it and are deleted one by one, log included.</summary>
public sealed class AccountDeleter(Database db, GameRepository games, IBlobStore blobs, StytchClient stytch, ILogger<AccountDeleter> log)
{
    public async Task<bool> Delete(Guid accountId)
    {
        IReadOnlyList<string> keys; IReadOnlyList<Guid> gameIds;
        await using (var c = await db.Open())
        {
            if (!await c.ExecuteScalarAsync<bool>("select exists(select 1 from account where id = @accountId)", new { accountId })) return false;
            keys = (await c.QueryAsync<string>("select s3_key from blob where owner_id = @accountId", new { accountId })).ToList();
            // Games it plays in, plus games it created and has since left: game.created_by has no on-delete action, so a game it created must go too.
            gameIds = (await c.QueryAsync<Guid>(@"select game_id from participant where account_id = @accountId
                                                 union select id from game where created_by = @accountId", new { accountId })).ToList();
        }
        foreach (var gameId in gameIds) await games.Delete(gameId);
        await using (var c = await db.Open())
            await c.ExecuteAsync("delete from account where id = @accountId", new { accountId });
        if (keys.Count > 0) await blobs.Delete(keys);
        try { await stytch.DeleteUser(accountId.ToString()); }
        catch (StytchException e) when (e.Status == 404) { log.LogWarning("Stytch had no user for account {AccountId}", accountId); }
        return true;
    }
}
