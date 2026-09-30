using System.Net;
using Tabletop.Client.Core;
using Tabletop.Content.Tests;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

sealed class ManualTime : TimeProvider
{
    DateTimeOffset _now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public class SessionRefreshTests : IDisposable
{
    const string Jwt1 = "a.jwt-1.z", Jwt2 = "a.jwt-2.z";
    static readonly AccountRow Clay = new(Guid.NewGuid(), "Clay");
    static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-refresh-tests").FullName;
    readonly FakeManagement _http = new();
    readonly FakeSocketFactory _sockets = new();
    readonly ManualTime _time = new();
    readonly SessionStore _store;
    readonly Session _s;

    public SessionRefreshTests()
    {
        _store = new SessionStore(Path.Combine(_dir, "session.bin"));
        _s = new Session(new ManagementClient(new HttpClient(_http), new Uri("http://localhost:5100")), _sockets, _store,
            ClientSettings.Default with { RememberMe = true }, _ => { }, new ImmediateDispatcher(), _ => TimeSpan.Zero, _time);
    }
    public void Dispose() => Directory.Delete(_dir, true);

    async Task LoggedIn() { _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt1, Clay)); await _s.LoginWithPasswordAsync("c@example.com", "pw"); }
    void Problem(int status) => _http.Answer((HttpStatusCode)status, "{\"title\":\"x\"}", "application/problem+json");
    int Refreshes => _http.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/auth/session/refresh");

    [Fact]
    public async Task A_401_on_resume_signs_out_and_clears_the_store()
    {
        await LoggedIn();
        Problem(401);
        Assert.False(await _s.TryResumeAsync());
        Assert.Equal(SessionPhase.LoggedOut, _s.Phase);
        Assert.Null(_s.Account);
        Assert.Null(_s.Management.Jwt);
        Assert.Null(_store.Read());
    }

    [Theory]
    [InlineData(502)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Other_statuses_on_resume_keep_the_token_and_a_later_refresh_recovers(int status)
    {
        _store.Write(Jwt1);
        Problem(status);
        Assert.False(await _s.TryResumeAsync());
        Assert.Equal(SessionPhase.LoggedOut, _s.Phase);
        Assert.Equal(Jwt1, _store.Read());
        Assert.Equal(Jwt1, _s.Management.Jwt);
        _time.Advance(Minute);
        _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt2, Clay));
        Assert.True(await _s.RefreshAsync());
        Assert.Equal(SessionPhase.LoggedIn, _s.Phase);
        Assert.Equal(Jwt2, _s.Management.Jwt);
    }

    [Fact]
    public async Task A_transport_failure_keeps_the_token()
    {
        await LoggedIn();
        _http.Fail(new HttpRequestException("down"));
        Assert.False(await _s.RefreshAsync());
        Assert.Equal(Jwt1, _s.Management.Jwt);
        Assert.Equal(Jwt1, _store.Read());
        Assert.Equal(SessionPhase.LoggedIn, _s.Phase);
    }

    [Fact]
    public async Task After_a_failure_no_request_goes_out_for_a_full_minute()
    {
        await LoggedIn();
        Problem(502);
        Assert.False(await _s.RefreshAsync());
        Assert.Equal(1, Refreshes);
        _time.Advance(Minute - TimeSpan.FromSeconds(1));
        Assert.False(await _s.RefreshAsync());
        Assert.Equal(1, Refreshes);
        _time.Advance(TimeSpan.FromSeconds(1));
        _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt2, Clay));
        Assert.True(await _s.RefreshAsync());
        Assert.Equal(2, Refreshes);
        _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt2, Clay));
        Assert.True(await _s.RefreshAsync());     // a success cleared the back-off
        Assert.Equal(3, Refreshes);
    }

    [Fact]
    public async Task Concurrent_refreshes_share_one_request()
    {
        await LoggedIn();
        _http.Gate = new TaskCompletionSource();
        _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt2, Clay));
        var a = _s.RefreshAsync();
        var b = _s.RefreshAsync();
        _http.Gate.SetResult();
        Assert.True(await a);
        Assert.True(await b);
        Assert.Equal(1, Refreshes);
    }

    [Fact]
    public async Task A_401_on_refresh_while_connected_closes_the_socket_and_signs_out()
    {
        await LoggedIn();
        await _s.ConnectAsync(Guid.NewGuid());
        var sock = _sockets.Last;
        Problem(401);
        Assert.False(await _s.RefreshAsync());
        Assert.False(sock.Connected);
        Assert.Equal(SessionPhase.LoggedOut, _s.Phase);
        Assert.Null(_s.Management.Jwt);
        Assert.Null(_store.Read());
    }

    [Fact]
    public async Task A_426_on_refresh_is_the_update_required_phase_without_throwing()
    {
        await LoggedIn();
        Problem(426);
        Assert.False(await _s.RefreshAsync());
        Assert.Equal(SessionPhase.UpdateRequired, _s.Phase);
    }
}
