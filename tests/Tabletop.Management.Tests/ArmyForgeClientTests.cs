using System.Net;
using System.Text.Json;
using Tabletop.Management.Content.Armies;

namespace Tabletop.Management.Tests;

public class ArmyForgeClientTests
{
    const string Export = """{"id":"t","name":"T","gameSystem":"gf","units":[]}""";

    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }

    /// <summary>Content of a declared length (or none) whose stream yields `actual` bytes of '1' without buffering them first.</summary>
    sealed class Endless(long? declared, long actual) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
        protected override bool TryComputeLength(out long length) { length = declared ?? 0; return declared is not null; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new Ones(actual));
    }

    sealed class Ones(long length) : Stream
    {
        long _left = length;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, _left);
            Array.Fill(buffer, (byte)'1', offset, n);
            _left -= n;
            return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    static ArmyForgeClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, TimeSpan? timeout = null) =>
        new(new HttpClient(new Handler(send)), timeout);
    static Task<HttpResponseMessage> Ok(HttpContent content) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

    [Fact]
    public async Task A_good_export_parses()
    {
        var list = await Client((_, _) => Ok(new StringContent(Export))).Fetch("abcd");
        Assert.Equal("T", list.Name);
    }

    [Fact]
    public async Task A_slow_answer_times_out()
    {
        var e = await Assert.ThrowsAsync<ArmyForgeException>(() => Client(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); }, TimeSpan.FromMilliseconds(50)).Fetch("abcd"));
        Assert.Null(e.Status);
        Assert.Equal(TimeSpan.FromSeconds(10), ArmyForgeClient.DefaultTimeout);
    }

    [Fact]
    public async Task A_body_over_2_MB_is_refused_whatever_its_content_length_says()
    {
        Assert.Equal(2 * 1024 * 1024, ArmyForgeClient.MaxBytes);
        await Assert.ThrowsAsync<ArmyForgeException>(() => Client((_, _) => Ok(new Endless(ArmyForgeClient.MaxBytes + 1, 0))).Fetch("abcd"));
        await Assert.ThrowsAsync<ArmyForgeException>(() => Client((_, _) => Ok(new Endless(null, ArmyForgeClient.MaxBytes + 1))).Fetch("abcd"));
        await Assert.ThrowsAsync<ArmyForgeException>(() => Client((_, _) => Ok(new Endless(10, 64L * 1024 * 1024))).Fetch("abcd"));
    }

    [Fact]
    public async Task Failures_are_army_forge_exceptions_and_bad_json_is_a_json_exception()
    {
        Assert.Equal(404, (await Assert.ThrowsAsync<ArmyForgeException>(() => Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))).Fetch("abcd"))).Status);
        Assert.Null((await Assert.ThrowsAsync<ArmyForgeException>(() => Client((_, _) => throw new HttpRequestException("refused")).Fetch("abcd"))).Status);
        await Assert.ThrowsAnyAsync<JsonException>(() => Client((_, _) => Ok(new StringContent("{not json"))).Fetch("abcd"));
        await Assert.ThrowsAnyAsync<JsonException>(() => Client((_, _) => Ok(new StringContent("null"))).Fetch("abcd"));
    }
}
