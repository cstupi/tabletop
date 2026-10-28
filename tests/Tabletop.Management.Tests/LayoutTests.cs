using System.Net;
using System.Net.Http.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Layouts;

namespace Tabletop.Management.Tests;

public class LayoutTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public LayoutTests(ManagementFactory f) => _f = f;

    public static TableState Sample(string? name = "Ruins", VisualRef? terrainMesh = null) => new(Guid.CreateVersion7(), name, new TableSize(72, 48), VisualRef.OfBuiltin("grass"),
        [new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill-small"), new Point3(12, 12, 0), new Yaw(45), 1.0,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 2, TerrainDeclarations.Cover | TerrainDeclarations.Difficult, "hill"))]),
         new TerrainPlacement(Guid.CreateVersion7(), terrainMesh ?? VisualRef.OfBuiltin("ruin-wall"), new Point3(40, 20, 0), new Yaw(0), 1.5, [])],
        [new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(36, 24, 0), new Yaw(0), 3, "centre", VisualRef.OfBuiltin("objective-ring"))],
        [new DeploymentZone(Guid.CreateVersion7(), "frontline/1", 1), new DeploymentZone(Guid.CreateVersion7(), "frontline/2", 2)]);

    static string Shape(TableState t) => ContentJson.Serialize(t with { Id = Guid.Empty,
        Terrain = t.Terrain.Select(p => p with { Id = Guid.Empty, Volumes = p.Volumes.Select(v => v with { Id = Guid.Empty }).ToList() }).ToList(),
        Markers = t.Markers.Select(m => m with { Id = Guid.Empty }).ToList(), Zones = t.Zones.Select(z => z with { Id = Guid.Empty }).ToList() });

    [Fact]
    public async Task Save_copies_with_new_ids_and_reads_back_the_same_shape()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var input = Sample();
        var saved = await c.PostJson<TableState>("/v1/layouts", input);
        Assert.NotEqual(input.Id, saved.Id);
        Assert.Empty(saved.Terrain.Select(p => p.Id).Intersect(input.Terrain.Select(p => p.Id)));
        Assert.Equal(Shape(input), Shape(saved));
        Assert.Equal(ContentJson.Serialize(saved), ContentJson.Serialize(await c.GetJson<TableState>($"/v1/layouts/{saved.Id}")));
        var list = await c.GetJson<List<LayoutSummary>>("/v1/layouts");
        Assert.Equal([new LayoutSummary(saved.Id, "Ruins", new TableSize(72, 48), 2, 1)], list);
    }

    [Fact]
    public async Task Patch_renames_or_replaces_and_everything_is_owner_only()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var saved = await mine.PostJson<TableState>("/v1/layouts", Sample());
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/v1/layouts/{saved.Id}")).StatusCode);
        var renamed = await mine.PatchJson<TableState>($"/v1/layouts/{saved.Id}", new PatchLayoutRequest("Ruins v2", null));
        Assert.Equal("Ruins v2", renamed.Name); Assert.Equal(2, renamed.Terrain.Count);
        var replaced = await mine.PatchJson<TableState>($"/v1/layouts/{saved.Id}", new PatchLayoutRequest(null, Sample() with { Size = new TableSize(44, 30), Terrain = [] }));
        Assert.Equal(("Ruins v2", 44.0, 0), (replaced.Name, replaced.Size.Width, replaced.Terrain.Count));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await mine.PostAsJsonAsync("/v1/layouts", Sample(name: null), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mine.DeleteAsync($"/v1/layouts/{saved.Id}")).StatusCode);
        Assert.Empty(await mine.GetJson<List<LayoutSummary>>("/v1/layouts"));
    }

    [Fact]
    public async Task Missing_texture_is_rejected_before_any_row_is_written()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        using var doc = System.Text.Json.JsonDocument.Parse(ContentJson.Serialize(Sample()));
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("texture")) continue;
                prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var response = await c.PostAsync("/v1/layouts", content);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(await c.GetJson<List<LayoutSummary>>("/v1/layouts"));
    }

    async Task<Guid> Asset(Guid owner, AssetKind kind, bool uploaded = true)
    {
        var repo = _f.Services.GetRequiredService<IAssetRepository>();
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        var a = await repo.Create(owner, new CreateAssetRequest("a", kind, hash, 100, kind == AssetKind.Texture ? "image/png" : "model/gltf-binary", 1, new Orientation(0, 0, 0), kind == AssetKind.Miniature ? Base.Circle(1) : null));
        if (uploaded) await repo.MarkUploaded(owner, hash);
        return a.Id;
    }

    [Fact]
    public async Task Asset_references_are_the_callers_uploaded_assets_of_a_fitting_kind()
    {
        var (me, stranger) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var c = await _f.AuthenticatedClient(me);
        await _f.AuthenticatedClient(stranger);
        var (terrain, texture, mini, pending, theirs) = (await Asset(me, AssetKind.Terrain), await Asset(me, AssetKind.Texture), await Asset(me, AssetKind.Miniature),
            await Asset(me, AssetKind.Terrain, uploaded: false), await Asset(stranger, AssetKind.Terrain));
        TableState With(Guid terrainMesh, Guid tex, Guid marker) => Sample(terrainMesh: VisualRef.OfAsset(terrainMesh)) with
        {
            Texture = VisualRef.OfAsset(tex),
            Markers = [new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(1, 1, 0), new Yaw(0), 1, null, VisualRef.OfAsset(marker))],
        };
        var saved = await c.PostJson<TableState>("/v1/layouts", With(terrain, texture, mini));
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/v1/layouts", With(terrain, texture, terrain), ContentJson.Options)).StatusCode);   // a marker takes either mesh kind
        foreach (var bad in new[] { With(theirs, texture, mini), With(pending, texture, mini), With(Guid.CreateVersion7(), texture, mini), With(texture, texture, mini),
                                    With(terrain, terrain, mini), With(terrain, texture, texture) })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/layouts", bad, ContentJson.Options)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchAsJsonAsync($"/v1/layouts/{saved.Id}", new PatchLayoutRequest(null, bad), ContentJson.Options)).StatusCode);
        }
    }

    [Fact]
    public async Task Names_sizes_and_counts_are_bounded()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var saved = await c.PostJson<TableState>("/v1/layouts", Sample());
        var placement = Sample().Terrain[0];
        var marker = Sample().Markers[0];
        var volume = placement.Volumes[0];
        TableState[] bad =
        [
            Sample(name: new string('n', 65)), Sample(name: "evil\u202Eamp"), Sample(name: "tab\there"),
            Sample() with { Size = new TableSize(145, 48) }, Sample() with { Size = new TableSize(0, 48) }, Sample() with { Size = new TableSize(72, -1) },
            Sample() with { Terrain = Enumerable.Range(0, 65).Select(_ => placement with { Id = Guid.CreateVersion7() }).ToList() },
            Sample() with { Markers = Enumerable.Range(0, 33).Select(_ => marker with { Id = Guid.CreateVersion7() }).ToList() },
            Sample() with { Terrain = [placement with { Volumes = Enumerable.Range(0, 17).Select(_ => volume with { Id = Guid.CreateVersion7() }).ToList() }] },
            Sample() with { Terrain = [placement with { Volumes = [volume with { Volume = volume.Volume with { Notes = new string('x', 2001) } }] }] },
            Sample() with { Markers = [marker with { Label = new string('l', 65) }] },
            Sample() with { Texture = VisualRef.OfBuiltin(new string('b', 65)) },
            Sample() with { Zones = [new DeploymentZone(Guid.CreateVersion7(), new string('z', 65), 1)] },
        ];
        foreach (var state in bad)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/layouts", state, ContentJson.Options)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchAsJsonAsync($"/v1/layouts/{saved.Id}", new PatchLayoutRequest(null, state), ContentJson.Options)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchAsJsonAsync($"/v1/layouts/{saved.Id}", new PatchLayoutRequest(new string('n', 65), null), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/v1/layouts", Sample() with { Size = new TableSize(144, 144), Terrain = Enumerable.Range(0, 64).Select(_ => placement with { Id = Guid.CreateVersion7() }).ToList() }, ContentJson.Options)).StatusCode);
        Assert.Equal("Ruins", (await c.GetJson<TableState>($"/v1/layouts/{saved.Id}")).Name);
    }

    [Fact]
    public async Task An_account_holds_at_most_200_layouts()
    {
        var me = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(me);
        lock (_f.Rows.Gate)
            for (var i = 0; i < Tabletop.Management.Http.Limits.PerAccount; i++)
                _f.Rows.Layouts[_f.Rows.NextId()] = new InMemoryRows.LayoutRow { Owner = me, State = Sample() };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/layouts", Sample(), ContentJson.Options)).StatusCode);
    }
}
