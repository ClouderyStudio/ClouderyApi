using ClouderyApi.Modules.Identity.Infrastructure.Persistence;
using ClouderyApi.Shared.Directory;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Identity.Application;

/// <summary>
/// <see cref="IUserDirectory"/> 的 Identity 实现：只读查询本地 <c>users</c> 表。
/// 供 SCForge 按用户名/邮箱指定管理员使用，不写任何用户数据。
/// </summary>
public sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public async Task<UserBrief?> FindAsync(string usernameOrEmail, CancellationToken cancellationToken = default)
    {
        var key = (usernameOrEmail ?? string.Empty).Trim();
        if (key.Length == 0) return null;

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Username == key || u.Email == key || u.CasdoorId == key,
                cancellationToken);

        return user is null ? null : new UserBrief(user.Id, user.Username, user.Email, user.Avatar, user.CasdoorId);
    }

    public async Task<UserBrief?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : new UserBrief(user.Id, user.Username, user.Email, user.Avatar, user.CasdoorId);
    }

    public async Task<IReadOnlyList<UserBrief>> SearchAsync(
        string? keyword,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 50);
        var query = db.Users.AsNoTracking();

        var key = (keyword ?? string.Empty).Trim();
        if (key.Length > 0)
        {
            var pattern = $"%{key.Replace("%", string.Empty).Replace("_", string.Empty)}%";
            query = query.Where(u =>
                EF.Functions.Like(u.Username, pattern) ||
                (u.Email != null && EF.Functions.Like(u.Email, pattern)));
        }

        return await query
            .OrderBy(u => u.Username)
            .Take(take)
            .Select(u => new UserBrief(u.Id, u.Username, u.Email, u.Avatar, u.CasdoorId))
            .ToListAsync(cancellationToken);
    }
}
