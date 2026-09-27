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

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        return _answers.Count > 0 ? _answers.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }
}
