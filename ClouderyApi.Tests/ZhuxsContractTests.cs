using System.Net;
using System.Text;
using System.Text.Json;
using ClouderyApi.Data;
using ClouderyApi.Modules.Zhuxs.Domain;
using ClouderyApi.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// Zhuxs 家族契约：读接口匿名可访问、写接口走 Cookie 方案（未登录是 302 跳登录页而非 401）、
/// 响应直接用 EF 实体 + 默认 camelCase 序列化。
/// </summary>
public sealed class ZhuxsContractTests : IntegrationTestBase
{
    [Fact]
    public async Task Terms_list_is_anonymous_and_serializes_entities_in_camel_case()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClouderyApiContext>();
            db.ZhuxsTerms.Add(new Term
            {
                Id = Guid.NewGuid().ToString("N"),
                RecordDate = "2026-01-02",
                Description = "契约测试条目",
                Information = new TermInfo
                {
                    Name = "Cloudery",
                    From = "1.0.0",
                    To = null,
                    Version = "1.0.0",
                    Modcount = 3,
                    Playercount = 7,
                },
                Files = [new TermFile { Filename = "release.zip", Size = 1.5f, Unit = "MB" }],
            });
            await db.SaveChangesAsync();
        }

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/zhuxs/terms");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        var item = Assert.Single(body.RootElement.EnumerateArray().ToArray());
        Assert.Equal("契约测试条目", item.GetProperty("description").GetString());
        Assert.Equal("2026-01-02", item.GetProperty("recordDate").GetString());
        Assert.Equal("Cloudery", item.GetProperty("information").GetProperty("name").GetString());
        Assert.Equal(3, item.GetProperty("information").GetProperty("modcount").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("information").GetProperty("to").ValueKind);
        var file = Assert.Single(item.GetProperty("files").EnumerateArray().ToArray());
        Assert.Equal("release.zip", file.GetProperty("filename").GetString());
        Assert.False(item.TryGetProperty("RecordDate", out _));
    }

    [Fact]
    public async Task Unknown_term_returns_404()
    {
        var response = await Client.GetAsync("/zhuxs/terms/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Applications_list_is_anonymous_and_serializes_entities_in_camel_case()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClouderyApiContext>();
            db.ZhuxsApplications.Add(new Application
            {
                Id = Guid.NewGuid().ToString("N"),
                Passed = false,
                SubmissionDate = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                Sharables = [new Sharable { Question = "问题", Answer = "答案" }],
            });
            await db.SaveChangesAsync();
        }

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/zhuxs/applications");

        Assert.Equal(HttpStatusCode.OK, status);
        var item = Assert.Single(body.RootElement.EnumerateArray().ToArray());
        Assert.False(item.GetProperty("passed").GetBoolean());
        Assert.False(item.TryGetProperty("SubmissionDate", out _));
        var sharable = Assert.Single(item.GetProperty("sharables").EnumerateArray().ToArray());
        Assert.Equal("问题", sharable.GetProperty("question").GetString());
    }

    /// <summary>
    /// 只挂 [Authorize] 的路由：默认认证方案是 Cookies，但默认 Challenge 方案是 Casdoor 包注册的
    /// JwtBearer（scheme "Bearer"），所以未登录是 401 + WWW-Authenticate: Bearer 的空体，不是 302 跳登录页。
    /// </summary>
    [Fact]
    public async Task Whitelists_list_requires_authentication()
    {
        var response = await Client.GetAsync("/zhuxs/whitelists");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Writing_terms_requires_authentication()
    {
        var response = await Client.PostAsync("/zhuxs/terms",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Writing_applications_requires_authentication()
    {
        var response = await Client.PostAsync("/zhuxs/applications",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Server_proxy_requires_authentication()
    {
        var response = await Client.GetAsync("/sc/Server/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }
}
