using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClouderyApi.Modules.Identity.Domain;
using ClouderyApi.Modules.Identity.Infrastructure.Persistence;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Infrastructure.Persistence;
using ClouderyApi.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Tests;

/// <summary>
/// 隐私插件（口令访问 / 指定人员可见）的契约测试。
///
/// 这套断言锁住的是四条最容易悄悄坏掉的不变量：
///   1. **不进公开目录** —— 搜索 / 精选 / 最近都拿不到隐私插件，否则「隐私」名存实亡；
///   2. **脱敏在服务端** —— 无权访问者拿到的外壳里 description / readme / gallery / versions 全空，
///      且返回 **403 而不是 404**（插件本就该能被链接直达，404 会让作者没法分享）；
///   3. **下载入口独立判定** —— 拿到 versionId 直接打下载端点不能绕过，
///      否则「详情页挡住」形同虚设；
///   4. **改隐私不触发重新审核** —— 否则作者换一下口令，已发布的插件当场从目录消失。
///
/// 另外还钉住了「口令只存哈希」「白名单整体替换」「换口令使旧令牌失效」这三条安全语义。
/// </summary>
public sealed class ScforgePrivacyAccessContractTests : IntegrationTestBase
{
    private static readonly Guid Author = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Granted = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Outsider = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AdminUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>测试用访问口令（≥ 6 位，够短以便在断言里出现）。</summary>
    private const string Password = "sw-secret-2026";

    /* ---------------- HTTP 辅助 ---------------- */

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content = null,
        Guid? userId = null,
        string? casdoorId = null,
        string? accessToken = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Content = content;
        if (userId is not null || casdoorId is not null)
        {
            request.Headers.Add("Cookie", AuthCookie.CreateHeader(Factory.Services, casdoorId, userId));
        }

        if (accessToken is not null) request.Headers.Add(ScforgeAccessAppService.TokenHeader, accessToken);

        return await Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> SendAsSuperAsync(HttpMethod method, string path, HttpContent? content = null) =>
        SendAsync(method, path, content, AdminUserId, AuthCookie.TestAdminCasdoorId);

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static byte[] BuildAssembly(string name, string version)
    {
        var bytes = new List<byte>();
        bytes.AddRange("MZ"u8.ToArray());
        bytes.AddRange(new byte[0x3C]);
        bytes.AddRange(BitConverter.GetBytes(0x80));
        bytes.AddRange(new byte[0x80 - 0x40]);
        bytes.AddRange("PE\0\0"u8.ToArray());
        bytes.AddRange(Encoding.UTF8.GetBytes($"{name}@{version}"));
        bytes.AddRange(new byte[256]);
        return bytes.ToArray();
    }

    private static MultipartFormDataContent CreateForm(
        string name,
        string slug,
        string version = "1.0.0",
        string? accessMode = null,
        string? accessPassword = null,
        string? accessHint = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(BuildAssembly(name, version));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "package", "plugin.dll");

        form.Add(new StringContent(name), "name");
        form.Add(new StringContent(slug), "slug");
        form.Add(new StringContent("plugin"), "kind");
        form.Add(new StringContent($"{name} 的一句话简介"), "summary");
        form.Add(new StringContent($"{name} 的详细描述"), "description");
        form.Add(new StringContent("utilities"), "category");
        form.Add(new StringContent("x26.07.01"), "gameVersion");
        form.Add(new StringContent("survival"), "tags");
        form.Add(new StringContent(version), "version");
        form.Add(new StringContent("release"), "channel");
        form.Add(new StringContent("首个版本"), "changelog");
        form.Add(new StringContent("x26.07.01"), "gameVersions");

        if (accessMode is not null) form.Add(new StringContent(accessMode), "accessMode");
        if (accessPassword is not null) form.Add(new StringContent(accessPassword), "accessPassword");
        if (accessHint is not null) form.Add(new StringContent(accessHint), "accessHint");

        return form;
    }

    private async Task<JsonElement> CreateAsync(
        string name,
        string slug,
        string? accessMode = null,
        string? accessPassword = null,
        string? accessHint = null)
    {
        var response = await SendAsync(
            HttpMethod.Post,
            "/scforge/addons",
            CreateForm(name, slug, "1.0.0", accessMode, accessPassword, accessHint),
            Author);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("addon").Clone();
    }

    private async Task ApprovePluginAsync(string pluginId)
    {
        var response = await SendAsSuperAsync(
            HttpMethod.Post,
            "/scforge/admin/addons/" + pluginId + "/review",
            JsonHttp.Body(new { approve = true, note = (string?)null }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task ApproveVersionAsync(string versionId)
    {
        var response = await SendAsSuperAsync(
            HttpMethod.Post,
            "/scforge/admin/versions/" + versionId + "/review",
            JsonHttp.Body(new { approve = true, note = (string?)null }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>创建 + 插件与首个版本都审过，让它对公众可见。</summary>
    private async Task<JsonElement> PublishAsync(
        string name,
        string slug,
        string? accessMode = null,
        string? password = null,
        string? hint = null)
    {
        var plugin = await CreateAsync(name, slug, accessMode, password, hint);
        await ApprovePluginAsync(plugin.GetProperty("id").GetString()!);
        await ApproveVersionAsync(plugin.GetProperty("versions")[0].GetProperty("id").GetString()!);
        return plugin;
    }

    /// <summary>编辑资料并在需要时带上隐私字段（字段「出现即生效」）。</summary>
    private static MultipartFormDataContent PrivacyEditForm(
        string summary = "编辑后的简介",
        string? accessMode = null,
        string? accessPassword = null,
        string? accessHint = null)
    {
        var form = EditForm(summary);
        if (accessMode is not null) AddField(form, "accessMode", accessMode);
        if (accessPassword is not null) AddField(form, "accessPassword", accessPassword);
        if (accessHint is not null) AddField(form, "accessHint", accessHint);
        return form;
    }

    /// <summary>作者编辑资料用的 multipart。隐私字段按「出现即生效」语义逐个传入。</summary>
    private static MultipartFormDataContent EditForm(string summary = "编辑后的简介")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(summary), "summary");
        form.Add(new StringContent("编辑后的详细描述"), "description");
        form.Add(new StringContent("编辑后的 README"), "readme");
        return form;
    }

    private static void AddField(MultipartFormDataContent form, string name, string value) =>
        form.Add(new StringContent(value), name);

    private async Task<string> UnlockAsync(string pluginId, string? password)
    {
        var response = await SendAsync(
            HttpMethod.Post,
            $"/scforge/addons/{pluginId}/access/unlock",
            JsonHttp.Body(new { password }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private async Task<Guid> SeedUserAsync(string username, Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Users.Add(new User
        {
            Id = userId,
            Username = username,
            Email = username + "@example.com",
            CasdoorId = "casdoor-" + username,
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static string[] IdsOf(JsonElement items) =>
        items.EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToArray();

    /* ---------------- 1. 不进公开目录 ---------------- */

    [Fact]
    public async Task Privacy_addons_are_absent_from_search_featured_and_recent()
    {
        var publicAddon = await PublishAsync("公开插件", "public-addon");
        var hidden = await PublishAsync("隐私插件", "hidden-addon", "password", Password);
        Assert.NotEqual(
            publicAddon.GetProperty("id").GetString(),
            hidden.GetProperty("id").GetString());

        var search = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/scforge/addons?page=1&pageSize=50"));
        var searchIds = IdsOf(search.RootElement.GetProperty("items"));
        Assert.Contains(publicAddon.GetProperty("id").GetString(), searchIds);
        Assert.DoesNotContain(hidden.GetProperty("id").GetString(), searchIds);

        foreach (var path in new[] { "/scforge/addons/featured?limit=20", "/scforge/addons/recent?limit=20" })
        {
            var feed = await ReadJsonAsync(await SendAsync(HttpMethod.Get, path));
            Assert.DoesNotContain(
                hidden.GetProperty("id").GetString(),
                IdsOf(feed.RootElement.GetProperty("items")));
        }
    }

    [Fact]
    public async Task Privacy_addon_is_still_reachable_by_slug_directly()
    {
        // 隐私插件不进目录，但作者需要能把链接发出去 ——
        // 因此详情返回 200 + 脱敏外壳（hasAccess=false），**不是 404**：
        // 404 会让作者没法分享链接，访客也分不清「插件没了」还是「你没权限」。
        var plugin = await PublishAsync("私密插件", "private-slug", "password", Password);
        var slug = plugin.GetProperty("slug").GetString()!;

        var response = await SendAsync(HttpMethod.Get, "/scforge/addons/" + slug);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var addon = body.RootElement.GetProperty("addon");

        Assert.Equal("私密插件", addon.GetProperty("name").GetString());
        Assert.False(addon.GetProperty("hasAccess").GetBoolean());
        Assert.Equal(string.Empty, addon.GetProperty("description").GetString());
        Assert.Equal(0, addon.GetProperty("versions").GetArrayLength());
    }

    /* ---------------- 2. 服务端脱敏 ---------------- */

    [Fact]
    public async Task Unauthorised_detail_returns_masked_shell_without_content()
    {
        var plugin = await PublishAsync("脱敏插件", "masked-addon", "password", Password);
        var id = plugin.GetProperty("id").GetString()!;
        var slug = plugin.GetProperty("slug").GetString()!;

        var response = await SendAsync(HttpMethod.Get, "/scforge/addons/" + id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var addon = body.RootElement.GetProperty("addon");

        // 标志位：前端据此渲染解锁门。
        Assert.False(addon.GetProperty("hasAccess").GetBoolean());
        Assert.False(addon.GetProperty("accessUnlocked").GetBoolean());
        Assert.Equal("password", addon.GetProperty("accessMode").GetString());

        // 外壳：够渲染「这是个受保护的插件」。
        Assert.Equal("脱敏插件", addon.GetProperty("name").GetString());
        Assert.NotEqual(string.Empty, addon.GetProperty("summary").GetString());
        Assert.Equal(0, addon.GetProperty("versions").GetArrayLength());
        Assert.Equal(0, addon.GetProperty("gallery").GetArrayLength());

        // 正文与链接：一个字节都不能下发 —— 靠前端隐藏等于内容已经发到浏览器了。
        Assert.Equal(string.Empty, addon.GetProperty("description").GetString());
        Assert.Equal(string.Empty, addon.GetProperty("readme").GetString());
        Assert.Equal(JsonValueKind.Null, addon.GetProperty("sourceUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, addon.GetProperty("issuesUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, addon.GetProperty("licenseUrl").ValueKind);

        // slug 直达与 id 直达一致（两条路径都要脱敏）。
        var bySlug = await SendAsync(HttpMethod.Get, "/scforge/addons/" + slug);
        using var slugBody = await ReadJsonAsync(bySlug);
        Assert.Equal(
            string.Empty,
            slugBody.RootElement.GetProperty("addon").GetProperty("description").GetString());
    }

    [Fact]
    public async Task Author_and_admin_see_privacy_addon_fully_without_unlocking()
    {
        var plugin = await PublishAsync("作者可见", "author-view", "password", Password);
        var id = plugin.GetProperty("id").GetString()!;

        foreach (var (label, response) in new[]
                 {
                     ("作者", await SendAsync(HttpMethod.Get, "/scforge/addons/" + id, null, Author)),
                     ("管理员", await SendAsSuperAsync(HttpMethod.Get, "/scforge/addons/" + id)),
                 })
        {
            using var body = await ReadJsonAsync(response);
            var addon = body.RootElement.GetProperty("addon");
            Assert.True(addon.GetProperty("hasAccess").GetBoolean(), label + " 应当有访问权");
            Assert.NotEqual(string.Empty, addon.GetProperty("description").GetString());
            Assert.Equal(1, addon.GetProperty("versions").GetArrayLength());
        }
    }

    /* ---------------- 3. 口令解锁 ---------------- */

    [Fact]
    public async Task Correct_password_issues_token_that_unlocks_detail()
    {
        var plugin = await PublishAsync("口令插件", "password-addon", "password", Password, "去群里问作者");
        var id = plugin.GetProperty("id").GetString()!;

        var token = await UnlockAsync(id, Password);

        var response = await SendAsync(HttpMethod.Get, "/scforge/addons/" + id, null, null, null, token);
        using var body = await ReadJsonAsync(response);
        var addon = body.RootElement.GetProperty("addon");
        Assert.True(addon.GetProperty("hasAccess").GetBoolean());
        Assert.True(addon.GetProperty("accessUnlocked").GetBoolean());
        Assert.NotEqual(string.Empty, addon.GetProperty("description").GetString());
        Assert.Equal(1, addon.GetProperty("versions").GetArrayLength());
    }

    [Fact]
    public async Task Wrong_password_is_rejected_with_403()
    {
        var plugin = await PublishAsync("口令插件", "wrong-pwd", "password", Password);
        var id = plugin.GetProperty("id").GetString()!;

        var response = await SendAsync(
            HttpMethod.Post,
            $"/scforge/addons/{id}/access/unlock",
            JsonHttp.Body(new { password = "not-the-password" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unlock_on_public_addon_is_rejected()
    {
        // 公开插件没有口令概念：给它一个解锁端点等于开了个「随便输点什么就发令牌」的后门。
        var plugin = await PublishAsync("公开插件", "public-unlock", null);
        var id = plugin.GetProperty("id").GetString()!;

        var response = await SendAsync(
            HttpMethod.Post,
            $"/scforge/addons/{id}/access/unlock",
            JsonHttp.Body(new { password = Password }));

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden,
            $"公开插件解锁应被拒，实际 {response.StatusCode}");
    }

    [Fact]
    public async Task Forged_or_tampered_token_does_not_unlock()
    {
        var plugin = await PublishAsync("口令插件", "forged-token", "password", Password);
        var id = plugin.GetProperty("id").GetString()!;
        var token = await UnlockAsync(id, Password);

        // 篡改签名段。
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{new string('A', parts[2].Length)}";

        foreach (var candidate in new[] { tampered, "garbage", token + "x" })
        {
            var response = await SendAsync(HttpMethod.Get, "/scforge/addons/" + id, null, null, null, candidate);
            using var body = await ReadJsonAsync(response);
            Assert.False(
                body.RootElement.GetProperty("addon").GetProperty("hasAccess").GetBoolean(),
                $"伪造令牌 {candidate[..Math.Min(16, candidate.Length)]} 不应解锁");
        }
    }

    [Fact]
    public async Task Changing_password_invalidates_previously_issued_tokens()
    {
        // 令牌签名里带口令指纹：不这么做的话，换口令后旧令牌会一直有效到过期。
        var plugin = await PublishAsync("口令插件", "rotate-pwd", "password", Password);
        var id = plugin.GetProperty("id").GetString()!;
        var oldToken = await UnlockAsync(id, Password);

        var rotated = await SendAsync(
            HttpMethod.Put,
            "/scforge/addons/" + id,
            PrivacyEditForm(accessMode: "password", accessPassword: "new-secret-2026"),
            Author);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        var afterRotation = await SendAsync(HttpMethod.Get, "/scforge/addons/" + id, null, null, null, oldToken);
        using var body = await ReadJsonAsync(afterRotation);
        Assert.False(
            body.RootElement.GetProperty("addon").GetProperty("hasAccess").GetBoolean(),
            "换口令后旧令牌必须失效");

        // 新口令可解锁。
        var newToken = await UnlockAsync(id, "new-secret-2026");
        var withNew = await SendAsync(HttpMethod.Get, "/scforge/addons/" + id, null, null, null, newToken);
        using var newBody = await ReadJsonAsync(withNew);
        Assert.True(newBody.RootElement.GetProperty("addon").GetProperty("hasAccess").GetBoolean());
    }

    [Fact]
    public async Task Password_mode_requires_a_password_on_first_switch()
    {
        // 没口令的口令插件 = 谁都进不去的死资源，必须在入口就拒。
        var plugin = await PublishAsync("公开插件", "switch-to-password");
        var id = plugin.GetProperty("id").GetString()!;

        var response = await SendAsync(
            HttpMethod.Put,
            "/scforge/addons/" + id,
            PrivacyEditForm(accessMode: "password"),
            Author);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Short_password_is_rejected()
    {
        var response = await SendAsync(
            HttpMethod.Post,
            "/scforge/addons",
            CreateForm("短口令插件", "short-pwd", "1.0.0", "password", "123"),
            Author);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /* ---------------- 4. 下载入口独立判定 ---------------- */

    [Fact]
    public async Task Download_endpoint_enforces_privacy_independently()
    {
        // 这是最关键的一条：详情页挡住不算挡住，versionId 一旦泄露就能直连下载。
        var plugin = await PublishAsync("隐私插件", "download-guard", "password", Password);
        var versionId = plugin.GetProperty("versions")[0].GetProperty("id").GetString()!;
        var path = $"/scforge/versions/{versionId}/download";

        var anonymous = await SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);

        var wrongToken = await SendAsync(HttpMethod.Get, path, null, null, null, "not-a-token");
        Assert.Equal(HttpStatusCode.Forbidden, wrongToken.StatusCode);

        // 未解锁的登录用户同样被拒。
        var loggedIn = await SendAsync(HttpMethod.Get, path, null, Outsider);
        Assert.Equal(HttpStatusCode.Forbidden, loggedIn.StatusCode);

        // 正确令牌放行。
        var token = await UnlockAsync(plugin.GetProperty("id").GetString()!, Password);
        var allowed = await SendAsync(HttpMethod.Get, path, null, null, null, token);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    /* ---------------- 5. 白名单 ---------------- */

    [Fact]
    public async Task Whitelist_admits_only_listed_users()
    {
        await SeedUserAsync("granted", Granted);
        await SeedUserAsync("outsider", Outsider);

        var plugin = await PublishAsync("白名单插件", "whitelist-addon", "whitelist");
        var id = plugin.GetProperty("id").GetString()!;

        // 匿名与未授权用户都拿不到内容。
        foreach (var response in new[]
                 {
                     await SendAsync(HttpMethod.Get, "/scforge/addons/" + id),
                     await SendAsync(HttpMethod.Get, "/scforge/addons/" + id, null, Outsider),
                 })
        {
            using var body = await ReadJsonAsync(response);
            Assert.False(body.RootElement.GetProperty("addon").GetProperty("hasAccess").GetBoolean());
        }

        // 名单为空时写入 = 无效操作：把范围整体替换掉。
        var write = await SendAsync(
            HttpMethod.Put,
            $"/scforge/addons/{id}/access/grants",
            JsonHttp.Body(new { userIds = new[] { Granted.ToString() } }),
            Author);
        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        using var written = await ReadJsonAsync(write);
        var ids = written.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("userId").GetString()!).ToArray();
        Assert.Equal(new[] { Granted.ToString() }, ids);

        // 名单内的人放行，名单外的人仍拒。
        var inside = await SendAsync(HttpMethod.Get, "/scforge/addons/" + id, null, Granted);
        using var insideBody = await ReadJsonAsync(inside);
        Assert.True(insideBody.RootElement.GetProperty("addon").GetProperty("hasAccess").GetBoolean());

        var outside = await SendAsync(HttpMethod.Get, "/scforge/addons/" + id, null, Outsider);
        using var outsideBody = await ReadJsonAsync(outside);
        Assert.False(outsideBody.RootElement.GetProperty("addon").GetProperty("hasAccess").GetBoolean());
    }

    [Fact]
    public async Task Whitelist_write_is_author_only()
    {
        var plugin = await PublishAsync("白名单插件", "grants-auth", "whitelist");
        var id = plugin.GetProperty("id").GetString()!;

        var anonymous = await SendAsync(
            HttpMethod.Put,
            $"/scforge/addons/{id}/access/grants",
            JsonHttp.Body(new { userIds = new[] { Granted.ToString() } }));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // 非作者、无内容管理权限的普通用户也不该能改别人的名单。
        var other = await SendAsync(
            HttpMethod.Put,
            $"/scforge/addons/{id}/access/grants",
            JsonHttp.Body(new { userIds = new[] { Outsider.ToString() } }),
            Outsider);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    }

    [Fact]
    public async Task Whitelist_grants_are_cleared_when_switching_away()
    {
        await SeedUserAsync("granted", Granted);
        var plugin = await PublishAsync("白名单插件", "grants-clear", "whitelist");
        var id = plugin.GetProperty("id").GetString()!;

        await SendAsync(
            HttpMethod.Put,
            $"/scforge/addons/{id}/access/grants",
            JsonHttp.Body(new { userIds = new[] { Granted.ToString() } }),
            Author);

        // 切回公开：名单必须清掉，否则「切回来时授权还在」是个让人误解的状态。
        var response = await SendAsync(
            HttpMethod.Put,
            "/scforge/addons/" + id,
            PrivacyEditForm(accessMode: "public"),
            Author);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScforgeDbContext>();
        Assert.Empty(await db.ScforgeAccessGrants.AsNoTracking().Where(g => g.PluginId == Guid.Parse(id)).ToListAsync());
    }

    [Fact]
    public async Task Author_is_never_listed_in_their_own_whitelist()
    {
        // 作者本来就放行，把自己塞进名单只是让名单看起来有内容。
        var plugin = await PublishAsync("白名单插件", "no-self", "whitelist");
        var id = plugin.GetProperty("id").GetString()!;

        var response = await SendAsync(
            HttpMethod.Put,
            $"/scforge/addons/{id}/access/grants",
            JsonHttp.Body(new { userIds = new[] { Author.ToString(), Granted.ToString() } }),
            Author);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var ids = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("userId").GetString()!).ToArray();
        Assert.Equal(new[] { Granted.ToString() }, ids);
    }

    [Fact]
    public async Task Grant_search_resolves_user_names_for_the_whitelist_editor()
    {
        await SeedUserAsync("searchable", Granted);

        var response = await SendAsync(HttpMethod.Get, "/scforge/addons/access/users?keyword=searchable", null, Author);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Contains(items, i => i.GetProperty("username").GetString() == "searchable");
        // 回传的是 Identity 域的 GUID —— 与名单判定用的 UserId 同一套标识。
        Assert.Contains(items, i => i.GetProperty("userId").GetString() == Granted.ToString());
    }

    [Fact]
    public async Task Grant_search_requires_a_session()
    {
        var response = await SendAsync(HttpMethod.Get, "/scforge/addons/access/users?keyword=a");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /* ---------------- 6. 评论与投票同样受隐私约束 ---------------- */

    [Fact]
    public async Task Comments_respect_privacy()
    {
        // 评论树会暴露「这个插件存在、有人在讨论它」，跟正文同属隐私面。
        var plugin = await PublishAsync("隐私插件", "comment-guard", "password", Password);
        var id = plugin.GetProperty("id").GetString()!;
        var path = $"/scforge/addons/{id}/comments";

        var list = await SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var post = await SendAsync(
            HttpMethod.Post, path, JsonHttp.Body(new { body = "窥探一下", parentId = (string?)null }), Outsider);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);

        // 解锁后放行。
        var token = await UnlockAsync(id, Password);
        var unlockedList = await SendAsync(HttpMethod.Get, path, null, null, null, token);
        Assert.Equal(HttpStatusCode.OK, unlockedList.StatusCode);

        var unlockedPost = await SendAsync(
            HttpMethod.Post, path, JsonHttp.Body(new { body = "解锁后可以评论", parentId = (string?)null }),
            Outsider, null, token);
        Assert.Equal(HttpStatusCode.OK, unlockedPost.StatusCode);
    }

    [Fact]
    public async Task Votes_respect_privacy()
    {
        // 给看不到的插件点赞等于公开确认它存在，也让作者能从票数反推使用人数。
        var plugin = await PublishAsync("隐私插件", "vote-guard", "password", Password);
        var id = plugin.GetProperty("id").GetString()!;

        var anonymous = await SendAsync(HttpMethod.Put, $"/scforge/addons/{id}/vote/up");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var loggedIn = await SendAsync(HttpMethod.Put, $"/scforge/addons/{id}/vote/up", null, Outsider);
        Assert.Equal(HttpStatusCode.Forbidden, loggedIn.StatusCode);

        var token = await UnlockAsync(id, Password);
        var unlocked = await SendAsync(HttpMethod.Put, $"/scforge/addons/{id}/vote/up", null, Outsider, null, token);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
    }

    /* ---------------- 7. 改隐私不触发重新审核 ---------------- */

    [Fact]
    public async Task Changing_privacy_keeps_the_addon_published()
    {
        // 这条回归是刻意守着的：换口令却让插件从目录消失，等于逼作者每次轮换口令都要走一遍审核，
        // 久而久之大家就不换口令了。
        var plugin = await PublishAsync("轮换插件", "rotate-privacy");
        var id = plugin.GetProperty("id").GetString()!;

        var response = await SendAsync(
            HttpMethod.Put,
            "/scforge/addons/" + id,
            PrivacyEditForm(accessMode: "password", accessPassword: Password, accessHint: "群里领取"),
            Author);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        var addon = body.RootElement.GetProperty("addon");
        Assert.Equal("published", addon.GetProperty("status").GetString());
        Assert.Equal("password", addon.GetProperty("accessMode").GetString());

        // 已经从公开目录消失了 —— 这正是隐私的预期效果。
        var search = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/scforge/addons?page=1&pageSize=50"));
        Assert.DoesNotContain(id, IdsOf(search.RootElement.GetProperty("items")));
    }

    [Fact]
    public async Task Editing_other_fields_still_triggers_review()
    {
        // 与上一条成对：隐私改动豁免审核，普通改动不能被顺带豁免。
        var plugin = await PublishAsync("普通编辑", "still-review");
        var id = plugin.GetProperty("id").GetString()!;

        var response = await SendAsync(HttpMethod.Put, "/scforge/addons/" + id, EditForm("改了简介"), Author);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("pending", body.RootElement.GetProperty("addon").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unlocking_whitelist_addon_is_rejected()
    {
        // 白名单模式没有自助解锁入口：一旦允许提交口令就等于给了一份万能钥匙的错觉。
        var plugin = await PublishAsync("白名单插件", "whitelist-unlock", "whitelist");
        var id = plugin.GetProperty("id").GetString()!;

        var response = await SendAsync(
            HttpMethod.Post,
            $"/scforge/addons/{id}/access/unlock",
            JsonHttp.Body(new { password = Password }));

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden,
            $"白名单插件解锁应被拒，实际 {response.StatusCode}");
    }

    /* ---------------- 8. 口令与哈希不外泄 ---------------- */

    [Fact]
    public async Task Password_hash_never_appears_in_any_response()
    {
        var plugin = await PublishAsync("口令插件", "no-hash-leak", "password", Password, "群里问");
        var id = plugin.GetProperty("id").GetString()!;

        var paths = new[]
        {
            $"/scforge/addons/{id}",
            $"/scforge/addons/{id}/access/unlock",
            "/scforge/addons/mine",
            "/scforge/addons?page=1&pageSize=50",
        };

        foreach (var path in paths)
        {
            var payload = path.EndsWith("unlock", StringComparison.Ordinal)
                ? await (await SendAsync(HttpMethod.Post, path, JsonHttp.Body(new { password = Password }))).Content
                    .ReadAsStringAsync()
                : await (await SendAsync(HttpMethod.Get, path, null, Author)).Content.ReadAsStringAsync();

            Assert.DoesNotContain("pbkdf2", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("accessPasswordHash", payload, StringComparison.OrdinalIgnoreCase);
            // 只有一个「是否已设口令」的布尔位，明文与哈希都不出库。
            if (path.EndsWith("/access/unlock", StringComparison.Ordinal))
            {
                Assert.DoesNotContain(Password, payload, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task Access_mode_catalog_is_anon_readable()
    {
        var response = await SendAsync(HttpMethod.Get, "/scforge/addons/access/modes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var keys = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("key").GetString()!).ToArray();
        Assert.Equal(new[] { "public", "password", "whitelist" }, keys);
    }
}
