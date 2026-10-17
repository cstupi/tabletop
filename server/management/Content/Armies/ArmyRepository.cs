using Dapper;
using Npgsql;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Armies;

public sealed record ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models);
public sealed record ImportRequest(string Source, BaseSet Bases = BaseSet.Round);
public sealed record UnitPatch(Guid UnitId, Guid? AssetId);
public sealed record SlotPatch(Guid SlotId, Guid? AssetId, bool ClearAsset, Base? Base, double? Height);
public sealed record PatchArmyRequest(string? Name, IReadOnlyList<UnitPatch>? Units, IReadOnlyList<SlotPatch>? Slots);

/// <summary>The army tree: army → unit → model_slot → weapon, each level with its stat and property rows.</summary>
public sealed class ArmyRepository(Database db) : IArmyRepository
{
    private sealed record UnitRow(Guid Id, int Ordinal, string Name, string? ReserveProperty, Guid? ReserveOf);
    private sealed record SlotRow(Guid Id, Guid UnitId, int Ordinal, string Name, string BaseShape, double BaseA, double BaseB, double Height, Guid? AssetId, string? ArmyBook);
    private sealed record WeaponRow(Guid Id, Guid SlotId, string Name);
    private sealed record KV(Guid Owner, string Key, string Value);
    private sealed record Prop(Guid Owner, string Name, int? Parameter);

    public async Task Insert(Guid ownerId, Army army)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("insert into army (id, owner_id, name, game_system, source) values (@Id, @ownerId, @Name, @GameSystem, @Source)", new { army.Id, ownerId, army.Name, army.GameSystem, army.Source }, tx);
        foreach (var u in army.Units)
        {
            await c.ExecuteAsync("insert into unit (id, army_id, ordinal, name, reserve_property, reserve_of) values (@Id, @armyId, @Ordinal, @Name, @reserveProperty, @reserveOf)",
                new { u.Id, armyId = army.Id, u.Ordinal, u.Name, reserveProperty = u.Reserve?.Property, reserveOf = u.Reserve?.Of }, tx);
            await c.ExecuteAsync("insert into unit_stat (unit_id, stat, value) values (@id, @k, @v)", u.Stats.Select(s => new { id = u.Id, k = s.Key, v = s.Value }), tx);
            await c.ExecuteAsync("insert into unit_property (unit_id, name, parameter) values (@id, @Name, @Parameter)", u.Properties.Select(p => new { id = u.Id, p.Name, p.Parameter }), tx);
            foreach (var m in u.Models)
            {
                await c.ExecuteAsync("insert into model_slot (id, unit_id, ordinal, name, base_shape, base_a, base_b, height, asset_id, army_book) values (@Id, @unitId, @Ordinal, @Name, @shape, @a, @b, @Height, @AssetId, @ArmyBook)",
                    new { m.Id, unitId = u.Id, m.Ordinal, m.Name, shape = m.Base.Shape.ToString().ToLowerInvariant(), a = m.Base.A, b = m.Base.B, m.Height, m.AssetId, m.ArmyBook }, tx);
                await c.ExecuteAsync("insert into slot_stat (slot_id, stat, value) values (@id, @k, @v)", m.Stats.Select(s => new { id = m.Id, k = s.Key, v = s.Value }), tx);
                await c.ExecuteAsync("insert into slot_property (slot_id, name, parameter) values (@id, @Name, @Parameter)", m.Properties.Select(p => new { id = m.Id, p.Name, p.Parameter }), tx);
                foreach (var w in m.Weapons)
                {
                    await c.ExecuteAsync("insert into weapon (id, slot_id, name) values (@Id, @slotId, @Name)", new { w.Id, slotId = m.Id, w.Name }, tx);
                    await c.ExecuteAsync("insert into weapon_stat (weapon_id, stat, value) values (@id, @k, @v)", w.Stats.Select(s => new { id = w.Id, k = s.Key, v = s.Value }), tx);
                    await c.ExecuteAsync("insert into weapon_property (weapon_id, name, parameter) values (@id, @Name, @Parameter)", w.Properties.Select(p => new { id = w.Id, p.Name, p.Parameter }), tx);
                }
            }
        }
        await tx.CommitAsync();
    }

    public async Task<Army?> Get(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        var head = await c.QuerySingleOrDefaultAsync<(string Name, string GameSystem, string Source)>("select name, game_system, source from army where id = @id and owner_id = @ownerId", new { id, ownerId });
        if (head == default) return null;
        var units = (await c.QueryAsync<UnitRow>("select id as Id, ordinal as Ordinal, name as Name, reserve_property as ReserveProperty, reserve_of as ReserveOf from unit where army_id = @id order by ordinal", new { id })).ToList();
        var slots = (await c.QueryAsync<SlotRow>(@"select s.id as Id, s.unit_id as UnitId, s.ordinal as Ordinal, s.name as Name, s.base_shape as BaseShape, s.base_a::float8 as BaseA, s.base_b::float8 as BaseB, s.height::float8 as Height, s.asset_id as AssetId, s.army_book as ArmyBook
            from model_slot s join unit u on u.id = s.unit_id where u.army_id = @id order by s.ordinal", new { id })).ToLookup(s => s.UnitId);
        // Stat, property and weapon rows are written once at import and never updated (Patch touches only model_slot
        // columns), so `ctid` — physical row order — is stable and equals insertion order for as long as the army
        // exists. It stands in for an ordinal column these tables don't have; if a future patch ever writes these
        // tables in place, an explicit ordinal must replace it.
        var weapons = (await c.QueryAsync<WeaponRow>("select w.id as Id, w.slot_id as SlotId, w.name as Name from weapon w join model_slot s on s.id = w.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by w.ctid", new { id })).ToLookup(w => w.SlotId);
        var unitStats = (await c.QueryAsync<KV>("select us.unit_id as Owner, us.stat as Key, us.value as Value from unit_stat us join unit u on u.id = us.unit_id where u.army_id = @id order by us.ctid", new { id })).ToLookup(x => x.Owner);
        var unitProps = (await c.QueryAsync<Prop>("select up.unit_id as Owner, up.name as Name, up.parameter as Parameter from unit_property up join unit u on u.id = up.unit_id where u.army_id = @id order by up.ctid", new { id })).ToLookup(x => x.Owner);
        var slotStats = (await c.QueryAsync<KV>("select ss.slot_id as Owner, ss.stat as Key, ss.value as Value from slot_stat ss join model_slot s on s.id = ss.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by ss.ctid", new { id })).ToLookup(x => x.Owner);
        var slotProps = (await c.QueryAsync<Prop>("select sp.slot_id as Owner, sp.name as Name, sp.parameter as Parameter from slot_property sp join model_slot s on s.id = sp.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by sp.ctid", new { id })).ToLookup(x => x.Owner);
        var weaponStats = (await c.QueryAsync<KV>("select ws.weapon_id as Owner, ws.stat as Key, ws.value as Value from weapon_stat ws join weapon w on w.id = ws.weapon_id join model_slot s on s.id = w.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by ws.ctid", new { id })).ToLookup(x => x.Owner);
        var weaponProps = (await c.QueryAsync<Prop>("select wp.weapon_id as Owner, wp.name as Name, wp.parameter as Parameter from weapon_property wp join weapon w on w.id = wp.weapon_id join model_slot s on s.id = w.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by wp.ctid", new { id })).ToLookup(x => x.Owner);

        IReadOnlyDictionary<string, string> Stats(ILookup<Guid, KV> l, Guid owner) => l[owner].ToDictionary(x => x.Key, x => x.Value);
        IReadOnlyList<PropertyInstance> Props(ILookup<Guid, Prop> l, Guid owner) => l[owner].Select(p => new PropertyInstance(p.Name, p.Parameter)).ToList();

        return new Army(id, head.Name, head.GameSystem, head.Source, units.Select(u => new Unit(u.Id, u.Ordinal, u.Name, Stats(unitStats, u.Id), Props(unitProps, u.Id),
            slots[u.Id].Select(s => new ModelSlot(s.Id, s.Ordinal, s.Name, Stats(slotStats, s.Id), Props(slotProps, s.Id),
                new Base(Enum.Parse<BaseShape>(s.BaseShape, true), s.BaseA, s.BaseB), s.Height, s.AssetId,
                weapons[s.Id].Select(w => new Weapon(w.Id, w.Name, Stats(weaponStats, w.Id), Props(weaponProps, w.Id))).ToList(), s.ArmyBook)).ToList(),
            u.ReserveProperty is { } rule && u.ReserveOf is { } of ? new UnitReserve(rule, of) : null)).ToList());
    }

    public async Task<IReadOnlyList<ArmySummary>> List(Guid ownerId, Http.Keyset page)
    {
        await using var c = await db.Open();
        return (await c.QueryAsync<ArmySummary>(@"select a.id as Id, a.name as Name, a.game_system as GameSystem, a.source as Source,
            (select count(*) from unit u where u.army_id = a.id)::int as Units,
            (select count(*) from model_slot s join unit u on u.id = s.unit_id where u.army_id = a.id)::int as Models
            from army a where a.owner_id = @ownerId and (@after::uuid is null or a.id > @after::uuid) order by a.id limit @limit", new { ownerId, after = page.After, limit = page.Limit })).ToList();
    }

    public async Task<int> Count(Guid ownerId)
    {
        await using var c = await db.Open();
        return await c.ExecuteScalarAsync<int>("select count(*)::int from army where owner_id = @ownerId", new { ownerId });
    }

    public async Task<Army?> Patch(Guid ownerId, Guid id, PatchArmyRequest p)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        if (await c.ExecuteAsync("update army set name = coalesce(@name, name) where id = @id and owner_id = @ownerId", new { id, ownerId, name = p.Name }, tx) == 0) return null;
        foreach (var u in p.Units ?? [])
            await c.ExecuteAsync("update model_slot set asset_id = @asset where unit_id = @unitId and unit_id in (select id from unit where army_id = @id)", new { id, unitId = u.UnitId, asset = u.AssetId }, tx);
        foreach (var s in p.Slots ?? [])
            await c.ExecuteAsync(@"update model_slot set asset_id = case when @clear then null else coalesce(@asset, asset_id) end,
                base_shape = coalesce(@shape, base_shape), base_a = coalesce(@a, base_a), base_b = coalesce(@b, base_b), height = coalesce(@height, height)
                where id = @slotId and unit_id in (select id from unit where army_id = @id)",
                new { id, slotId = s.SlotId, clear = s.ClearAsset, asset = s.AssetId, shape = s.Base?.Shape.ToString().ToLowerInvariant(), a = s.Base?.A, b = s.Base?.B, height = s.Height }, tx);
        await tx.CommitAsync();
        return await Get(ownerId, id);
    }

    public async Task<bool> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from army where id = @id and owner_id = @ownerId", new { id, ownerId }) == 1;
    }
}
