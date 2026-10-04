using System.Security.Claims;
using ClouderyApi.Modules.Scforge.Domain;

namespace ClouderyApi.Modules.Scforge.Infrastructure;

/// <summary>
/// 当前请求的身份快照（来自 Casdoor Cookie 会话写入的 Claims）。
///
/// SCForge 不复用 MHOP 的 JWT：登录态完全由 Cloudery 主站的 Cookie 会话承担。
/// 这里只回答「我是谁」；「我有什么后台权限」由 <see cref="ScforgeAdminAccessor"/> 单独解析
/// （需要查 scforge_admins，且与身份是两件事：同一个人可能既是作者又是审核员）。
/// </summary>
public sealed class ScforgeCurrentUser
{
    public ScforgeCurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;
        IsAuthenticated = principal?.Identity?.IsAuthenticated == true;
        if (!IsAuthenticated) return;

        UserId = principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        UserName = principal.FindFirst(ClaimTypes.Name)?.Value;
        Avatar = principal.FindFirst("Avatar")?.Value;
        CasdoorId = principal.FindFirst("CasdoorId")?.Value;
    }

    public bool IsAuthenticated { get; }

    /// <summary>Identity 域 Users.Id（GUID 字符串）。</summary>
    public string? UserId { get; }

    public string? UserName { get; }

    public string? Avatar { get; }

    public string? CasdoorId { get; }

    /// <summary>当前用户 Id；未登录抛 401 业务异常。</summary>
    public string RequireUserId() =>
        IsAuthenticated && !string.IsNullOrEmpty(UserId)
            ? UserId
            : throw new ScforgeApiException(401, "请先登录");

    /// <summary>作者名快照；缺失时用 CasdoorId 兜底，避免出现空作者。</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(UserName)
        ? UserName
        : (!string.IsNullOrWhiteSpace(CasdoorId) ? CasdoorId : "匿名用户");
}
