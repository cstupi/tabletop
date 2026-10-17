using System.Net.Http.Json;
using Tabletop.Content;

namespace Tabletop.Management.Tests;

public static class HttpJson
{
    public static async Task<T> PostJson<T>(this HttpClient c, string url, object body) => await Read<T>(await c.PostAsJsonAsync(url, body, ContentJson.Options));
    public static async Task<T> PutJson<T>(this HttpClient c, string url, object body) => await Read<T>(await c.PutAsJsonAsync(url, body, ContentJson.Options));
    public static async Task<T> PatchJson<T>(this HttpClient c, string url, object body) => await Read<T>(await c.PatchAsJsonAsync(url, body, ContentJson.Options));
    public static Task<HttpResponseMessage> PatchJson(this HttpClient c, string url, object body) => c.PatchAsJsonAsync(url, body, ContentJson.Options);
    public static async Task<T> GetJson<T>(this HttpClient c, string url) => await Read<T>(await c.GetAsync(url));

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(ContentJson.Options))!;
    }
}
