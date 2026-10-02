using Casdoor.Client;
using ClouderyApi.Data;
using ClouderyApi.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Identity.Application;

/// <summary>
/// Casdoor 用户 → 本地 Identity 用户表的同步（登录回调时调用）。
/// 以 CasdoorId 为唯一身份键：已存在则刷新用户名/邮箱/头像与最近登录时间，否则新建。
/// 失败不抛异常而是返回 null（控制器据此返回 500「用户同步失败」），与抽取前的行为一致。
/// </summary>
public class UserSyncService(IdentityDbContext db, ILogger<UserSyncService> logger)
{
    public async Task<User?> SyncAsync(CasdoorUser casdoorUser, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. 检查用户是否已存在（通过 CasdoorId）
            var existingUser = await db.Users
                .FirstOrDefaultAsync(u => u.CasdoorId == casdoorUser.Id, cancellationToken);

            if (existingUser != null)
            {
                // 更新用户信息
                existingUser.Username = casdoorUser.Name ?? casdoorUser.Email?.Split('@')[0] ?? "用户";
                existingUser.Email = casdoorUser.Email;
                existingUser.Avatar = casdoorUser.Avatar;
                existingUser.LastLoginAt = DateTime.UtcNow;

                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("更新用户信息: {UserId}", existingUser.Id);
                return existingUser;
            }

            // 2. 创建新用户
            var newUser = new User
            {
                Id = Guid.NewGuid(),
                Username = casdoorUser.Name ?? casdoorUser.Email?.Split('@')[0] ?? "用户",
                Email = casdoorUser.Email,
                Avatar = casdoorUser.Avatar,
                CasdoorId = casdoorUser.Id ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };

            db.Users.Add(newUser);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("创建新用户: {UserId}, CasdoorId: {CasdoorId}", newUser.Id, newUser.CasdoorId);
            return newUser;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "同步用户到数据库失败");
            return null;
        }
    }
}
