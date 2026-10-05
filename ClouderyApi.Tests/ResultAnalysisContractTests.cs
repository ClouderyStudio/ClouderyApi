using System.Net;
using System.Text.Json;
using ClouderyApi.Tests.TestSupport;

namespace ClouderyApi.Tests;

/// <summary>
/// 结果解读（exam/result-analysis）契约。注意 IpRateLimit 限 8 次 / 300 秒（key=ip|path），
/// 本类对同一路径的请求数必须远小于 8；计数走 IRateLimitStore（宿主内单例，每个测试方法一个宿主），
/// 因此不会与其它测试类互相污染。
/// 构造函数把 LLM 端点指向不可达地址，保证 engine=local 的本地兜底路径确定可测。
/// </summary>
public sealed class ResultAnalysisContractTests : IntegrationTestBase
{
    public ResultAnalysisContractTests()
    {
        Environment.SetEnvironmentVariable("Llm__BaseUrl", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("Llm__ApiKey", "contract-test");
    }

    [Fact]
    public async Task Post_without_body_is_rejected_before_the_action_runs()
    {
        var response = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/exam/result-analysis");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_test_id_returns_400_chinese_detail()
    {
        var (emptyStatus, emptyBody) = await JsonHttp.PostJsonAsync(Client, "/exam/result-analysis", new { });
        Assert.Equal(HttpStatusCode.BadRequest, emptyStatus);
        Assert.False(emptyBody.RootElement.TryGetProperty("success", out _));
        Assert.Equal("缺少量表标识（testId）", emptyBody.RootElement.GetProperty("detail").GetString());

        var (blankStatus, blankBody) = await JsonHttp.PostJsonAsync(Client, "/exam/result-analysis", new { testId = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, blankStatus);
        Assert.Equal("缺少量表标识（testId）", blankBody.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Post_with_test_id_returns_local_engine_analysis_in_declared_order()
    {
        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/exam/result-analysis", new
        {
            testId = "phq9",
            testTitle = "PHQ-9",
            totalScore = 10,
            maxScore = 27,
            level = "中度",
            severity = 0.5,
            risk = false,
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            new[] { "analysis", "engine", "crisis", "generatedAt" },
            body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("local", body.RootElement.GetProperty("engine").GetString());
        Assert.False(body.RootElement.GetProperty("crisis").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("analysis").GetString()));
        Assert.True(body.RootElement.GetProperty("generatedAt").TryGetDateTimeOffset(out _));
    }
}
