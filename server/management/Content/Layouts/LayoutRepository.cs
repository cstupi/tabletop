using Dapper;
using Npgsql;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Layouts;

public sealed record LayoutSummary(Guid Id, string Name, TableSize Size, int Terrain, int Markers);
public sealed record PatchLayoutRequest(string? Name, TableState? State);

public sealed class LayoutRepository(Database db) : ILayoutRepository
{
    private sealed record Head(Guid Id, string? Name, double Width, double Length, string? TextureBuiltin, Guid? TextureAssetId);
    private sealed record PlacementRow(Guid Id, string? Builtin, Guid? AssetId, double PosX, double PosY, double PosZ, double Yaw, double Scale);
    private sealed record MarkerRow(Guid Id, string Kind, double PosX, double PosY, double PosZ, double Yaw, double Diameter, string? Label, string? VisualBuiltin, Guid? VisualAssetId);
    private sealed record ZoneRow(Guid Id, string Shape, int TeamOrdinal);

    public async Task<TableState> Save(Guid ownerId, TableState state)
    {
        var id = Guid.CreateVersion7();
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("insert into table_state (id, owner_id, name, width, length, texture_builtin, texture_asset_id) values (@id, @ownerId, @name, @w, @l, @tb, @ta)",
            new { id, ownerId, name = state.Name, w = state.Size.Width, l = state.Size.Length, tb = state.Texture.Builtin, ta = state.Texture.AssetId }, tx);
        await WriteChildren(c, tx, id, state);
        await tx.CommitAsync();
        return (await Get(ownerId, id))!;
    }

    /// <summary>Copies the children with fresh ids. Shared with Patch; the game server writes the game's own tree, never this code.</summary>
    private static async Task WriteChildren(NpgsqlConnection c, NpgsqlTransaction tx, Guid tableStateId, TableState s)
    {
        foreach (var p in s.Terrain)
        {
            var pid = Guid.CreateVersion7();
            await c.ExecuteAsync("insert into terrain_placement (id, table_state_id, builtin, asset_id, pos_x, pos_y, pos_z, yaw, scale) values (@pid, @tableStateId, @b, @a, @x, @y, @z, @yaw, @scale)",
                new { pid, tableStateId, b = p.Mesh.Builtin, a = p.Mesh.AssetId, x = p.Position.X, y = p.Position.Y, z = p.Position.Z, yaw = p.Yaw.Degrees, scale = p.Scale }, tx);
            foreach (var v in p.Volumes)
            {
                var r = VolumeRow.From(Guid.CreateVersion7(), v.Volume);
                await c.ExecuteAsync($"insert into terrain_volume (terrain_placement_id, {VolumeRow.InsertColumns}) values (@Pid, {VolumeRow.InsertValues})",
                    new { Pid = pid, r.Id, r.Kind, r.Footprint, r.DimA, r.DimB, r.OffsetX, r.OffsetY, r.Yaw, r.Bottom, r.Top, r.Impassable, r.BlocksLos, r.SeeInOut, r.Cover, r.Difficult, r.Dangerous, r.DangerousRushCharge, r.Steep, r.Notes }, tx);
            }
        }
        foreach (var m in s.Markers)
            await c.ExecuteAsync("insert into marker (id, table_state_id, kind, pos_x, pos_y, pos_z, yaw, diameter, label, visual_builtin, visual_asset_id) values (@id, @tableStateId, @kind, @x, @y, @z, @yaw, @d, @label, @vb, @va)",
                new { id = Guid.CreateVersion7(), tableStateId, kind = m.Kind.ToString().ToLowerInvariant(), x = m.Position.X, y = m.Position.Y, z = m.Position.Z, yaw = m.Yaw.Degrees, d = m.Diameter, label = m.Label, vb = m.Visual.Builtin, va = m.Visual.AssetId }, tx);
        foreach (var z in s.Zones)
            await c.ExecuteAsync("insert into deployment_zone (id, table_state_id, shape, team_ordinal) values (@id, @tableStateId, @shape, @ord)", new { id = Guid.CreateVersion7(), tableStateId, shape = z.Shape, ord = z.TeamOrdinal }, tx);
    }

    public static async Task<TableState> ReadTree(NpgsqlConnection c, Guid id, string? name, TableSize size, VisualRef texture)
    {
        var placements = (await c.QueryAsync<PlacementRow>("select id as Id, builtin as Builtin, asset_id as AssetId, pos_x::float8 as PosX, pos_y::float8 as PosY, pos_z::float8 as PosZ, yaw::float8 as Yaw, scale::float8 as Scale from terrain_placement where table_state_id = @id order by id", new { id })).ToList();
        var volumes = new Dictionary<Guid, List<TerrainVolume>>();
        foreach (var p in placements)
            volumes[p.Id] = (await c.QueryAsync<VolumeRow>($"select {VolumeRow.Columns} from terrain_volume where terrain_placement_id = @pid order by id", new { pid = p.Id })).Select(v => new TerrainVolume(v.Id, v.ToVolume())).ToList();
        var markers = await c.QueryAsync<MarkerRow>("select id as Id, kind as Kind, pos_x::float8 as PosX, pos_y::float8 as PosY, pos_z::float8 as PosZ, yaw::float8 as Yaw, diameter::float8 as Diameter, label as Label, visual_builtin as VisualBuiltin, visual_asset_id as VisualAssetId from marker where table_state_id = @id order by id", new { id });
        var zones = await c.QueryAsync<ZoneRow>("select id as Id, shape as Shape, team_ordinal as TeamOrdinal from deployment_zone where table_state_id = @id order by team_ordinal", new { id });
        return new TableState(id, name, size, texture,
            placements.Select(p => new TerrainPlacement(p.Id, new VisualRef(p.Builtin, p.AssetId), new Point3(p.PosX, p.PosY, p.PosZ), new Yaw(p.Yaw), p.Scale, volumes[p.Id])).ToList(),
            markers.Select(m => new Marker(m.Id, Enum.Parse<MarkerKind>(m.Kind, true), new Point3(m.PosX, m.PosY, m.PosZ), new Yaw(m.Yaw), m.Diameter, m.Label, new VisualRef(m.VisualBuiltin, m.VisualAssetId))).ToList(),
            zones.Select(z => new DeploymentZone(z.Id, z.Shape, z.TeamOrdinal)).ToList());
    }

    public async Task<TableState?> Get(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        var h = await c.QuerySingleOrDefaultAsync<Head>("select id as Id, name as Name, width::float8 as Width, length::float8 as Length, texture_builtin as TextureBuiltin, texture_asset_id as TextureAssetId from table_state where id = @id and owner_id = @ownerId", new { id, ownerId });
        return h is null ? null : await ReadTree(c, h.Id, h.Name, new TableSize(h.Width, h.Length), new VisualRef(h.TextureBuiltin, h.TextureAssetId));
    }

    public async Task<IReadOnlyList<LayoutSummary>> List(Guid ownerId)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<(Guid Id, string Name, double Width, double Length, int Terrain, int Markers)>(@"select t.id, t.name, t.width::float8, t.length::float8,
            (select count(*) from terrain_placement p where p.table_state_id = t.id)::int, (select count(*) from marker m where m.table_state_id = t.id)::int
            from table_state t where t.owner_id = @ownerId order by t.id", new { ownerId });
        return rows.Select(r => new LayoutSummary(r.Id, r.Name, new TableSize(r.Width, r.Length), r.Terrain, r.Markers)).ToList();
    }

    public async Task<TableState?> Patch(Guid ownerId, Guid id, PatchLayoutRequest p)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        var n = p.State is null
            ? await c.ExecuteAsync("update table_state set name = coalesce(@name, name) where id = @id and owner_id = @ownerId", new { id, ownerId, name = p.Name }, tx)
            : await c.ExecuteAsync("update table_state set name = coalesce(@name, name), width = @w, length = @l, texture_builtin = @tb, texture_asset_id = @ta where id = @id and owner_id = @ownerId",
                new { id, ownerId, name = p.Name, w = p.State.Size.Width, l = p.State.Size.Length, tb = p.State.Texture.Builtin, ta = p.State.Texture.AssetId }, tx);
        if (n == 0) return null;
        if (p.State is not null)
        {
            await c.ExecuteAsync("delete from terrain_placement where table_state_id = @id; delete from marker where table_state_id = @id; delete from deployment_zone where table_state_id = @id", new { id }, tx);
            await WriteChildren(c, tx, id, p.State);
        }
        await tx.CommitAsync();
        return await Get(ownerId, id);
    }

    public async Task<bool> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from table_state where id = @id and owner_id = @ownerId", new { id, ownerId }) == 1;
    }
}
