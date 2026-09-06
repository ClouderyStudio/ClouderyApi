using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Controllers.Filters;

/// <summary>
/// 管理员鉴权过滤器：仅放行已登录且 CasdoorId 属于配置项
/// <c>Authorization:Admins</c> 的用户，否则返回 403。
/// 用于白名单、赛季、申请、成员等敏感写操作（及白名单读取）。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class AdminOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedObjectResult(new { success = false, message = "请先登录" });
            return;
        }

        var casdoorId = user.FindFirst("CasdoorId")?.Value;
        var admins = context.HttpContext.RequestServices
            .GetService<IConfiguration>()?
            .GetSection("Authorization:Admins")
            .Get<string[]>()
            ?? System.Array.Empty<string>();

        if (string.IsNullOrEmpty(casdoorId) ||
            !admins.Contains(casdoorId, System.StringComparer.OrdinalIgnoreCase))
        {
            context.Result = new ObjectResult(new { success = false, message = "无管理员权限，操作被拒绝" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
