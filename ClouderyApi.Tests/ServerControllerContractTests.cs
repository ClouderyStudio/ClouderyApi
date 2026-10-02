using System.Net;
using System.Text;
using ClouderyApi.Modules.SurvivalCraft.Api;
using ClouderyApi.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// /sc/Server 转发契约：路径校验、上游状态码与响应体透传、Bearer 令牌取自 Env:SCKEY_* 配置键。
/// 出站请求由桩处理器接管，测试不访问真实 SCKEY 后端。
/// </summary>
public sealed class ServerControllerContractTests : IAsyncLifetime
{
    private const string StubBase = "http://stub.invalid";
    private const string StubToken = "test-token";

    private sealed class StubHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Uri, string? Authorization, string Body)> Requests = new();

        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("upstream-ok", Encoding.UTF8, "text/plain") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method.Method, request.RequestUri?.ToString() ?? "", request.Headers.Authorization?.ToString(), body));
            return Responder(request);
        }
    }

    private readonly StubHandler _handler = new();
    private string _databaseName = string.Empty;
    private ClouderyApiFactory _factory = null!;
    private WebApplicationFactory<Program> _stubbed = null!;
    private HttpClient _client = null!;
    private string _cookie = string.Empty;

    public async Task InitializeAsync()
    {
        // ServerController 的配置来自应用配置系统；环境变量源排在 appsettings.json 之后，可稳定压过它。
        Environment.SetEnvironmentVariable("Env__SCKEY_API_BASE", StubBase);
        Environment.SetEnvironmentVariable("Env__SCKEY_BEARER_TOKEN", StubToken);

        _databaseName = MySqlTestServer.NewDatabaseName();
        await MySqlTestServer.CreateDatabaseAsync(_databaseName);

        _factory = new ClouderyApiFactory(_databaseName);
        await _factory.PrepareAuxiliarySchemasAsync();

        // 出站 HttpClient 由桩处理器接管：AddHttpClient 的处理器工厂按注册顺序覆盖，测试注册排在应用之后。
        _stubbed = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(ServerController.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _handler)));

        _client = _stubbed.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        _cookie = AuthCookie.CreateHeader(_stubbed.Services);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _stubbed?.Dispose();
        _factory?.Dispose();
        if (!string.IsNullOrEmpty(_databaseName)) await MySqlTestServer.DropDatabaseAsync(_databaseName);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, bool authenticated = true)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonHttp.Body(body);
        if (authenticated) request.Headers.Add("Cookie", _cookie);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task InvalidPath_Returns400_AndDoesNotCallUpstream()
    {
        using var response = await SendAsync(HttpMethod.Get, "/sc/Server/bad..path");

        var (status, json) = await JsonHttp.ReadAsync(response);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("非法的服务器路径", json.RootElement.GetProperty("message").GetString());
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task GetFromLocalServer_ForwardsToConfiguredBaseWithBearerToken()
    {
        using var response = await SendAsync(HttpMethod.Get, "/sc/Server/get/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("upstream-ok", await response.Content.ReadAsStringAsync());

        var captured = Assert.Single(_handler.Requests);
        Assert.Equal("GET", captured.Method);
        Assert.Equal(StubBase + "/server/status", captured.Uri);
        Assert.Equal("Bearer " + StubToken, captured.Authorization);
    }

    [Fact]
    public async Task PostFromServerPath_ForwardsBodyAndPassesThroughUpstreamStatus()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("conflict-body", Encoding.UTF8, "text/plain"),
        };

        using var response = await SendAsync(HttpMethod.Post, "/sc/Server/heartbeat", new { ping = "pong" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("conflict-body", await response.Content.ReadAsStringAsync());

        var captured = Assert.Single(_handler.Requests);
        Assert.Equal("POST", captured.Method);
        Assert.Equal(StubBase + "/server/heartbeat", captured.Uri);
        Assert.Contains("pong", captured.Body);
    }

    [Fact]
    public async Task UpstreamTransportFailure_Returns502WithOriginalMessage()
    {
        _handler.Responder = _ => throw new HttpRequestException("boom");

        using var response = await SendAsync(HttpMethod.Get, "/sc/Server/status");

        var (status, json) = await JsonHttp.ReadAsync(response);
        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("后端请求失败: boom", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Unauthenticated_Returns401_AndDoesNotCallUpstream()
    {
        using var response = await SendAsync(HttpMethod.Get, "/sc/Server/status", authenticated: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        Assert.Empty(_handler.Requests);
    }
}
