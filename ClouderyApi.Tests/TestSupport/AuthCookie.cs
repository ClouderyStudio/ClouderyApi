using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Tests.TestSupport;

/// <summary>
/// 绕过 Casdoor 直接签发身份 Cookie：票据由应用真实注册的 "Cookies" 方案加密，
/// 因此 [Authorize] / AdminOnly 的鉴权链路与 Casdoor 回调签发的 Cookie 完全一致。
/// </summary>
public static class AuthCookie
{
    /// <summary>测试夹具注入到 Authorization:Admins 里的管理员 CasdoorId。</summary>
    public const string TestAdminCasdoorId = "test-admin";

    public static string CreateHeader(IServiceProvider services, string? casdoorId = null, Guid? userId = null)
    {
        var options = services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, (userId ?? Guid.NewGuid()).ToString()),
            new(ClaimTypes.Name, "contract-user"),
        };
        if (casdoorId is not null) claims.Add(new Claim("CasdoorId", casdoorId));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), CookieAuthenticationDefaults.AuthenticationScheme);
        return options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket);
    }
}
