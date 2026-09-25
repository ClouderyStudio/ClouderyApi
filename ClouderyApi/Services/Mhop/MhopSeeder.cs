using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Services.Mhop;

/// <summary>初始化种子数据：管理员账号 + 一条引导帖（对应 Python 后端的 seed.py）。</summary>
public static class MhopSeeder
{
    public const string WelcomePost =
        "最近压力很大，夜里总是睡不着，白天也提不起精神，不知道该怎么办……" +
        "第一次来这里，想问问大家都是怎么熬过低谷期的？";

    public const string WelcomeAiReply =
        "谢谢你愿意把这份疲惫说出来。失眠和情绪低落叠加时，人会格外消耗，先别苛责自己。\n\n" +
        "可以先尝试两件小事：\n" +
        "· 睡前1小时离开手机，做5分钟缓慢呼吸（吸气4秒-屏息7秒-呼气8秒），帮身体先放松；\n" +
        "· 白天安排一次10-20分钟的户外走动，自然光对睡眠节律很有帮助。\n\n" +
        "如果这种状态已经持续两周以上，或开始影响吃饭、工作和社交，建议到正规医院心理科做一次评估，" +
        "这不是软弱，而是认真照顾自己。我们一直在这里，你愿意多说一点也可以。";

    public static async Task SeedAsync(MhopDbContext db, MhopPasswordHasher hasher, ILogger logger)
    {
        if (!await db.MhopUsers.AnyAsync(u => u.Username == "admin"))
        {
            db.MhopUsers.Add(new MhopUser
            {
                Username = "admin",
                PasswordHash = hasher.Hash("admin123"),
                Role = "superadmin",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
            });
            logger.LogInformation("MHOP 种子数据：已创建默认超级管理员 admin（首次登录后请立即修改密码）");
        }
        else
        {
            // 老库升级：内置 admin 账号升级为超级管理员（幂等）。
            // 其他存量管理员保持 admin 角色但权限为空，需超管在后台逐个重新授权。
            var upgraded = await db.MhopUsers
                .Where(u => u.Username == "admin" && u.Role == "admin")
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, "superadmin"));
            if (upgraded > 0)
                logger.LogInformation("MHOP 种子数据：内置管理员 admin 已升级为超级管理员");
        }

        // 应急超管账号 root：原 admin 被停用时仍可进入后台。仅在不存在时创建，
        // 已存在则绝不重置密码/状态，避免覆盖后续的改密操作。
        if (!await db.MhopUsers.AnyAsync(u => u.Username == "root"))
        {
            db.MhopUsers.Add(new MhopUser
            {
                Username = "root",
                PasswordHash = hasher.Hash("1234567"),
                Role = "superadmin",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
            });
            logger.LogInformation("MHOP 种子数据：已创建应急超级管理员 root（首次登录后请立即修改密码）");
        }

        if (!await db.MhopPosts.AnyAsync())
        {
            var post = new MhopPost
            {
                UserId = null,
                IsAnonymous = true,
                Content = WelcomePost,
                Board = "stress",
                Status = 1,
                Crisis = false,
                CreatedAt = DateTime.UtcNow,
            };
            db.MhopPosts.Add(post);
            await db.SaveChangesAsync();
            db.MhopReplies.Add(new MhopReply
            {
                PostId = post.Id,
                UserId = null,
                IsAnonymous = true,
                Content = WelcomeAiReply,
                Status = 1,
                IsAi = true,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }
}
