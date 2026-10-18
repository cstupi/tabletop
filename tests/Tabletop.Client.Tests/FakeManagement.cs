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

    public FakeManagement Respond(HttpResponseMessage response) { _answers.Enqueue(response); return this; }

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
