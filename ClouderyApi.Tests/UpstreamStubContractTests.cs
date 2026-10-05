using System.Net;
using System.Text;
using System.Text.Json;
using ClouderyApi.Shared.Options;
using ClouderyApi.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClouderyApi.Tests;

/// <summary>
/// 上游 HTTP 的成功路径（C.6③④）：把 Casdoor 与 LLM 的 HttpClient 处理器换成桩，
/// 覆盖「真实 Casdoor 回调建立 Cookie 会话」与「LLM 应答时 engine=llm」两条分支。
/// 其它测试把 Llm__BaseUrl 指向不可达地址，只覆盖本地兜底。
/// Casdoor 的 OIDC 发现文档由 <see cref="CasdoorDiscoveryStubServer"/> 在 loopback 端口上应答：
/// SDK 用自建 HttpClient 抓它，桩处理器拦不到。
/// </summary>
public sealed class UpstreamStubContractTests : IntegrationTestBase
{
    private const string CasdoorUserId = "casdoor-user-1";
    private const string CasdoorUserName = "cbtest";

    private readonly RecordingLoggerProvider _recorder = new();

    /// <summary>OIDC 发现文档桩（只在需要回调成功路径的测试里启动）。</summary>
    private CasdoorDiscoveryStubServer? _casdoor;

    private const string LlmReply =
        "1) 第一段：整体状态需要关注。\n2) 第二段：请保持规律作息。\n3) 第三段：可与亲友多交流。\n4) 第四段：必要时寻求专业帮助。";

    public UpstreamStubContractTests()
    {
        // 只换处理器、不改配置形状；BaseUrl 指向桩域名，请求不会真的出网。
        Environment.SetEnvironmentVariable("Llm__BaseUrl", "http://llm.stub.local");
        Environment.SetEnvironmentVariable("Llm__ApiKey", "contract-test");
    }

    protected override void ConfigureFactory(ClouderyApiFactory factory)
    {
        // Casdoor.Client 换 token 前会先用 IdentityModel 的 ConfigurationManager 抓 OIDC 发现文档
        // （CasdoorOptions.Validate() 里 AutoDiscovery 默认为 true，控制器自建的 CasdoorOptions 不听 DI 配置），
        // 那份文档由 SDK 自建的 HttpClient 发出，桩处理器拦不到 —— 只能用真实 loopback 端口应答，
        // 并把 Casdoor:Endpoint 指过去，让 SDK 的 authority = 桩地址。
        _casdoor = CasdoorDiscoveryStubServer.Start();

        factory.ConfigureTestServices = services =>
        {
            services.AddLogging(logging => logging.AddProvider(_recorder));
            services.PostConfigure<CasdoorSettings>(settings => settings.Endpoint = _casdoor!.BaseUrl);

            // token 端点仍由桩应答：这一步走 DI 的 "Casdoor" 命名客户端（发现文档里的 token_endpoint 指向它）。
            services.AddHttpClient("Casdoor").ConfigurePrimaryHttpMessageHandler(() =>
                new StubHandler(request =>
                {
                    var path = request.RequestUri!.AbsolutePath;
                    if (path.Contains("access_token", StringComparison.OrdinalIgnoreCase)) return AccessTokenResponse();
                    if (path.Contains("jwks", StringComparison.OrdinalIgnoreCase)) return "{\"keys\":[]}";
                    return "{}";
                }));

            // LlmClient 用 IHttpClientFactory.CreateClient()，即默认（空名）客户端；
            // 注意参数为空的 AddHttpClient() 只注册基础设施并返回 IServiceCollection，必须用 AddHttpClient(string.Empty)。
            services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() =>
                new StubHandler(request => request.RequestUri!.AbsolutePath
                    .Contains("chat/completions", StringComparison.OrdinalIgnoreCase)
                        ? LlmResponse()
                        : "{}"));
        };
    }

    /// <summary>先让基类释放宿主与一次性库，再停掉本类拉起的发现文档桩。</summary>
    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_casdoor is not null)
        {
            await _casdoor.DisposeAsync();
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json"),
            });
    }

    private static string LlmResponse() => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = LlmReply } } },
    });

    private static string AccessTokenResponse() => JsonSerializer.Serialize(new
    {
        access_token = Jwt(JsonSerializer.Serialize(new
        {
            id = CasdoorUserId,
            sub = CasdoorUserId,
            name = CasdoorUserName,
            displayName = "CB Test",
            email = "cb@example.com",
            avatar = "",
            owner = "test",
        })),
        token_type = "Bearer",
        expires_in = 3600,
        refresh_token = "stub-refresh-token",
    });

    /// <summary>三段式 JWT：ParseJwtToken(..., validate: false) 只解负载，签名段只要结构合法。</summary>
    private static string Jwt(string payload)
        => Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")
           + "." + Base64Url(payload)
           + "." + Base64Url("stub-signature");

    private static string Base64Url(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task Casdoor_callback_establishes_a_cookie_session()
    {
        var (stateStatus, stateBody) = await JsonHttp.GetJsonAsync(Client, "/identity/auth/state");
        Assert.Equal(HttpStatusCode.OK, stateStatus);
        var state = stateBody.RootElement.GetProperty("state").GetString()!;

        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/identity/auth/callback", new
        {
            code = "stub-auth-code",
            state,
            redirectUri = "https://localhost/identity/auth/callback",
        });

        Assert.True(
            status == HttpStatusCode.OK,
            $"Casdoor 回调失败：{status} {body.RootElement.GetRawText()}\n宿主日志：\n{_recorder.Text}");
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("登录成功", body.RootElement.GetProperty("message").GetString());
        var user = body.RootElement.GetProperty("user");
        Assert.Equal(CasdoorUserName, user.GetProperty("username").GetString());
        Assert.Equal("cb@example.com", user.GetProperty("email").GetString());

        // 会话 Cookie 已种下：紧接着的 /identity/auth/me 认得出同一个用户。
        var (meStatus, me) = await JsonHttp.GetJsonAsync(Client, "/identity/auth/me");
        Assert.Equal(HttpStatusCode.OK, meStatus);
        Assert.Equal(CasdoorUserName, me.RootElement.GetProperty("user").GetProperty("username").GetString());
    }

    [Fact]
    public async Task Result_analysis_uses_the_llm_engine_when_upstream_answers()
    {
        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/exam/result-analysis", new
        {
            testId = "phq9",
            testTitle = "PHQ-9",
            totalScore = 10,
            maxScore = 27,
            level = "中度",
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("llm", body.RootElement.GetProperty("engine").GetString());
        Assert.Contains("第一段", body.RootElement.GetProperty("analysis").GetString());
        Assert.False(body.RootElement.GetProperty("crisis").GetBoolean());
    }
}
