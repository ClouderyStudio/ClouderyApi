using Microsoft.AspNetCore.Authorization;

namespace ClouderyApi.Shared.Authorization;

/// <summary>
/// 管理员授权特性：本质是 <c>[Authorize(Policy = "AdminOnly")]</c> 的简写。
/// 判定逻辑见 <see cref="AdminOnlyAuthorizationHandler"/>，响应形状见
/// <see cref="AdminOnlyAuthorizationMiddlewareResultHandler"/>。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class AdminOnlyAttribute : AuthorizeAttribute
{
    /// <summary>管理员策略名。</summary>
    public const string PolicyName = "AdminOnly";

    public AdminOnlyAttribute() => Policy = PolicyName;
}
