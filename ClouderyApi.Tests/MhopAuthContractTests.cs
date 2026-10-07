using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClouderyApi.Modules.Mhop.Infrastructure;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// 锁定 MHOP 身份接口的对外契约：路由、状态码、{ detail } 错误体、蛇形字段名、
/// 北京时间（+08:00）时间，以及公开资料的脱敏规则。这些是前端依赖的兼容红线。
/// </summary>
public sealed class MhopAuthContractTests : IntegrationTestBase
{
    private static StringContent JsonBody(object body)
        => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string path, object body, string? token = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonBody(body) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetAsync(string path, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Client.SendAsync(request);
    }

    private static async Task<(HttpStatusCode Status, JsonDocument Body)> ReadAsync(HttpResponseMessage response)
        => (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()));

    private async Task<string> RegisterAsync(string username, string password = "secret123")
    {
        var (status, body) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Post, "/mhop/auth/register", new { username, password }));
        Assert.Equal(HttpStatusCode.OK, status);
        return body.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<string> LoginAsync(string username, string password)
    {
        var (status, body) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Post, "/mhop/auth/login", new { username, password }));
        Assert.Equal(HttpStatusCode.OK, status);
        return body.RootElement.GetProperty("access_token").GetString()!;
    }

    [Fact]
    public async Task Register_returns_snake_case_payload_with_beijing_created_at()
    {
        var (status, body) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Post, "/mhop/auth/register", new { username = "tester01", password = "secret123" }));

        Assert.Equal(HttpStatusCode.OK, status);
        var root = body.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("access_token").GetString()));
        Assert.Equal("bearer", root.GetProperty("token_type").GetString());
        Assert.False(root.GetProperty("new_account").GetBoolean());

        var user = root.GetProperty("user");
        Assert.Equal("tester01", user.GetProperty("username").GetString());
        Assert.Equal("user", user.GetProperty("role").GetString());
        Assert.Equal("active", user.GetProperty("status").GetString());
        Assert.EndsWith("+08:00", user.GetProperty("created_at").GetString());
    }

    [Theory]
    [InlineData("a", "secret123", "用户名长度需为 2-32 个字符")]
    [InlineData("tester01", "12345", "密码长度需为 6-64 位")]
    public async Task Register_rejects_invalid_credentials(string username, string password, string expectedDetail)
    {
        var (status, body) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Post, "/mhop/auth/register", new { username, password }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(expectedDetail, body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Register_rejects_duplicate_username()
    {
        await RegisterAsync("dupuser");

        var (status, body) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Post, "/mhop/auth/register", new { username = "dupuser", password = "secret123" }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("用户名已存在", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Error_body_uses_detail_key_and_keeps_chinese_un_escaped()
    {
        var response = await SendJsonAsync(HttpMethod.Post, "/mhop/auth/login", new { username = "nobody", password = "whatever" });
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("detail", raw);
        Assert.Contains("用户名或密码错误", raw);
        Assert.Contains("charset=utf-8", response.Content.Headers.ContentType?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Login_returns_superadmin_for_seeded_admin_and_401_for_wrong_password()
    {
        var (ok, okBody) = await ReadAsync(await SendJsonAsync(HttpMethod.Post, "/mhop/auth/login",
            new
            {
                username = ClouderyApiFactory.TestSeedAdminUsername,
                password = ClouderyApiFactory.TestSeedAdminPassword,
            }));
        Assert.Equal(HttpStatusCode.OK, ok);
        Assert.Equal("superadmin", okBody.RootElement.GetProperty("user").GetProperty("role").GetString());

        var (bad, badBody) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Post, "/mhop/auth/login",
                new
                {
                    username = ClouderyApiFactory.TestSeedAdminUsername,
                    password = "wrong-password",
                }));
        Assert.Equal(HttpStatusCode.Unauthorized, bad);
        Assert.Equal("用户名或密码错误", badBody.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Login_with_disabled_account_returns_403()
    {
        await RegisterAsync("disableduser");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            var user = await db.MhopUsers.SingleAsync(u => u.Username == "disableduser");
            user.Status = "disabled";
            await db.SaveChangesAsync();
        }

        var (status, body) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Post, "/mhop/auth/login", new { username = "disableduser", password = "secret123" }));
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("账号已被停用", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Me_requires_bearer_token_and_returns_current_user()
    {
        var (anon, anonBody) = await ReadAsync(await GetAsync("/mhop/auth/me"));
        Assert.Equal(HttpStatusCode.Unauthorized, anon);
        Assert.Equal("请先登录", anonBody.RootElement.GetProperty("detail").GetString());

        var token = await LoginAsync(
            ClouderyApiFactory.TestSeedAdminUsername, ClouderyApiFactory.TestSeedAdminPassword);
        var (authed, body) = await ReadAsync(await GetAsync("/mhop/auth/me", token));
        Assert.Equal(HttpStatusCode.OK, authed);
        Assert.Equal("admin", body.RootElement.GetProperty("username").GetString());
        Assert.True(body.RootElement.GetProperty("permissions").GetArrayLength() > 0);
    }

    [Fact]
    public async Task BindPhone_is_reflected_in_profile_and_masked_in_public_profile()
    {
        var token = await RegisterAsync("phoneuser");

        var (bind, bindBody) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Put, "/mhop/auth/me/phone", new { phone = "13800138000" }, token));
        Assert.Equal(HttpStatusCode.OK, bind);
        Assert.Equal("13800138000", bindBody.RootElement.GetProperty("phone").GetString());
        var userId = bindBody.RootElement.GetProperty("id").GetInt32();

        var (pub, pubBody) = await ReadAsync(await GetAsync("/mhop/auth/users/" + userId));
        Assert.Equal(HttpStatusCode.OK, pub);
        Assert.Equal(JsonValueKind.Null, pubBody.RootElement.GetProperty("phone").ValueKind);
        Assert.Equal(0, pubBody.RootElement.GetProperty("permissions").GetArrayLength());
        // 公开资料不泄露邮箱验证状态（未验证账号）
        Assert.False(pubBody.RootElement.GetProperty("email_verified").GetBoolean());
        // 邮箱是邮箱验证码登录的唯一凭据，公开接口不得下发（否则可按 id 遍历全站 PII）。
        Assert.Equal(JsonValueKind.Null, pubBody.RootElement.GetProperty("email").ValueKind);

        var (invalid, invalidBody) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Put, "/mhop/auth/me/phone", new { phone = "12345" }, token));
        Assert.Equal(HttpStatusCode.BadRequest, invalid);
        Assert.Equal("手机号格式不正确", invalidBody.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task BindPhone_rejects_phone_already_bound_to_another_account()
    {
        var first = await RegisterAsync("phoneA");
        var (firstBind, _) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Put, "/mhop/auth/me/phone", new { phone = "13900139000" }, first));
        Assert.Equal(HttpStatusCode.OK, firstBind);

        var second = await RegisterAsync("phoneB");
        var (status, body) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Put, "/mhop/auth/me/phone", new { phone = "13900139000" }, second));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("该手机号已被其他账号绑定", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task BindEmail_requires_a_valid_code_and_is_reflected_in_profile()
    {
        var token = await RegisterAsync("emailbind");

        // 未发码 / 错码：一律 400，不泄露验证码状态
        var (wrong, wrongBody) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Put, "/mhop/auth/me/email",
                new { email = "bind@example.com", code = "000000" }, token));
        Assert.Equal(HttpStatusCode.BadRequest, wrong);
        Assert.Equal("验证码错误或已过期", wrongBody.RootElement.GetProperty("detail").GetString());

        // 未验证前对外字段为 false
        var (before, beforeBody) = await ReadAsync(await GetAsync("/mhop/auth/me", token));
        Assert.Equal(HttpStatusCode.OK, before);
        Assert.False(beforeBody.RootElement.GetProperty("email_verified").GetBoolean());

        var (badFormat, badFormatBody) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Put, "/mhop/auth/me/email",
                new { email = "not-an-email", code = "123456" }, token));
        Assert.Equal(HttpStatusCode.BadRequest, badFormat);
        Assert.Equal("邮箱格式不正确", badFormatBody.RootElement.GetProperty("detail").GetString());

        // 正确验证码：写入邮箱并盖上已验证标记
        await MhopEmailVerify.VerifyEmailAsync(Factory, Client, token, "bind@example.com");

        var (mine, mineBody) = await ReadAsync(await GetAsync("/mhop/auth/me", token));
        Assert.Equal(HttpStatusCode.OK, mine);
        Assert.Equal("bind@example.com", mineBody.RootElement.GetProperty("email").GetString());
        Assert.True(mineBody.RootElement.GetProperty("email_verified").GetBoolean());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
        var user = await db.MhopUsers.AsNoTracking().SingleAsync(u => u.Username == "emailbind");
        Assert.NotNull(user.EmailVerifiedAt);
    }

    [Fact]
    public async Task BindEmail_rejects_email_already_bound_to_another_account()
    {
        // 直接把第一个账号的邮箱落到库里，避免与第二个账号抢同一个地址的 60 秒重发间隔
        await RegisterAsync("emailbindA");
        using (var seedScope = Factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<MhopDbContext>();
            var firstUser = await seedDb.MhopUsers.SingleAsync(u => u.Username == "emailbindA");
            firstUser.Email = "shared@example.com";
            firstUser.EmailVerifiedAt = DateTime.UtcNow;
            await seedDb.SaveChangesAsync();
        }

        var second = await RegisterAsync("emailbindB");
        var codes = Factory.Services.GetRequiredService<MhopEmailCodeService>();
        var issued = await codes.IssueCodeAsync("shared@example.com");
        Assert.NotNull(issued.Code);

        var (status, body) = await ReadAsync(
            await SendJsonAsync(HttpMethod.Put, "/mhop/auth/me/email",
                new { email = "shared@example.com", code = issued.Code }, second));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("该邮箱已被其他账号绑定", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Public_profile_of_unknown_user_returns_404()
    {
        var (status, body) = await ReadAsync(await GetAsync("/mhop/auth/users/987654"));
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("用户不存在", body.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>
    /// 反向锁定：脱敏只作用于公开资料接口，本人访问自己的资料仍必须能拿到邮箱，
    /// 否则前端账号页会显示不出绑定邮箱。
    /// </summary>
    [Fact]
    public async Task Own_profile_still_exposes_email_while_public_profile_masks_it()
    {
        var token = await RegisterAsync("emailowner");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            var user = await db.MhopUsers.SingleAsync(u => u.Username == "emailowner");
            user.Email = "owner@example.com";
            await db.SaveChangesAsync();
        }

        var userId = await ResolveUserIdAsync(token);

        var (mine, mineBody) = await ReadAsync(await GetAsync("/mhop/auth/me", token));
        Assert.Equal(HttpStatusCode.OK, mine);
        Assert.Equal("owner@example.com", mineBody.RootElement.GetProperty("email").GetString());

        var (pub, pubBody) = await ReadAsync(await GetAsync("/mhop/auth/users/" + userId));
        Assert.Equal(HttpStatusCode.OK, pub);
        Assert.Equal(JsonValueKind.Null, pubBody.RootElement.GetProperty("email").ValueKind);
    }

    private async Task<int> ResolveUserIdAsync(string token)
    {
        var (_, body) = await ReadAsync(await GetAsync("/mhop/auth/me", token));
        return body.RootElement.GetProperty("id").GetInt32();
    }
}
