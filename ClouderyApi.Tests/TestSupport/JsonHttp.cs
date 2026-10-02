using System.Net;
using System.Text;
using System.Text.Json;

namespace ClouderyApi.Tests.TestSupport;

/// <summary>测试用的 JSON 请求/响应小工具：统一 UTF-8 请求体、可选 Bearer 头与 JsonDocument 解析。</summary>
public static class JsonHttp
{
    public static StringContent Body(object body)
        => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null, string? token = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = Body(body);
        if (token is not null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? token = null)
        => SendAsync(client, HttpMethod.Get, path, null, token);

    public static async Task<(HttpStatusCode Status, JsonDocument Body)> ReadAsync(HttpResponseMessage response)
        => (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()));

    public static async Task<(HttpStatusCode Status, JsonDocument Body)> PostJsonAsync(HttpClient client, string path, object body, string? token = null)
        => await ReadAsync(await SendAsync(client, HttpMethod.Post, path, body, token));

    public static async Task<(HttpStatusCode Status, JsonDocument Body)> PutJsonAsync(HttpClient client, string path, object body, string? token = null)
        => await ReadAsync(await SendAsync(client, HttpMethod.Put, path, body, token));

    public static async Task<(HttpStatusCode Status, JsonDocument Body)> GetJsonAsync(HttpClient client, string path, string? token = null)
        => await ReadAsync(await GetAsync(client, path, token));
}
