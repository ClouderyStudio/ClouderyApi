using System.Net;
using System.Text.Json;
using ClouderyApi.Tests.TestSupport;

namespace ClouderyApi.Tests;

/// <summary>
/// Zhuxs 三张表的写路径契约（C.6 补充覆盖）：白名单 / 赛季 / 入服申请的
/// POST 201 + Location 小写、PUT 204、DELETE 204、重复删除 404，
/// 以及 [AdminOnly] 的非管理员 403。读路径的匿名契约见 ZhuxsContractTests。
/// </summary>
public sealed class ZhuxsWriteContractTests : IntegrationTestBase
{
    private void SignInAsAdmin()
        => Client.DefaultRequestHeaders.Add(
            "Cookie",
            AuthCookie.CreateHeader(Factory.Services, AuthCookie.TestAdminCasdoorId));

    private void SignInAsPlainUser()
        => Client.DefaultRequestHeaders.Add("Cookie", AuthCookie.CreateHeader(Factory.Services, "plain-user"));

    private static object TermBody(string description) => new
    {
        recordDate = "2026-02-03",
        description,
        information = new { name = "Cloudery", from = "1.0.0", version = "1.0.0", modcount = 3, playercount = 7 },
        files = new[] { new { filename = "release.zip", size = 1.5f, unit = "MB" } },
    };

    private static void AssertUnified(JsonDocument body, string detail)
    {
        Assert.Equal(new[] { "detail" }, body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(detail, body.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>POST 201 的 Location 必须是小写路径（LocationUrlExtensions.LowercaseActionUrl）。</summary>
    private static void AssertLowercaseLocation(HttpResponseMessage response, string prefix)
    {
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.StartsWith(prefix, location!.AbsolutePath);
    }

    [Fact]
    public async Task Whitelists_crud_round_trip_as_admin()
    {
        SignInAsAdmin();

        var created = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/zhuxs/whitelists", new { code = "INVITE-001" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AssertLowercaseLocation(created, "/zhuxs/whitelists/");
        var (_, createdBody) = await JsonHttp.ReadAsync(created);
        var id = createdBody.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("INVITE-001", createdBody.RootElement.GetProperty("code").GetString());

        var (listStatus, list) = await JsonHttp.GetJsonAsync(Client, "/zhuxs/whitelists");
        Assert.Equal(HttpStatusCode.OK, listStatus);
        Assert.Contains(list.RootElement.EnumerateArray(), e => e.GetProperty("id").GetString() == id);

        var (findStatus, found) = await JsonHttp.GetJsonAsync(Client, $"/zhuxs/whitelists/{id}");
        Assert.Equal(HttpStatusCode.OK, findStatus);
        Assert.Equal("INVITE-001", found.RootElement.GetProperty("code").GetString());

        var deleted = await JsonHttp.SendAsync(Client, HttpMethod.Delete, $"/zhuxs/whitelists/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var again = await JsonHttp.SendAsync(Client, HttpMethod.Delete, $"/zhuxs/whitelists/{id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        var (_, againBody) = await JsonHttp.ReadAsync(again);
        AssertUnified(againBody, "记录不存在");
    }

    [Fact]
    public async Task Terms_crud_round_trip_as_admin()
    {
        SignInAsAdmin();

        var created = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/zhuxs/terms", TermBody("赛季一"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AssertLowercaseLocation(created, "/zhuxs/terms/");
        var (_, createdBody) = await JsonHttp.ReadAsync(created);
        var id = createdBody.RootElement.GetProperty("id").GetString()!;

        // 读接口匿名可访问（这里带着 Cookie 也无妨）——PUT 生效后立刻可见。
        var put = await JsonHttp.SendAsync(Client, HttpMethod.Put, $"/zhuxs/terms/{id}", TermBody("赛季一（改）"));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var (_, after) = await JsonHttp.GetJsonAsync(Client, $"/zhuxs/terms/{id}");
        Assert.Equal("赛季一（改）", after.RootElement.GetProperty("description").GetString());

        var putUnknown = await JsonHttp.PutJsonAsync(Client, "/zhuxs/terms/missing", TermBody("不存在"));
        Assert.Equal(HttpStatusCode.NotFound, putUnknown.Status);
        AssertUnified(putUnknown.Body, "记录不存在");

        var deleted = await JsonHttp.SendAsync(Client, HttpMethod.Delete, $"/zhuxs/terms/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var again = await JsonHttp.SendAsync(Client, HttpMethod.Delete, $"/zhuxs/terms/{id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Applications_crud_round_trip_and_post_cannot_self_approve()
    {
        SignInAsAdmin();

        // POST 显式带 passed=true 也必须被忽略（由管理员在 PUT 阶段审核）。
        var created = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/zhuxs/applications", new
        {
            sharables = new[] { new { question = "为什么想加入", answer = "因为想玩" } },
            passed = true,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AssertLowercaseLocation(created, "/zhuxs/applications/");
        var (_, createdBody) = await JsonHttp.ReadAsync(created);
        var id = createdBody.RootElement.GetProperty("id").GetString()!;
        Assert.False(createdBody.RootElement.GetProperty("passed").GetBoolean());

        var put = await JsonHttp.SendAsync(Client, HttpMethod.Put, $"/zhuxs/applications/{id}", new { passed = true });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var (_, after) = await JsonHttp.GetJsonAsync(Client, $"/zhuxs/applications/{id}");
        Assert.True(after.RootElement.GetProperty("passed").GetBoolean());

        var putUnknown = await JsonHttp.PutJsonAsync(Client, "/zhuxs/applications/missing", new { passed = false });
        Assert.Equal(HttpStatusCode.NotFound, putUnknown.Status);

        var deleted = await JsonHttp.SendAsync(Client, HttpMethod.Delete, $"/zhuxs/applications/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var again = await JsonHttp.SendAsync(Client, HttpMethod.Delete, $"/zhuxs/applications/{id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Zhuxs_writes_reject_non_admin_with_403()
    {
        SignInAsPlainUser();

        var cases = new (string Path, object Body, string Method)[]
        {
            ("/zhuxs/whitelists", new { code = "INVITE-403" }, "POST"),
            ("/zhuxs/terms", TermBody("赛季一"), "POST"),
            ("/zhuxs/applications", new { sharables = Array.Empty<object>() }, "POST"),
        };

        foreach (var (path, body, method) in cases)
        {
            var response = await JsonHttp.SendAsync(Client, new HttpMethod(method), path, body);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var (_, responseBody) = await JsonHttp.ReadAsync(response);
            AssertUnified(responseBody, "无管理员权限，操作被拒绝");
        }
    }
}
