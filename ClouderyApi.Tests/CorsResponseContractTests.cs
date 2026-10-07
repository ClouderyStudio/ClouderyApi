using System.Net;
using System.Text.Json;
using ClouderyApi.Tests.TestSupport;

namespace ClouderyApi.Tests;

/// <summary>
/// 跨域错误响应的对外契约：CORS 中间件必须排在「会短路并直接写响应」的中间件（全局限流、CSRF）之前，
/// 否则这些响应带不上 <c>Access-Control-Allow-Origin</c>，浏览器一律按跨域失败处理，
/// 前端 axios 拿不到 <c>error.response</c>，只能回退到英文 <c>Network Error</c>，看不到 <c>{ detail }</c> 文案。
/// </summary>
public sealed class CorsResponseContractTests : IntegrationTestBase
{
    private const string Origin = "https://mhop.cldery.com";

    private static HttpRequestMessage GetWithOrigin(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        // 模拟浏览器跨域请求：带 Origin 的客户端才会收到 Access-Control-Allow-Origin。
        request.Headers.Add("Origin", Origin);
        return request;
    }

    [Fact]
    public async Task Global_rate_limit_429_carries_the_cors_header()
    {
        // 全局限流是 300 次 / 60 秒且按实测的客户端 IP 计数，测试宿主里不再猜 key，
        // 直接把额度打满，让下一个请求命中 429。
        for (var i = 0; i < 300; i++)
        {
            using var allowed = GetWithOrigin("/mhop/health");
            var ok = await Client.SendAsync(allowed);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var limited = GetWithOrigin("/mhop/health");
        var response = await Client.SendAsync(limited);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(Origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("请求过于频繁，请稍后再试", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Error_responses_carry_the_cors_header_for_an_allowed_origin()
    {
        using var request = GetWithOrigin("/mhop/auth/me");
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("请先登录", body.RootElement.GetProperty("detail").GetString());
    }
}
