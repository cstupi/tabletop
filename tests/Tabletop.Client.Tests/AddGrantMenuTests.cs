using System.Net;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>Add a grant (decisions and interfaces → commands: AddGrant): the unit menu offers the property set's grantable effects by entry name.</summary>
public class AddGrantMenuTests
{
    static readonly Guid HexA = Guid.CreateVersion7(), HexB = Guid.CreateVersion7(), Growth = Guid.CreateVersion7();
    static readonly IReadOnlyList<GrantableEffectDto> Grantable = [new("Growth", Growth, "marker"), new("Hex", HexA, "once"), new("Hex", HexB, "once")];

    [Fact]
    public void Each_effect_is_a_choice_named_by_its_entry_and_numbered_when_the_entry_has_several()
    {
        Assert.Equal(new[] { "Growth", "Hex (1 of 2)", "Hex (2 of 2)" }, GrantChoices.Of(Grantable).Select(c => c.Label));
    }

    [Fact]
    public void The_unit_menu_adds_a_grant_of_the_chosen_effect_to_the_unit_with_one_use()
    {
        var p = TableUx.After(TableUx.Deploy); var unit = TableUx.Squad.Id;
        var item = MenuModel.ForUnit(p, unit, [], GrantChoices.Of(Grantable)).Single(i => i.Label == "Add a grant");
        Assert.Equal(MenuInput.Choice, item.Input);
        Assert.Equal(new[] { "Growth", "Hex (1 of 2)", "Hex (2 of 2)" }, item.Choices);
        var add = Assert.IsType<AddGrant>(item.Build("Hex (2 of 2)"));
        Assert.Equal((unit, HexB, "Hex", unit, 1), (add.UnitId, add.Grant.EffectId, add.Grant.EntryName, add.Grant.CasterUnitId, add.Grant.RemainingUses));
        Assert.Null(item.Build("Nothing"));
        Assert.DoesNotContain(MenuModel.ForUnit(p, unit, []), i => i.Label == "Add a grant");   // no grantable effects loaded: no item
    }

    [Fact]
    public async Task The_session_loads_them_with_the_rule_texts()
    {
        var dir = Directory.CreateTempSubdirectory("tabletop-grant-tests").FullName;
        try
        {
            var http = new FakeManagement(); var sockets = new FakeSocketFactory();
            var s = new Session(new ManagementClient(new HttpClient(http), new Uri("http://localhost:5100")), sockets, new SessionStore(Path.Combine(dir, "s.bin")),
                ClientSettings.Default, _ => { }, new ImmediateDispatcher(), _ => TimeSpan.Zero);
            var game = Tabletop.Protocol.Tests.Samples.Game;
            http.Answer(HttpStatusCode.OK, new SessionResponse("a.b.c", new AccountRow(game.Participants[0].AccountId, "Clay")));
            await s.LoginWithPasswordAsync("c@example.com", "pw");
            http.Answer(HttpStatusCode.OK, "[]").Answer(HttpStatusCode.OK, Grantable);
            await s.ConnectAsync(game.Id);
            sockets.Last.Deliver(new Welcome(Tabletop.Protocol.Tests.Samples.P1, game));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (s.Grantable.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(5);
            Assert.Equal(new[] { "Growth", "Hex (1 of 2)", "Hex (2 of 2)" }, s.Grantable.Select(c => c.Label));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task The_grantable_effects_are_read_from_the_pinned_set()
    {
        var f = new FakeManagement(); var c = new ManagementClient(new HttpClient(f), new Uri("http://localhost:5100"));
        f.Answer(HttpStatusCode.OK, $"[{{\"entryName\":\"Hex\",\"effectId\":\"{HexA}\",\"duration\":\"once\"}}]");
        Assert.Equal(new GrantableEffectDto("Hex", HexA, "once"), Assert.Single(await c.GetGrantableEffects("sha256:abc")));
        Assert.Equal("/v1/properties/sha256:abc/grants", f.Requests[0].RequestUri!.AbsolutePath);
    }
}
