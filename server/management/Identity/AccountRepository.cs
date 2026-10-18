using Dapper;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Identity;

public sealed record AccountRow(Guid Id, string Name);

public sealed class AccountRepository(Database db) : IAccountRepository
{
    public async Task<AccountRow?> Get(Guid id)
    {
        await using var c = await db.Open();
        return await c.QuerySingleOrDefaultAsync<AccountRow>("select id as Id, name as Name from account where id = @id", new { id });
    }

    public async Task Insert(Guid id, string name)
    {
        await using var c = await db.Open();
        await c.ExecuteAsync("insert into account (id, name) values (@id, @name) on conflict (id) do nothing", new { id, name });
    }

    public async Task<bool> Rename(Guid id, string name)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("update account set name = @name where id = @id", new { id, name }) == 1;
    }

    public async Task<IReadOnlyList<string>> BlobKeys(Guid accountId)
    {
        await using var c = await db.Open();
        return (await c.QueryAsync<string>("select s3_key from blob where owner_id = @accountId", new { accountId })).ToList();
    }

    public async Task<IReadOnlyList<Guid>> HostedGameIds(Guid accountId)
    {
        await using var c = await db.Open();
        return (await c.QueryAsync<Guid>("select id from game where created_by = @accountId", new { accountId })).ToList();
    }

    public async Task<IReadOnlyList<Guid>> JoinedGameIds(Guid accountId)
    {
        await using var c = await db.Open();
        return (await c.QueryAsync<Guid>(@"select distinct p.game_id from participant p join game g on g.id = p.game_id
                                          where p.account_id = @accountId and g.created_by <> @accountId", new { accountId })).ToList();
    }

    public async Task Delete(Guid accountId)
    {
        await using var c = await db.Open();
        await c.ExecuteAsync("delete from account where id = @accountId", new { accountId });
    }
}
