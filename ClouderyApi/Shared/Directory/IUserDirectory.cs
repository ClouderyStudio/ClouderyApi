namespace ClouderyApi.Shared.Directory;

/// <summary>跨模块的用户目录条目（只读投影，不暴露 Identity 的实体）。</summary>
public sealed record UserBrief(Guid Id, string Username, string? Email, string? Avatar, string? CasdoorId);

/// <summary>
/// 跨模块的用户查询边界：由 Identity 域实现，供其它模块（如 SCForge 指定管理员）按用户名 / 邮箱找人。
/// 只做只读查询，不写用户数据。
/// </summary>
public interface IUserDirectory
{
    /// <summary>按用户名或邮箱精确查找（忽略大小写）；找不到返回 null。</summary>
    Task<UserBrief?> FindAsync(string usernameOrEmail, CancellationToken cancellationToken = default);

    /// <summary>按用户 Id 查找；找不到返回 null。</summary>
    Task<UserBrief?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>按关键词模糊搜索（用户名或邮箱包含），最多返回 <paramref name="limit"/> 条。</summary>
    Task<IReadOnlyList<UserBrief>> SearchAsync(string? keyword, int limit = 20, CancellationToken cancellationToken = default);
}
