using System.Net;
using System.Net.Http.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public class ArmyTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public ArmyTests(ManagementFactory f) => _f = f;
    const string Share = "https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py";

    /// <summary>An asset of the owner's with its bytes uploaded (or not), straight through the repository.</summary>
    async Task<Guid> Uploaded(Guid owner, AssetKind kind, bool uploaded = true)
    {
        var assets = _f.Services.GetRequiredService<IAssetRepository>();
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        var dto = await assets.Create(owner, new CreateAssetRequest("mesh", kind, hash, 100, "model/gltf-binary", 1, new Orientation(0, 0, 0), kind == AssetKind.Miniature ? Base.Circle(1) : null));
        if (uploaded) await assets.MarkUploaded(owner, hash);
        return dto.Id;
    }

    static async Task<string> Problem(HttpResponseMessage r, HttpStatusCode status)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode}: {body}");
        return System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("detail").GetString()!;
    }

    static string Export(string unitJson) => $$"""{"id":"t","name":"T","gameSystem":"gf","units":[{{unitJson}}]}""";
    static string ListId() => "t" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task An_import_outside_the_limits_or_malformed_is_422_with_its_reason_and_stores_nothing()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var big = ListId();
        FakeArmyForge.Lists[big] = Export("""{"selectionId":"a","name":"Squad","size":60,"quality":4,"defense":4,"combined":false,"joinToUnit":null}""");
        Assert.Equal("Unit 'Squad': size 60 is outside 1–50.", await Problem(await c.PostAsJsonAsync("/v1/armies/import", new ImportRequest(big), ContentJson.Options), HttpStatusCode.UnprocessableEntity));
        var dup = ListId();
        FakeArmyForge.Lists[dup] = """{"id":"t","name":"T","gameSystem":"gf","units":[{"selectionId":"a","name":"S","size":1,"quality":4,"defense":4},{"selectionId":"a","name":"S","size":1,"quality":4,"defense":4}]}""";
        Assert.Equal("The list names entry 'a' twice.", await Problem(await c.PostAsJsonAsync("/v1/armies/import", new ImportRequest(dup), ContentJson.Options), HttpStatusCode.UnprocessableEntity));
        var garbled = ListId();
        FakeArmyForge.Lists[garbled] = """{"id":"t","name":"T","gameSystem":"gf","units":[{"selectionId":"a","size":"many"}]}""";
        Assert.Equal(ArmyEndpoints.MalformedExport, await Problem(await c.PostAsJsonAsync("/v1/armies/import", new ImportRequest(garbled), ContentJson.Options), HttpStatusCode.UnprocessableEntity));
        var notJson = ListId();
        FakeArmyForge.Lists[notJson] = "<html>";
        Assert.Equal(ArmyEndpoints.MalformedExport, await Problem(await c.PostAsJsonAsync("/v1/armies/import", new ImportRequest(notJson), ContentJson.Options), HttpStatusCode.UnprocessableEntity));
        Assert.Equal("source is at most 512 characters.", await Problem(await c.PostAsJsonAsync("/v1/armies/import", new ImportRequest("c2-6YsWRH3py?" + new string('x', 600)), ContentJson.Options), HttpStatusCode.UnprocessableEntity));
        Assert.Empty((await c.GetJson<Page<ArmySummary>>("/v1/armies")).Items);
    }

    [Fact]
    public async Task Army_forge_failures_are_502_with_fixed_text()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        Assert.Equal(ArmyEndpoints.NoSuchList, await Problem(await c.PostAsJsonAsync("/v1/armies/import", new ImportRequest("missing1"), ContentJson.Options), HttpStatusCode.BadGateway));
        Assert.Equal(ArmyEndpoints.Unreachable, await Problem(await c.PostAsJsonAsync("/v1/armies/import", new ImportRequest(FakeArmyForge.Unreachable), ContentJson.Options), HttpStatusCode.BadGateway));
    }

    [Fact]
    public async Task An_account_imports_at_most_the_per_account_limit()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var armies = _f.Services.GetRequiredService<IArmyRepository>();
        var one = await c.PostJson<Army>("/v1/armies/import", new ImportRequest(Share));
        for (var i = 1; i < Limits.PerAccount; i++) await armies.Insert(owner, one with { Id = Guid.CreateVersion7() });
        Assert.Equal($"An account keeps at most {Limits.PerAccount} armies.", await Problem(await c.PostAsJsonAsync("/v1/armies/import", new ImportRequest(Share), ContentJson.Options), HttpStatusCode.UnprocessableEntity));
        Assert.Equal(Limits.PerAccount, await armies.Count(owner));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/armies/{one.Id}")).StatusCode);
        await c.PostJson<Army>("/v1/armies/import", new ImportRequest(Share));
    }

    [Fact]
    public async Task A_patch_outside_the_limits_is_422_and_changes_nothing()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var army = await c.PostJson<Army>("/v1/armies/import", new ImportRequest(Share));
        var slot = army.Units[0].Models[0];
        var url = $"/v1/armies/{army.Id}";
        Assert.Equal("name is at most 64 characters.", await Problem(await c.PatchJson(url, new PatchArmyRequest(new string('n', 65), null, null)), HttpStatusCode.UnprocessableEntity));
        Assert.Equal("name contains a control or bidirectional formatting character.", await Problem(await c.PatchJson(url, new PatchArmyRequest("a\u202eb", null, null)), HttpStatusCode.UnprocessableEntity));
        Assert.Equal("name is required.", await Problem(await c.PatchJson(url, new PatchArmyRequest(" ", null, null)), HttpStatusCode.UnprocessableEntity));
        Assert.Equal($"Slot {slot.Id}: base 13x1 is outside 0.4–12 inches.", await Problem(await c.PatchJson(url, new PatchArmyRequest(null, null, [new SlotPatch(slot.Id, null, false, Base.Oval(13, 1), null)])), HttpStatusCode.UnprocessableEntity));
        Assert.Equal($"Slot {slot.Id}: base 1x0.3 is outside 0.4–12 inches.", await Problem(await c.PatchJson(url, new PatchArmyRequest(null, null, [new SlotPatch(slot.Id, null, false, Base.Oval(1, 0.3), null)])), HttpStatusCode.UnprocessableEntity));
        Assert.Equal($"Slot {slot.Id}: height 25 is outside 0.1–24 inches.", await Problem(await c.PatchJson(url, new PatchArmyRequest(null, null, [new SlotPatch(slot.Id, null, false, null, 25)])), HttpStatusCode.UnprocessableEntity));
        Assert.Equal($"Slot {slot.Id}: height 0 is outside 0.1–24 inches.", await Problem(await c.PatchJson(url, new PatchArmyRequest(null, null, [new SlotPatch(slot.Id, null, false, null, 0)])), HttpStatusCode.UnprocessableEntity));
        var shape = await c.PatchAsJsonAsync(url, new { slots = new[] { new { slotId = slot.Id, clearAsset = false, @base = new { shape = 7, a = 1, b = 1 } } } });
        Assert.Equal(HttpStatusCode.BadRequest, shape.StatusCode);                                   // enums bind by name only
        Assert.Equal(ContentJson.Serialize(army), ContentJson.Serialize(await c.GetJson<Army>(url)));
        var edge = await c.PatchJson<Army>(url, new PatchArmyRequest(new string('n', 64), null, [new SlotPatch(slot.Id, null, false, Base.Rect(0.4, 12), 24)]));
        Assert.Equal((Base.Rect(0.4, 12), 24.0), (edge.Units[0].Models[0].Base, edge.Units[0].Models[0].Height));
    }

    [Fact]
    public async Task A_patch_binds_only_uploaded_miniatures_of_the_callers_own()
    {
        var (me, them) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var c = await _f.AuthenticatedClient(me);
        await _f.AuthenticatedClient(them);
        var army = await c.PostJson<Army>("/v1/armies/import", new ImportRequest(Share));
        var unit = army.Units[0]; var slot = unit.Models[0];
        var url = $"/v1/armies/{army.Id}";
        var theirs = await Uploaded(them, AssetKind.Miniature);
        var texture = await Uploaded(me, AssetKind.Texture);
        var pending = await Uploaded(me, AssetKind.Miniature, uploaded: false);
        var invented = Guid.CreateVersion7();
        foreach (var bad in new[] { theirs, pending, invented })
        {
            Assert.Equal($"Asset {bad} is not an uploaded asset of yours.", await Problem(await c.PatchJson(url, new PatchArmyRequest(null, [new UnitPatch(unit.Id, bad)], null)), HttpStatusCode.UnprocessableEntity));
            Assert.Equal($"Asset {bad} is not an uploaded asset of yours.", await Problem(await c.PatchJson(url, new PatchArmyRequest(null, null, [new SlotPatch(slot.Id, bad, false, null, null)])), HttpStatusCode.UnprocessableEntity));
        }
        Assert.StartsWith($"Asset {texture} is", await Problem(await c.PatchJson(url, new PatchArmyRequest(null, null, [new SlotPatch(slot.Id, texture, false, null, null)])), HttpStatusCode.UnprocessableEntity));
        Assert.Equal(ContentJson.Serialize(army), ContentJson.Serialize(await c.GetJson<Army>(url)));
        var mine = await Uploaded(me, AssetKind.Miniature);
        Assert.All((await c.PatchJson<Army>(url, new PatchArmyRequest(null, [new UnitPatch(unit.Id, mine)], null))).Units[0].Models, m => Assert.Equal(mine, m.AssetId));
        var cleared = await c.PatchJson<Army>(url, new PatchArmyRequest(null, [new UnitPatch(unit.Id, null)], [new SlotPatch(slot.Id, invented, true, null, null)]));
        Assert.All(cleared.Units[0].Models, m => Assert.Null(m.AssetId));                     // clearing names no asset, and ClearAsset wins over an id
    }

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
        var me = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(me);
        var army = await c.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        var unit = army.Units.Single(u => u.Name == "Beast Riders");
        var mesh = await Uploaded(me, AssetKind.Miniature); var special = await Uploaded(me, AssetKind.Miniature);
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
    [Fact]
    public async Task Import_writes_the_chosen_base_set_and_defaults_to_round()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        static Base Riders(Army a) => a.Units.Single(u => u.Name == "Beast Riders").Models[0].Base;
        var square = await c.PostJson<Army>("/v1/armies/import", new ImportRequest(Share, BaseSet.Square));
        Assert.Equal(Base.Rect(50 / 25.4, 25 / 25.4), Riders(square));
        Assert.Equal(Base.Rect(50 / 25.4, 25 / 25.4), Riders(await c.GetJson<Army>($"/v1/armies/{square.Id}")));
        var plain = await c.PostAsJsonAsync("/v1/armies/import", new { source = Share });   // a caller that sends no base set
        var round = (await plain.Content.ReadFromJsonAsync<Army>(ContentJson.Options))!;
        Assert.Equal(Base.Oval(60 / 25.4, 35 / 25.4), Riders(round));
        var wire = await c.PostAsJsonAsync("/v1/armies/import", new { source = Share, bases = "square" });
        Assert.Equal(Base.Rect(50 / 25.4, 25 / 25.4), Riders((await wire.Content.ReadFromJsonAsync<Army>(ContentJson.Options))!));
    }
}
