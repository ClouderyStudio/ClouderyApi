using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClouderyApi.Tests.TestSupport;

/// <summary>
/// 只服务 OIDC 发现文档与 JWKS 的 loopback Kestrel 桩（契约测试 C.6③）。
/// <para>
/// Casdoor.Client 在换 token 之前会先用 IdentityModel 的 <c>ConfigurationManager</c> 抓发现文档
/// （<c>CasdoorOptions.Validate()</c> 默认 <c>Protocols.AutoDiscovery = true</c>），而那份文档由
/// <c>HttpDocumentRetriever</c> 自建的 HttpClient 发出：它既不经过 DI 的 "Casdoor" 命名客户端，
/// 也没有可注入的钩子，所以只能用真实端口来应答。
/// </para>
/// <para>
/// token 端点仍由测试里的 <c>StubHandler</c> 应答（那一步确实走命名客户端），
/// 发现文档里的 <c>token_endpoint</c> 因此指向本桩地址 —— StubHandler 只看路径，不看主机。
/// </para>
/// </summary>
public sealed class CasdoorDiscoveryStubServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private CasdoorDiscoveryStubServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    /// <summary>桩的根地址（形如 <c>http://127.0.0.1:52341</c>，无尾斜杠）。</summary>
    public string BaseUrl { get; }

    /// <summary>启动桩并等它就绪；调用方拿到 <see cref="BaseUrl"/> 后才能配置 Casdoor:Endpoint。</summary>
    public static CasdoorDiscoveryStubServer Start()
    {
        WebApplication? app = null;
        var baseUrl = string.Empty;

        // 在专用线程上同步等待异步启动：xUnit 为异步测试方法装了同步上下文，
        // 在当前线程上 .GetAwaiter().GetResult() 等 WebApplication.StartAsync 有死锁风险。
        RunOnDedicatedThread(() =>
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var built = builder.Build();
            built.MapGet("/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object?>
            {
                // issuer 与 authority 一致，ConfigurationManager 才会认这份文档。
                ["issuer"] = baseUrl,
                ["authorization_endpoint"] = baseUrl + "/login/oauth/authorize",
                ["token_endpoint"] = baseUrl + "/api/login/oauth/access_token",
                ["userinfo_endpoint"] = baseUrl + "/api/userinfo",
                ["jwks_uri"] = baseUrl + "/.well-known/jwks",
                ["response_types_supported"] = new[] { "code" },
                ["subject_types_supported"] = new[] { "public" },
                ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
                ["scopes_supported"] = new[] { "openid", "profile", "email" },
            }));
            // Validate() 里火忘的 LoadRemoteJwtPublicKeyAsync 会顺手抓 JWKS：给个空集，别让它挂住。
            built.MapGet("/.well-known/jwks", () => Results.Json(new Dictionary<string, object?>
            {
                ["keys"] = Array.Empty<object>(),
            }));

            built.Start();
            baseUrl = built.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First()
                .TrimEnd('/');
            app = built;
        });

        return new CasdoorDiscoveryStubServer(app!, baseUrl);
    }

    public async ValueTask DisposeAsync()
    {
        var app = _app;
        await Task.Run(async () =>
        {
            await app.StopAsync();
            await app.DisposeAsync();
        });
    }

    private static void RunOnDedicatedThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "casdoor-discovery-stub",
        };

        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("OIDC 发现文档桩启动失败。", failure);
        }
    }
}
