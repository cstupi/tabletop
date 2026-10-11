using System.Net;
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Godot's deferred calls: a post waits for the drain, and a post made while draining runs in the same drain.</summary>
sealed class QueueDispatcher : IDispatcher
{
    readonly Queue<Action> _queue = new();
    public void Post(Action action) => _queue.Enqueue(action);
    public void Drain() { while (_queue.TryDequeue(out var a)) a(); }
}

public class RenderSchedulerTests
{
    readonly QueueDispatcher _d = new();

    [Fact]
    public void Marks_before_a_flush_render_once_with_every_cause()
    {
        var r = new RenderScheduler(_d); var seen = new List<RenderCause>();
        r.Render += seen.Add;
        r.Mark(RenderCause.Events); r.Mark(RenderCause.Events); r.Mark(RenderCause.Facts);
        Assert.Empty(seen);
        _d.Drain();
        Assert.Equal([RenderCause.Events | RenderCause.Facts], seen);
        _d.Drain();
        Assert.Single(seen);
    }

    [Fact]
    public void A_mark_after_a_flush_renders_again_and_none_marks_nothing()
    {
        var r = new RenderScheduler(_d); var seen = new List<RenderCause>();
        r.Render += c => { seen.Add(c); if (seen.Count == 1) r.Mark(RenderCause.Board); };   // a render that changes state
        r.Mark(RenderCause.None); _d.Drain();
        Assert.Empty(seen);
        r.Mark(RenderCause.Game); _d.Drain();
        Assert.Equal([RenderCause.Game, RenderCause.Board], seen);
    }

    /// <summary>The performance review's finding 4: an activation is about 32 EventsMessages. Arriving within one frame they were 32
    /// renders of every Hud part; now they are one.</summary>
    [Fact]
    public async Task An_activations_batches_in_one_frame_are_one_render()
    {
        var http = new FakeManagement(); var sockets = new FakeSocketFactory();
        var dir = Directory.CreateTempSubdirectory("tabletop-frame-tests").FullName;
        try
        {
            var s = new Session(new ManagementClient(new HttpClient(http), new Uri("http://localhost:5100")), sockets, new SessionStore(Path.Combine(dir, "s.bin")),
                ClientSettings.Default, _ => { }, _d, _ => TimeSpan.Zero);
            http.Answer(HttpStatusCode.OK, new SessionResponse("a.b.c", new AccountRow(Samples.Game.Participants[0].AccountId, "Clay")));
            await s.LoginWithPasswordAsync("c@example.com", "pw");
            var game = Samples.Game;
            await s.ConnectAsync(game.Id);
            var sock = sockets.Last;
            sock.Deliver(new Welcome(Samples.P1, game)); sock.Deliver(new BoardMessage(Samples.Board));
            _d.Drain();
            var batches = 0; var renders = new List<RenderCause>();
            s.EventsApplied += _ => batches++; s.Frame.Render += renders.Add;
            for (var i = 1; i <= 32; i++) sock.Deliver(new EventsMessage([new GameEvent(i, Samples.P1, new ScoreSet(Samples.Team1, i), null)]));
            sock.Deliver(new ModelFacts(new Dictionary<Guid, int>()));
            _d.Drain();
            Assert.Equal(32, batches);                                          // what each part rendered before
            Assert.Equal([RenderCause.Events | RenderCause.Facts], renders);    // what it renders now
            Assert.Equal(32, s.Projection!.Scores[Samples.Team1]);
        }
        finally { Directory.Delete(dir, true); }
    }
}
