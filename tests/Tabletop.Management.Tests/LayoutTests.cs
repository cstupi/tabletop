using System.Net;
using System.Net.Http.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Layouts;

namespace Tabletop.Management.Tests;

public class LayoutTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public LayoutTests(ManagementFactory f) => _f = f;

    public static TableState Sample(string? name = "Ruins") => new(Guid.CreateVersion7(), name, new TableSize(72, 48), VisualRef.OfBuiltin("grass"),
        [new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill-small"), new Point3(12, 12, 0), new Yaw(45), 1.0,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 2, TerrainDeclarations.Cover | TerrainDeclarations.Difficult, "hill"))]),
         new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfAsset(Guid.CreateVersion7()), new Point3(40, 20, 0), new Yaw(0), 1.5, [])],
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
}
