using System.Net;
using System.Text.Json;
using ClouderyApi.Modules.Cloudery.Infrastructure.Persistence;
using ClouderyApi.Modules.Cloudery.Domain;
using ClouderyApi.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// Cloudery 成员接口契约：公开读匿名可达、写操作仅管理员、响应直接使用 EF 实体 + MVC 默认 camelCase。
/// 这些字段名与顺序是站点前端依赖的兼容红线，Stage 2 抽取应用层时必须逐字保持。
/// 路由前缀 cloudery/members 全部小写；响应是 camelCase（不是 MHOP 的蛇形）。
/// </summary>
public sealed class ClouderyMembersContractTests : IntegrationTestBase
{
    private async Task SeedMemberAsync(string id, string name)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClouderyContext>();
        db.ClouderyMembers.Add(new Member
        {
            Id = id,
            Name = name,
            Position = "站长",
            Description = "一段简介",
            Socials = new List<Social> { new() { Type = "github", Link = "https://example.com/gh" } },
        });
        await db.SaveChangesAsync();
    }

    private static async Task AssertUnified404Async(HttpResponseMessage response)
    {
        // 统一错误体（见 docs/API-ERROR-SHAPE.md）：404 是 { detail }，不再走框架 ProblemDetails、也不再是空体，
        // 因为控制器已改成 ApiError.Result 一个出口。
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var (_, body) = await JsonHttp.ReadAsync(response);
        Assert.Equal("记录不存在", body.RootElement.GetProperty("detail").GetString());
    }

    private void SignInAsAdmin()
        => Client.DefaultRequestHeaders.Add(
            "Cookie",
            AuthCookie.CreateHeader(Factory.Services, AuthCookie.TestAdminCasdoorId));

    [Fact]
    public async Task List_is_anonymous_and_serializes_member_entity_in_camel_case()
    {
        await SeedMemberAsync("member-list", "张三");

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/cloudery/members");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        var item = Assert.Single(body.RootElement.EnumerateArray().ToArray());
        Assert.Equal(
            new[] { "id", "name", "position", "description", "socials" },
            item.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("member-list", item.GetProperty("id").GetString());
        Assert.Equal("张三", item.GetProperty("name").GetString());
        Assert.Equal("站长", item.GetProperty("position").GetString());
        Assert.Equal("一段简介", item.GetProperty("description").GetString());
        var social = Assert.Single(item.GetProperty("socials").EnumerateArray().ToArray());
        Assert.Equal(new[] { "type", "link" }, social.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("github", social.GetProperty("type").GetString());
        Assert.Equal("https://example.com/gh", social.GetProperty("link").GetString());
    }

    [Fact]
    public async Task Get_by_id_is_anonymous_and_unknown_id_returns_unified_404()
    {
        await SeedMemberAsync("member-one", "李四");

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/cloudery/members/member-one");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("李四", body.RootElement.GetProperty("name").GetString());

        var missing = await Client.GetAsync("/cloudery/members/does-not-exist");
        await AssertUnified404Async(missing);
    }

    [Fact]
    public async Task Put_requires_authentication_with_unified_401()
    {
        var response = await JsonHttp.SendAsync(
            Client, HttpMethod.Put, "/cloudery/members/whatever",
            new { name = "新名", position = "新职位" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        var (_, body) = await JsonHttp.ReadAsync(response);
        Assert.Equal("请先登录", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Put_unknown_id_as_admin_returns_unified_404()
    {
        SignInAsAdmin();

        var response = await JsonHttp.SendAsync(
            Client, HttpMethod.Put, "/cloudery/members/does-not-exist",
            new { name = "新名", position = "新职位" });

        await AssertUnified404Async(response);
    }

    [Fact]
    public async Task Post_as_admin_creates_member_and_returns_201_with_location()
    {
        SignInAsAdmin();

        var response = await JsonHttp.SendAsync(
            Client, HttpMethod.Post, "/cloudery/members",
            new
            {
                name = "王五",
                position = "副站长",
                description = "新增",
                socials = new[] { new { type = "x", link = "https://x.com/w" } },
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        // 201 的 Location 由 LocationUrlExtensions.LowercaseActionUrl 显式生成小写路径，与文档/前端拼写一致。
        Assert.StartsWith("/cloudery/members/", location!.AbsolutePath);

        var (_, created) = await JsonHttp.ReadAsync(response);
        var id = created.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal("王五", created.RootElement.GetProperty("name").GetString());

        var (status, body) = await JsonHttp.GetJsonAsync(Client, location.AbsolutePath);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("新增", body.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Put_updates_existing_member_as_admin()
    {
        await SeedMemberAsync("member-edit", "旧名");
        SignInAsAdmin();

        var response = await JsonHttp.SendAsync(
            Client, HttpMethod.Put, "/cloudery/members/member-edit",
            new { name = "新名", position = "新职位", description = (string?)null, socials = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());

        var (_, body) = await JsonHttp.GetJsonAsync(Client, "/cloudery/members/member-edit");
        Assert.Equal("新名", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("新职位", body.RootElement.GetProperty("position").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("description").ValueKind);
    }

    [Fact]
    public async Task Delete_unknown_id_as_admin_returns_unified_404_and_existing_returns_204()
    {
        await SeedMemberAsync("member-del", "删除我");
        SignInAsAdmin();

        var missing = await JsonHttp.SendAsync(Client, HttpMethod.Delete, "/cloudery/members/does-not-exist");
        await AssertUnified404Async(missing);

        var deleted = await JsonHttp.SendAsync(Client, HttpMethod.Delete, "/cloudery/members/member-del");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var gone = await Client.GetAsync("/cloudery/members/member-del");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Post_with_invalid_dto_returns_unified_validation_shape()
    {
        SignInAsAdmin();

        var response = await JsonHttp.SendAsync(
            Client, HttpMethod.Post, "/cloudery/members", new { position = "缺名字" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var (_, body) = await JsonHttp.ReadAsync(response);
        // [ApiController] 的自动校验 400 也走 ApiError：{ detail, errors }，不再有 ProblemDetails。
        Assert.Equal("参数校验失败", body.RootElement.GetProperty("detail").GetString());
        Assert.False(body.RootElement.TryGetProperty("success", out _));
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("Name", out _));
    }
}
