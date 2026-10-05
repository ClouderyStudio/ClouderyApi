using System.Net;
using System.Text;
using System.Text.Json;
using ClouderyApi.Tests.TestSupport;

namespace ClouderyApi.Tests;

/// <summary>
/// 统一错误体契约（见 docs/API-ERROR-SHAPE.md）：所有 &gt;=400 且没有业务体的响应，
/// 都由 ApiErrorBodyMiddleware 补成 {"detail":"..."} —— 包括路由未匹配的 404、方法不允许的 405、
/// 内容类型不支持的 415、以及框架鉴权 challenge 的 401；成功体与控制器自己写的错误体保持原样。
/// </summary>
public sealed class ApiErrorShapeContractTests : IntegrationTestBase
{
    private static void AssertOnlyDetail(JsonDocument body, string expected)
    {
        Assert.Equal(new[] { "detail" }, body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(expected, body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Unknown_route_returns_unified_404()
    {
        var response = await Client.GetAsync("/no-such-route-for-api-error-shape");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var (_, body) = await JsonHttp.ReadAsync(response);
        AssertOnlyDetail(body, "资源不存在");
    }

    [Fact]
    public async Task Wrong_method_returns_unified_405()
    {
        var response = await Client.GetAsync("/identity/auth/callback");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var (_, body) = await JsonHttp.ReadAsync(response);
        AssertOnlyDetail(body, "请求方法不被允许");
    }

    [Fact]
    public async Task Unsupported_content_type_returns_unified_415()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/identity/auth/callback")
        {
            Content = new StringContent("{}", Encoding.UTF8, "text/plain"),
        };

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var (_, body) = await JsonHttp.ReadAsync(response);
        AssertOnlyDetail(body, "不支持的请求内容类型");
    }

    [Fact]
    public async Task Framework_auth_challenge_401_gets_unified_body()
    {
        var response = await JsonHttp.SendAsync(
            Client, HttpMethod.Put, "/cloudery/members/api-error-shape",
            new { name = "新名", position = "新职位" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var (_, body) = await JsonHttp.ReadAsync(response);
        AssertOnlyDetail(body, "请先登录");
    }

    private void SignInAsAdmin()
        => Client.DefaultRequestHeaders.Add(
            "Cookie",
            AuthCookie.CreateHeader(Factory.Services, AuthCookie.TestAdminCasdoorId));

    /// <summary>
    /// 附录 C.3：同一个 API 里「未知资源」的 404 不再有两种形状——
    /// Cloudery / ExamResults / ExamPapers / Zhuxs 六条路径都收敛成 { detail }。
    /// </summary>
    [Fact]
    public async Task Cross_module_404s_share_one_shape()
    {
        SignInAsAdmin();

        var cases = new (HttpMethod Method, string Path, string Detail)[]
        {
            (HttpMethod.Get, "/cloudery/members/does-not-exist", "记录不存在"),
            (HttpMethod.Delete, "/exam/results/does-not-exist", "记录不存在"),
            (HttpMethod.Get, "/exam/ExamPapers/does-not-exist", "未找到该试卷"),
            (HttpMethod.Get, "/zhuxs/whitelists/does-not-exist", "记录不存在"),
            (HttpMethod.Get, "/zhuxs/terms/does-not-exist", "记录不存在"),
            (HttpMethod.Get, "/zhuxs/applications/does-not-exist", "记录不存在"),
        };

        foreach (var (method, path, detail) in cases)
        {
            var response = await JsonHttp.SendAsync(Client, method, path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var (_, body) = await JsonHttp.ReadAsync(response);
            AssertOnlyDetail(body, detail);
        }
    }
}
