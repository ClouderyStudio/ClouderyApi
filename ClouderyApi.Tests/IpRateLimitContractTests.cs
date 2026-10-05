using System.Net;
using System.Text.Json;
using ClouderyApi.Tests.TestSupport;

namespace ClouderyApi.Tests;

/// <summary>
/// 单接口限流（<c>IpRateLimitAttribute</c>）的对外契约：/exam/result-analysis 限 8 次 / 300 秒，
/// 第 9 次返回 429 + 既有中文文案 + <c>Retry-After</c> 头，响应体仍是 Cloudery 模块的裸对象
/// <c>{ success, message, retryAfterSeconds }</c>（不是 MHOP 的 <c>{ detail }</c>）。
/// <para>
/// 计数走 IRateLimitStore（未配置 Redis 时是进程内实现，宿主内单例）。每个测试方法各有独立宿主，
/// 因此这里的 9 次请求不会与其它测试类互相污染。
/// </para>
/// </summary>
public sealed class IpRateLimitContractTests : IntegrationTestBase
{
    public IpRateLimitContractTests()
    {
        // 放行的请求走本地兜底解读，不打真实模型。
        Environment.SetEnvironmentVariable("Llm__BaseUrl", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("Llm__ApiKey", "contract-test");
    }

    [Fact]
    public async Task Ninth_request_within_the_window_is_rejected_with_the_existing_body()
    {
        var payload = new
        {
            testId = "phq9",
            testTitle = "PHQ-9",
            totalScore = 10,
            maxScore = 27,
            level = "中度",
            severity = 0.5,
            risk = false,
        };

        for (var i = 1; i <= 8; i++)
        {
            var allowed = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/exam/result-analysis", payload);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var limited = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/exam/result-analysis", payload);
        var body = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(new[] { "detail", "retryAfterSeconds" }, body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("分析请求过于频繁，请稍后再试", body.RootElement.GetProperty("detail").GetString());

        // 固定窗口：窗口内的 8 次请求本身要花十几秒，所以剩余秒数是「300 - 已过时间」，只断言范围。
        var bodyRetryAfterSeconds = body.RootElement.GetProperty("retryAfterSeconds").GetInt32();
        Assert.InRange(bodyRetryAfterSeconds, 1, 300);

        // 响应头与响应体取自同一个计数值，必须一致（既有行为：头是 retryAfterSeconds 的秒数）。
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal(bodyRetryAfterSeconds, (int)limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds);
    }
}
