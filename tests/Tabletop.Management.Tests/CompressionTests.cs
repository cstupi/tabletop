using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;

namespace Tabletop.Management.Tests;

/// <summary>Response compression (Brotli, gzip) on management's JSON, the session-carrying auth answers excepted, and the content-hash routes' Cache-Control.</summary>
public class CompressionTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public CompressionTests(ManagementFactory f) => _f = f;
    const string Hash = "sha256:test-compression";

    async Task<HttpClient> Client(string? encoding, bool https = false)
    {
        lock (_f.Rows.Gate)
            if (!_f.Rows.Properties.ContainsKey(Hash))
            {
                _f.Rows.SeedProperties(Hash, "Compression", "grimdark-future");
                for (var i = 0; i < 40; i++) _f.Rows.SeedCatalogEntry(Hash, $"Rule {i:D2}", "unit", false, "Gets +1 to hit when attacking, and the same again.");
            }
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7(), "Clay");
        if (https) c.BaseAddress = new Uri("https://localhost");
        if (encoding is not null) c.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue(encoding));
        return c;
    }

    static async Task<string> Decoded(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStreamAsync();
        Stream decoded = r.Content.Headers.ContentEncoding.SingleOrDefault() switch
        {
            "br" => new BrotliStream(body, CompressionMode.Decompress),
            "gzip" => new GZipStream(body, CompressionMode.Decompress),
            null => body,
            var other => throw new InvalidOperationException(other),
        };
        return await new StreamReader(decoded).ReadToEndAsync();
    }

    [Theory]
    [InlineData("br", false)]
    [InlineData("gzip", false)]
    [InlineData("br", true)]                 // behind Caddy the request is https (X-Forwarded-Proto)
    [InlineData("gzip", true)]
    public async Task Json_is_compressed_in_the_encoding_asked_for_and_decodes_to_the_plain_body(string encoding, bool https)
    {
        var plain = await (await (await Client(null)).GetAsync("/v1/layouts/builtin")).Content.ReadAsStringAsync();
        var r = await (await Client(encoding, https)).GetAsync("/v1/layouts/builtin");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal([encoding], r.Content.Headers.ContentEncoding);
        Assert.Contains("Accept-Encoding", r.Headers.Vary);
        Assert.Equal(plain, await Decoded(r));
    }

    [Fact]
    public async Task Rule_texts_compress_too()
    {
        var r = await (await Client("gzip", https: true)).GetAsync($"/v1/properties/{Uri.EscapeDataString(Hash)}/rules");
        Assert.Equal(["gzip"], r.Content.Headers.ContentEncoding);
        Assert.Contains("Rule 39", await Decoded(r));
    }

    [Fact]
    public async Task Nothing_is_compressed_without_an_accept_encoding()
    {
        var r = await (await Client(null)).GetAsync("/v1/layouts/builtin");
        Assert.Empty(r.Content.Headers.ContentEncoding);
    }

    [Theory]
    [InlineData("/v1/auth/otp")]
    public async Task An_auth_answer_is_never_compressed(string path)
    {
        var c = await Client("gzip", https: true);
        var r = await c.PostAsJsonAsync(path, new { email = "compress@example.com" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Empty(r.Content.Headers.ContentEncoding);
    }

    [Theory]
    [InlineData("rules")]
    [InlineData("grants")]
    public async Task A_content_hash_route_is_immutable_for_its_hash_and_a_miss_is_not(string route)
    {
        var c = await Client(null);
        var hit = await c.GetAsync($"/v1/properties/{Uri.EscapeDataString(Hash)}/{route}");
        Assert.Equal(HttpStatusCode.OK, hit.StatusCode);
        var cache = hit.Headers.CacheControl!;
        Assert.True(cache.Private);
        Assert.False(cache.Public);
        Assert.Equal(TimeSpan.FromDays(365), cache.MaxAge);
        Assert.Contains(cache.Extensions, e => e.Name == "immutable");
        var miss = await c.GetAsync($"/v1/properties/sha256%3Anope/{route}");
        Assert.Equal(HttpStatusCode.NotFound, miss.StatusCode);
        Assert.Null(miss.Headers.CacheControl);
    }

    [Fact]
    public async Task Other_routes_carry_no_cache_header()
    {
        var r = await (await Client(null)).GetAsync("/v1/properties");
        Assert.Null(r.Headers.CacheControl);
    }
}
