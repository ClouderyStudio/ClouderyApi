namespace ClouderyApi.Modules.Scforge.Application;

/// <summary>
/// 一次请求的操作者身份快照。
///
/// 只承载「我是谁」；「我有什么权限」由 <see cref="ScforgeAdminContext"/> 单独解析
/// （需要查库，且与身份是两件事：同一个人可能是作者，也可能同时是审核员）。
/// 控制器从 Cookie 会话的 Claims 构造它，应用层因此不依赖 HttpContext。
/// </summary>
public sealed record ScforgeActor(string? UserId, string DisplayName, string? Avatar)
{
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);

    /// <summary>操作者 Id；未登录抛 401 业务异常。</summary>
    public string RequireUserId() =>
        IsAuthenticated ? UserId! : throw new Domain.ScforgeApiException(401, "请先登录");

    /// <summary>
    /// 是否是某个资源的作者本人。**发布权与编辑权只属于作者**，
    /// 管理员身份在这里不生效（内容管理走独立的 admin 接口）。
    /// </summary>
    public bool IsAuthorOf(string authorId) => IsAuthenticated && UserId == authorId;
}
