using ClouderyApi.Shared.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using ClouderyApi.Shared.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Shared.Authorization;

/// <summary>管理员授权要求：已登录且 CasdoorId 命中 <c>Authorization:Admins</c>。</summary>
public sealed class AdminOnlyRequirement : IAuthorizationRequirement
{
}

/// <summary>管理员判定：与旧 AdminOnly 过滤器逐字同义（claim <c>CasdoorId</c>，忽略大小写）。</summary>
public sealed class AdminOnlyAuthorizationHandler(IOptions<AdminOptions> options) : AuthorizationHandler<AdminOnlyRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminOnlyRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var casdoorId = context.User.FindFirst("CasdoorId")?.Value;
            var admins = options.Value.Admins ?? Array.Empty<string>();
            if (!string.IsNullOrEmpty(casdoorId) &&
                admins.Contains(casdoorId, StringComparer.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// 授权失败响应形状保持不变：只有 <c>[AdminOnly]</c> 一条授权元数据（类上无 <c>[Authorize]</c>）的端点，
/// 未登录写 401 <c>{"detail":"请先登录"}</c>，非管理员写 403
/// <c>{"detail":"无管理员权限，操作被拒绝"}</c>；
/// 其余端点（类级 <c>[Authorize]</c>）继续走框架默认挑战（401 + WWW-Authenticate: Bearer + 空体）。
/// </summary>
public sealed class AdminOnlyAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Succeeded &&
            Enumerable.Any(policy.Requirements.OfType<AdminOnlyRequirement>()))
        {
            var authenticated = context.User?.Identity?.IsAuthenticated == true;
            // 已登录但非管理员：与旧 AdminOnly 过滤器逐字同形（无论类上是否还有 [Authorize]）。
            // 未登录：只有 [AdminOnly] 一条授权元数据的端点才由本处理器写 JSON，
            // 其余端点维持框架默认挑战（401 + WWW-Authenticate: Bearer + 空体）。
            if (authenticated || CountsAsSoleAuthorizeData(context))
            {
                // 统一错误体，与控制器写出的形状一致（见 docs/API-ERROR-SHAPE.md）。
                await ApiError.WriteAsync(
                    context,
                    authenticated ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized,
                    authenticated ? "无管理员权限，操作被拒绝" : "请先登录");
                return;
            }
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private static bool CountsAsSoleAuthorizeData(HttpContext context)
    {
        var authorizeData = context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>();
        return authorizeData is null || authorizeData.Count <= 1;
    }
}

/// <summary>注册管理员 policy、判定处理器与响应处理器（Stage 5.3）。</summary>
public static class AdminOnlyAuthorizationExtensions
{
    public static IServiceCollection AddAdminOnlyAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
            options.AddPolicy(AdminOnlyAttribute.PolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new AdminOnlyRequirement());
            }));
        services.AddSingleton<IAuthorizationHandler, AdminOnlyAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AdminOnlyAuthorizationMiddlewareResultHandler>();
        return services;
    }
}
