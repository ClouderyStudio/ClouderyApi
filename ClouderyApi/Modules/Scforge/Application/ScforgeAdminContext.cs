using ClouderyApi.Modules.Scforge.Domain;

namespace ClouderyApi.Modules.Scforge.Application;

/// <summary>
/// 调用者在 SCForge 后台里的身份：是否管理员、是否超管、以及被授予的权限码。
///
/// 权限判定集中在这里，业务代码只问「能不能审核 / 能不能管内容」，
/// 不自己拼 role 判断，避免各处规则漂移。
/// </summary>
public sealed record ScforgeAdminContext(
    bool IsAdmin,
    bool IsSuperAdmin,
    IReadOnlyList<string> Permissions,
    string DisplayName)
{
    public static readonly ScforgeAdminContext Anonymous = new(false, false, [], string.Empty);

    /// <summary>超管隐式拥有全部权限。</summary>
    public bool Has(string code) =>
        IsAdmin && (IsSuperAdmin || Permissions.Contains(code, StringComparer.Ordinal));

    public bool CanReview => Has(ScforgePermissionSet.Review);

    public bool CanManageContent => Has(ScforgePermissionSet.Content);

    /// <summary>进入后台面板的最低门槛。</summary>
    public void RequireAdmin()
    {
        if (!IsAdmin) throw new ScforgeApiException(403, "需要管理员权限");
    }

    public void RequireReview()
    {
        if (!CanReview) throw new ScforgeApiException(403, "没有审核权限，请联系超级管理员授予");
    }

    public void RequireContent()
    {
        if (!CanManageContent) throw new ScforgeApiException(403, "没有内容管理权限，请联系超级管理员授予");
    }

    /// <summary>管理员管理（授予 / 调整 / 撤销）是超管专属。</summary>
    public void RequireSuper()
    {
        if (!IsSuperAdmin) throw new ScforgeApiException(403, "只有超级管理员可以管理管理员");
    }
}
