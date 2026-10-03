using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Modules.Mhop.Domain;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace ClouderyApi.Modules.Mhop.Infrastructure;

/// <summary>
/// 初始化种子数据：管理员账号 + 一条引导帖（对应 Python 后端的 seed.py）。
/// </summary>
public static class MhopSeeder
{
    /// <summary>种子管理员账号名。保留历史命名，避免存量部署重复创建第二个超管。</summary>
    public const string AdminUsername = "admin";
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

    public static async Task SeedAsync(
        MhopDbContext db, MhopPasswordHasher hasher, MhopOptions options, ILogger logger)
    {
        if (!await db.MhopUsers.AnyAsync(u => u.Username == AdminUsername))
        {
            // 口令来源优先级：配置 Mhop:SeedAdminPassword > 一次性随机强口令。
            // 绝不使用代码内写死的口令：仓库是公开的，写死的口令等同无口令。
            var configured = options.SeedAdminPassword;
            var generated = string.IsNullOrWhiteSpace(configured);
            var password = generated ? GenerateStrongPassword() : configured;

            db.MhopUsers.Add(new MhopUser
            {
                Username = AdminUsername,
                PasswordHash = hasher.Hash(password),
                Role = MhopUserRole.SuperAdmin,
                Status = MhopUserStatus.Active,
                CreatedAt = DateTime.UtcNow,
            });
            logger.LogInformation("MHOP 种子数据：已创建默认超级管理员 {Admin}", AdminUsername);
            if (generated)
            {
                // 未配置口令时只在本次日志里出现一次，运维应立刻登录改密。
                logger.LogWarning(
                    "MHOP 种子数据：未配置 Mhop:SeedAdminPassword，已为超管 {Admin} 生成随机初始口令：{Password}（仅本次日志可见，请立即登录修改）",
                    AdminUsername, password);
            }
            else
            {
                logger.LogInformation("MHOP 种子数据：超管初始口令取自 Mhop:SeedAdminPassword（首次登录后请立即修改密码）");
            }
        }
        else
        {
            // 老库升级：内置 admin 账号升级为超级管理员（幂等）。
            // 其他存量管理员保持 admin 角色但权限为空，需超管在后台逐个重新授权。
            var upgraded = await db.MhopUsers
                .Where(u => u.Username == AdminUsername && u.Role == MhopUserRole.Admin)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, MhopUserRole.SuperAdmin));
            if (upgraded > 0)
                logger.LogInformation("MHOP 种子数据：内置管理员 admin 已升级为超级管理员");
        }

        if (!await db.MhopPosts.AnyAsync())
        {
            var post = new MhopPost
            {
                UserId = null,
                IsAnonymous = true,
                Content = WelcomePost,
                Board = "stress",
                Status = ContentStatus.Published,
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
                Status = ContentStatus.Published,
                IsAi = true,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 生成一次性随机强口令（24 位，大小写 + 数字 + 符号）。
    /// 用 <see cref="RandomNumberGenerator"/> 而非 <see cref="Random"/>：后者在 .NET 6+
    /// 对无参构造已不是密码学安全随机。
    /// </summary>
    private static string GenerateStrongPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*-_=+";
        const string all = upper + lower + digits + symbols;

        var chars = new char[24];
        // 前四类各保底一位，保证口令一定同时含大小写、数字与符号。
        chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        for (var i = 4; i < chars.Length; i++)
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

        // Fisher-Yates 洗牌，避免"类型前缀"成为可预测规律。
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
