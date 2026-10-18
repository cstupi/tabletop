using Tabletop.Management.Identity;

namespace Tabletop.Management.Tests;

public sealed class InMemoryAccountRepository(InMemoryRows rows) : IAccountRepository
{
    public Task<AccountRow?> Get(Guid id)
    {
        lock (rows.Gate) return Task.FromResult(rows.Accounts.TryGetValue(id, out var a) ? new AccountRow(id, a.Name) : null);
    }

    public Task Insert(Guid id, string name)
    {
        lock (rows.Gate) rows.Accounts.TryAdd(id, new InMemoryRows.AccountRow { Name = name });
        return Task.CompletedTask;
    }

    public Task<bool> Rename(Guid id, string name)
    {
        lock (rows.Gate)
        {
            if (!rows.Accounts.TryGetValue(id, out var a)) return Task.FromResult(false);
            a.Name = name;
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<string>> BlobKeys(Guid accountId)
    {
        lock (rows.Gate) return Task.FromResult<IReadOnlyList<string>>(rows.Blobs.Where(b => b.Key.Owner == accountId).Select(b => b.Value.Key).ToList());
    }

    public Task<IReadOnlyList<Guid>> HostedGameIds(Guid accountId)
    {
        lock (rows.Gate) return Task.FromResult<IReadOnlyList<Guid>>(rows.Games.Where(g => g.Value.CreatedBy == accountId).Select(g => g.Key).ToList());
    }

    public Task<IReadOnlyList<Guid>> JoinedGameIds(Guid accountId)
    {
        lock (rows.Gate)
            return Task.FromResult<IReadOnlyList<Guid>>(rows.Participants
                .Where(p => p.AccountId == accountId && rows.Games.TryGetValue(p.GameId, out var g) && g.CreatedBy != accountId).Select(p => p.GameId).Distinct().ToList());
    }

    public Task Delete(Guid accountId)
    {
        lock (rows.Gate) if (rows.Accounts.ContainsKey(accountId)) rows.RemoveAccount(accountId);
        return Task.CompletedTask;
    }
}
