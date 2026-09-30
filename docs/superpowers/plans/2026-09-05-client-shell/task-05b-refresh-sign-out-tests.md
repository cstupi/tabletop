# Task 5b: The refresh and sign-out tests

Part of Task 5; its files and interfaces are in [Task 5a](task-05a-session-tests.md).

- [ ] **Step 2: Extend `FakeManagement` and write the refresh tests**

`FakeManagement` (Task 3) gains `Fail(Exception)`, which makes the next request throw instead of answering, and `Gate`, which holds every request after it is recorded until the test completes it. `Gate` is how a test puts a login or logout inside a refresh's flight.

```csharp
using System.Net;
using System.Text;
using Tabletop.Content;

namespace Tabletop.Client.Tests;

public sealed class FakeManagement : HttpMessageHandler
{
    public readonly List<HttpRequestMessage> Requests = new();
    public readonly List<string> Bodies = new();
    readonly Queue<HttpResponseMessage> _answers = new();

    public FakeManagement Answer(HttpStatusCode status, object? body = null, string mediaType = "application/json")
    {
        var content = body is null ? null : new StringContent(body as string ?? ContentJson.Serialize(body), Encoding.UTF8, mediaType);
        _answers.Enqueue(new HttpResponseMessage(status) { Content = content });
        return this;
    }

    readonly Queue<Exception?> _failures = new();
    /// <summary>When set, every request waits for it after being recorded and before it is answered.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>The next request throws instead of being answered.</summary>
    public FakeManagement Fail(Exception error) { _failures.Enqueue(error); _answers.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)); return this; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        if (Gate is { } gate) await gate.Task;
        if (_failures.Count > 0 && _failures.Peek() is { } error) { _failures.Dequeue(); _answers.Dequeue(); throw error; }
        if (_failures.Count > 0) _failures.Dequeue();
        return _answers.Count > 0 ? _answers.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }
}
```

The tests run the session on a `ManualTime` clock, so the one-minute back-off is stepped rather than waited.

```csharp
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

    [Fact]
    public async Task A_store_that_cannot_clear_does_not_wedge_later_refreshes()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;      // read-only directories do not stop these
        await LoggedIn();
        var dir = Path.GetDirectoryName(Path.Combine(_dir, "session.bin"))!;
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);      // File.Delete now throws
        try
        {
            Problem(401);
            Assert.False(await _s.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(SessionPhase.LoggedOut, _s.Phase);
            Assert.Null(_s.Management.Jwt);
        }
        finally { File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        await LoggedIn();
        _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt2, Clay));
        Assert.True(await _s.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_logout_during_a_refresh_that_then_fails_leaves_the_token_gone()
    {
        await LoggedIn();
        _http.Gate = new TaskCompletionSource();
        Problem(502);
        var refresh = _s.RefreshAsync();
        var logout = _s.LogoutAsync();
        _http.Gate.SetResult();
        Assert.False(await refresh);
        await logout;
        Assert.Null(_s.Management.Jwt);
        var before = _http.Requests.Count;
        _time.Advance(Minute);
        Assert.False(await _s.RefreshAsync());
        Assert.Equal(before, _http.Requests.Count);
    }

    [Fact]
    public async Task A_login_during_a_refresh_that_then_succeeds_keeps_the_new_login()
    {
        await LoggedIn();
        var other = new AccountRow(Guid.NewGuid(), "Other");
        var gate = _http.Gate = new TaskCompletionSource();
        _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt-3.z", other));           // the login, answered first: the gate holds only the refresh
        _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt2, Clay));                   // the refresh
        var refresh = _s.RefreshAsync();
        _http.Gate = null;
        await _s.LoginWithPasswordAsync("o@example.com", "pw");
        gate.SetResult();
        Assert.False(await refresh);
        Assert.Equal(("a.jwt-3.z", "Other"), (_s.Management.Jwt, _s.Account!.Name));
        Assert.Equal("a.jwt-3.z", _store.Read());
    }

    [Fact]
    public async Task A_login_during_a_refresh_that_then_401s_stays_logged_in()
    {
        await LoggedIn();
        var other = new AccountRow(Guid.NewGuid(), "Other");
        var gate = _http.Gate = new TaskCompletionSource();
        _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt-3.z", other));           // the login, answered first: the gate holds only the refresh
        Problem(401);                                                                       // the refresh
        var refresh = _s.RefreshAsync();
        _http.Gate = null;
        await _s.LoginWithPasswordAsync("o@example.com", "pw");
        gate.SetResult();
        Assert.False(await refresh);
        Assert.Equal(SessionPhase.LoggedIn, _s.Phase);
        Assert.Equal("a.jwt-3.z", _s.Management.Jwt);
    }
}
```
