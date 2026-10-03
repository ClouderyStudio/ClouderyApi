using System.Net;
using System.Text.Json;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// 后台鉴权阶梯：无令牌 401 / 普通用户 403 / 管理员 403（缺模块权限）/ 超级管理员 200。
/// 角色与权限每次请求都从库里重读，令牌里的角色声明不参与判定。
/// </summary>
public sealed class MhopAdminAuthorizationTests : IntegrationTestBase
{
    private async Task<string> SuperAdminTokenAsync()
    {
        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/auth/login",
            new
            {
                username = ClouderyApiFactory.TestSeedAdminUsername,
                password = ClouderyApiFactory.TestSeedAdminPassword,
            });
        Assert.Equal(HttpStatusCode.OK, status);
        return body.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<string> UserTokenWithRoleAsync(string username, string role)
    {
        var (registered, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/auth/register",
            new { username, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, registered);
        var token = body.RootElement.GetProperty("access_token").GetString()!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
        var user = await db.MhopUsers.SingleAsync(u => u.Username == username);
        user.Role = role;
        await db.SaveChangesAsync();

        return token;
    }

    [Fact]
    public async Task Admin_stats_ladder_is_401_then_403_then_200()
    {
        var (anon, anonBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/stats");
        Assert.Equal(HttpStatusCode.Unauthorized, anon);
        Assert.Equal("请先登录", anonBody.RootElement.GetProperty("detail").GetString());

        var userToken = await UserTokenWithRoleAsync("admin_stats_user", "user");
        var (forbidden, forbiddenBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/stats", userToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
        Assert.Equal("需要管理员权限", forbiddenBody.RootElement.GetProperty("detail").GetString());

        var adminToken = await SuperAdminTokenAsync();
        var (ok, okBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/stats", adminToken);
        Assert.Equal(HttpStatusCode.OK, ok);
        Assert.True(okBody.RootElement.GetProperty("users").GetInt32() >= 2);
        Assert.Equal(JsonValueKind.Number, okBody.RootElement.GetProperty("pending_posts").ValueKind);
    }

    [Fact]
    public async Task Invalid_token_is_treated_as_anonymous()
    {
        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/stats", "not-a-real-jwt");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("请先登录", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Permission_scoped_endpoint_rejects_admin_without_that_module()
    {
        var adminToken = await UserTokenWithRoleAsync("admin_no_review", "admin");

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/posts", adminToken);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("没有「内容审核」模块权限", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Super_only_endpoint_rejects_plain_admin()
    {
        var adminToken = await UserTokenWithRoleAsync("admin_not_super", "admin");

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/users/1/permissions", adminToken);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("仅超级管理员可执行该操作", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Superadmin_can_read_review_queue()
    {
        var adminToken = await SuperAdminTokenAsync();

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/posts", adminToken);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
    }
}
