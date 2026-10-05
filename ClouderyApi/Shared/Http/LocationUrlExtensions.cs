using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ClouderyApi.Shared.Http;

/// <summary>
/// 生成对外资源地址（201 Created 的 Location）。
/// 路由匹配本就不区分大小写，但对外暴露的 URL 一律小写，与文档与前端拼写的路径一致
/// （声明路由是 cloudery/[controller]，<see cref="ControllerBase.CreatedAtAction(string, object)"/> 会生成
/// /cloudery/Members/{id}）。
///</summary>
/// <remarks>
/// 刻意不用全局 <c>AddRouting(o => o.LowercaseUrls = true)</c>：那会把 Swagger 的路由表一并改成小写，
/// 撞 <c>SwaggerRouteSnapshotTests</c> 的契约基线（路由大小写属对外契约，逐字不变）；
/// 因此只在真正生成 Location 的这 5 个 POST 端点显式小写（见 docs/DDD-REFACTOR-PLAN.md 附录 C.5）。
/// </remarks>
public static class LocationUrlExtensions
{
    /// <summary>
    /// 按 action 名生成**绝对**地址（与 CreatedAtAction 一致，Location 必须是绝对 URI），并把路径部分小写。
    /// 路由不存在时与 CreatedAtAction 一样抛 InvalidOperationException；
    /// 主机名保持原样，只小写路径（5 个调用点只传路由值 { id }，不会带出可区分大小写的查询串）。
    /// </summary>
    public static string LowercaseActionUrl(this IUrlHelper url, string actionName, object? routeValues)
    {
        // 单参数组没有带 protocol 的重载，用 UrlActionContext 显式要绝对地址（protocol = 当前请求的 scheme）。
        var context = new UrlActionContext
        {
            Action = actionName,
            Values = routeValues,
            Protocol = url.ActionContext.HttpContext.Request.Scheme,
        };
        var absolute = url.Action(context)
            ?? throw new InvalidOperationException($"无法为 action '{actionName}' 生成资源地址。");

        var authorityStart = absolute.IndexOf("://", StringComparison.Ordinal);
        var authorityEnd = authorityStart < 0 ? -1 : absolute.IndexOf('/', authorityStart + 3);
        return authorityEnd < 0
            ? absolute
            : absolute[..authorityEnd] + absolute[authorityEnd..].ToLowerInvariant();
    }
}
