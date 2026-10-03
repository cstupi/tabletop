using System.Net;
using System.Net.Http.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public class ArmyTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public ArmyTests(ManagementFactory f) => _f = f;
    const string Share = "https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py";

    [Fact]
    public async Task Import_stores_the_whole_tree_and_reads_it_back_identically()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var imported = await c.PostJson<Army>("/v1/armies/import", new ImportRequest(Share));
        Assert.Equal(5, imported.Units.Count); Assert.Equal(Share, imported.Source);
        var fetched = await c.GetJson<Army>($"/v1/armies/{imported.Id}");
        Assert.All(fetched.Units.SelectMany(u => u.Models), m => Assert.Equal("prsajf5ot936qznk", m.ArmyBook));
        Assert.Equal(ContentJson.Serialize(imported), ContentJson.Serialize(fetched));
        var page = await c.GetJson<Page<ArmySummary>>("/v1/armies");
        Assert.Equal([(imported.Id, 5, 33)], page.Items.Select(a => (a.Id, a.Units, a.Models)));
    }

    [Fact]
    public async Task Patch_binds_a_unit_default_then_overrides_one_slot_and_its_base()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var army = await c.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        var unit = army.Units.Single(u => u.Name == "Beast Riders");
        var mesh = Guid.CreateVersion7(); var special = Guid.CreateVersion7();
        var patched = await c.PatchJson<Army>($"/v1/armies/{army.Id}", new PatchArmyRequest("Daemons", [new UnitPatch(unit.Id, mesh)],
            [new SlotPatch(unit.Models[0].Id, special, false, Base.Circle(1.57), 2.0)]));
        var u = patched.Units.Single(x => x.Id == unit.Id);
        Assert.Equal("Daemons", patched.Name);
        Assert.Equal(special, u.Models[0].AssetId); Assert.Equal(Base.Circle(1.57), u.Models[0].Base); Assert.Equal(2.0, u.Models[0].Height);
        Assert.All(u.Models.Skip(1), m => Assert.Equal(mesh, m.AssetId));
        var cleared = await c.PatchJson<Army>($"/v1/armies/{army.Id}", new PatchArmyRequest(null, null, [new SlotPatch(unit.Models[0].Id, null, true, null, null)]));
        Assert.Null(cleared.Units.Single(x => x.Id == unit.Id).Models[0].AssetId);
    }

    [Fact]
    public async Task Owner_only_and_import_errors()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var army = await mine.PostJson<Army>("/v1/armies/import", new ImportRequest(Share));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/v1/armies/{army.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.DeleteAsync($"/v1/armies/{army.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await mine.PostAsJsonAsync("/v1/armies/import", new ImportRequest("https://army-forge.onepagerules.com/share?id=missing1"), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await mine.PostAsJsonAsync("/v1/armies/import", new ImportRequest("not a link"), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mine.DeleteAsync($"/v1/armies/{army.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mine.GetAsync($"/v1/armies/{army.Id}")).StatusCode);
    }
}
