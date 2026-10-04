using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Domain;
using ClouderyApi.Shared.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Modules.Scforge.Infrastructure;

/// <summary>
/// 解析调用者在 SCForge 后台里的权限。
///
/// 顺序：
///   1. 未登录 → 匿名；
///   2. <c>scforge_admins</c> 里有本用户 Id 的记录 → 按记录里的角色与权限；
///   3. 否则若其 CasdoorId 命中配置白名单 <c>Authorization:Admins</c> → 视为**超级管理员**。
///      这一步是引导口子：第一个超管不必手工插库就能登录后台并授权他人。
/// 每次请求实时读库，权限调整立即生效（与 MHOP 后台一致）。
/// </summary>
public sealed class ScforgeAdminAccessor(
    IScforgeDbContext db,
    ScforgeCurrentUser current,
    IOptions<AdminOptions> adminOptions)
{
    /// <summary>配置里的管理员 CasdoorId 白名单（引导用）。</summary>
    public IReadOnlyList<string> ConfigWhitelist => adminOptions.Value.Admins ?? [];

    public async Task<ScforgeAdminContext> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (!current.IsAuthenticated) return ScforgeAdminContext.Anonymous;

        // 白名单优先：Authorization:Admins 里的账号恒为超管。
        // 否则一条误建的管理员记录就能把引导超管降级，平台会再也没人能授予权限。
        var whitelist = adminOptions.Value.Admins ?? [];
        if (!string.IsNullOrEmpty(current.CasdoorId) &&
            whitelist.Contains(current.CasdoorId, StringComparer.OrdinalIgnoreCase))
        {
            return new ScforgeAdminContext(
                true,
                true,
                ScforgePermissionSet.All,
                current.DisplayName);
        }

        if (Guid.TryParse(current.UserId, out var userId))
        {
            var record = await db.ScforgeAdmins
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == userId, cancellationToken);

            if (record is not null)
            {
                return new ScforgeAdminContext(
                    true,
                    record.IsSuper,
                    record.EffectivePermissions,
                    record.UserName);
            }
        }

        return ScforgeAdminContext.Anonymous;
    }
}
