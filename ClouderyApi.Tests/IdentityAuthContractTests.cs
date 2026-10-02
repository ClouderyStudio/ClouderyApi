using System.Net;
using System.Text.Json;
using ClouderyApi.Tests.TestSupport;

namespace ClouderyApi.Tests;

/// <summary>
/// Identity 鉴权契约：config/state 匿名可达、callback 在触达 Casdoor 前就能被 state 短路、
/// me/status 的匿名与登录态形状、logout 返回 Casdoor 登出地址。
/// 注意：默认 Challenge 方案是 Casdoor 的 JwtBearer，因此受保护端点是 401 空体而非 302。
/// </summary>
public sealed class IdentityAuthContractTests : IntegrationTestBase
{
    [Fact]
    public async Task Config_is_anonymous_and_reports_casdoor_and_callback_uri()
    {
        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/identity/auth/config");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        var casdoor = body.RootElement.GetProperty("casdoor");
        Assert.Equal(
            new[] { "endpoint", "organizationName", "applicationName", "clientId", "scope" },
            casdoor.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(casdoor.GetProperty("endpoint").GetString()));
        Assert.Equal("openid profile email", casdoor.GetProperty("scope").GetString());
        Assert.False(string.IsNullOrWhiteSpace(casdoor.GetProperty("clientId").GetString()));
        Assert.EndsWith("/identity/auth/callback", body.RootElement.GetProperty("callbackUri").GetString());
    }

    [Fact]
    public async Task State_returns_url_safe_token_and_http_only_same_site_cookie()
    {
        var response = await Client.GetAsync("/identity/auth/state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (_, body) = await JsonHttp.ReadAsync(response);
        var state = body.RootElement.GetProperty("state").GetString();
        Assert.False(string.IsNullOrWhiteSpace(state));
        Assert.DoesNotContain("+", state);
        Assert.DoesNotContain("/", state);
        Assert.DoesNotContain("=", state);

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var cookieValue = setCookie.Split(';')[0]["oauth_state=".Length..];
        Assert.Equal(state, cookieValue);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Callback_with_empty_code_returns_400_chinese_message()
    {
        var response = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/identity/auth/callback",
            new { code = "", state = "", redirectUri = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (_, body) = await JsonHttp.ReadAsync(response);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("授权码不能为空", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Callback_without_matching_state_cookie_returns_400()
    {
        var response = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/identity/auth/callback",
            new { code = "auth-code", state = "not-the-cookie", redirectUri = "https://localhost/identity/auth/callback" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (_, body) = await JsonHttp.ReadAsync(response);
        Assert.Equal("state 校验失败，请重新发起登录", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Callback_missing_required_properties_returns_framework_validation_problem()
    {
        var response = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/identity/auth/callback", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var (_, body) = await JsonHttp.ReadAsync(response);
        Assert.False(body.RootElement.TryGetProperty("success", out _));
        Assert.True(body.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Me_is_401_when_anonymous_and_returns_claims_when_signed_in()
    {
        var anonymous = await Client.GetAsync("/identity/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var (_, anonymousBody) = await JsonHttp.ReadAsync(anonymous);
        Assert.False(anonymousBody.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("未登录", anonymousBody.RootElement.GetProperty("message").GetString());

        var userId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Client.DefaultRequestHeaders.Add("Cookie", AuthCookie.CreateHeader(Factory.Services, userId: userId));

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/identity/auth/me");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        var user = body.RootElement.GetProperty("user");
        Assert.Equal(userId.ToString(), user.GetProperty("id").GetString());
        Assert.Equal("contract-user", user.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Status_reports_authentication_state()
    {
        var (anonymousStatus, anonymousBody) = await JsonHttp.GetJsonAsync(Client, "/identity/auth/status");
        Assert.Equal(HttpStatusCode.OK, anonymousStatus);
        Assert.False(anonymousBody.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("未登录", anonymousBody.RootElement.GetProperty("message").GetString());

        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Client.DefaultRequestHeaders.Add("Cookie", AuthCookie.CreateHeader(Factory.Services, userId: userId));

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/identity/auth/status");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(userId.ToString(), body.RootElement.GetProperty("user").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Logout_returns_casdoor_logout_url()
    {
        var response = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/identity/auth/logout", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (_, body) = await JsonHttp.ReadAsync(response);
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("已成功登出", body.RootElement.GetProperty("message").GetString());
        Assert.EndsWith("/api/logout", body.RootElement.GetProperty("casdoorLogoutUrl").GetString());
    }
}
