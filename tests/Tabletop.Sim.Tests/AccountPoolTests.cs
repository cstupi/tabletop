using System.Net;
using System.Text;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Tests;

/// <summary>Every socket game plays on one fixed pool of dev-session accounts: participant i is sim-player-i, whatever the game.</summary>
public class AccountPoolTests
{
    static IEnumerable<CompiledScenario> EveryScenario() =>
        ScenarioCatalog.Names.Select(ScenarioCatalog.Get).Concat(SuiteCatalog.All.Select(g => g.Scenario)).Select(ScenarioCompiler.Compile);

    [Fact]
    public void Participant_i_is_sim_player_i()
    {
        Assert.Equal("sim-player-0", SocketCredentials.NameFor(0));
        Assert.Equal(Enumerable.Range(0, SocketCredentials.PoolSize).Select(i => $"sim-player-{i}"), SocketCredentials.Pool);
    }

    [Fact]
    public void The_pool_is_as_large_as_the_most_participants_any_scenario_has()
    {
        Assert.Equal(EveryScenario().Max(c => c.Info.Participants.Count), SocketCredentials.PoolSize);
    }

    [Fact]
    public void A_participant_past_the_pool_has_no_name()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SocketCredentials.NameFor(SocketCredentials.PoolSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => SocketCredentials.NameFor(-1));
    }

    [Fact]
    public void Every_kept_game_has_the_one_spectator() => Assert.Equal("sim-spectator", SuiteRows.Spectator);

    sealed class Answers(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        int _next;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            var status = statuses[Math.Min(_next++, statuses.Length - 1)];
            var id = Guid.CreateVersion7();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK
                    ? $$$"""{"sessionJwt":"jwt","account":{"id":"{{{id}}}","name":"x"}}""" : "{}", Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task The_mint_asks_for_the_pool_names_in_participant_order()
    {
        var handler = new Answers(HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://mint/") };
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish"));

        var credentials = await SocketCredentials.MintAsync(c, http, CancellationToken.None);

        Assert.Equal(c.Info.Participants.Count, credentials.Sessions.Count);
        Assert.Equal(Enumerable.Range(0, c.Info.Participants.Count).Select(i => $$"""{"name":"sim-player-{{i}}"}"""), handler.Bodies);
    }

    /// <summary>Two runs minting a pool name for the first time at once: the loser of the Stytch create gets 409 and asks again.</summary>
    [Fact]
    public async Task A_conflict_on_the_first_mint_of_a_name_is_asked_again()
    {
        var handler = new Answers(HttpStatusCode.Conflict, HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://mint/") };

        var session = await SocketCredentials.MintOneAsync(http, "sim-player-0", CancellationToken.None);

        Assert.Equal("jwt", session.Jwt);
        Assert.Equal(2, handler.Bodies.Count);
    }
}
