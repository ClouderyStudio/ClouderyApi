using System.Security.Cryptography;
using ClouderyApi.Modules.Scforge.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Scforge.Application;

/// <summary>
/// API Key 的签发与吊销。
///
/// 安全约定：
///   • 令牌明文**只在创建时返回一次**，库里只留哈希；本服务没有任何方法能取回明文。
///   • 吊销是软删除（写 <c>RevokedAt</c>），保留行以便审计「谁在何时用哪把 Key 发生过什么」。
///   • 前缀（<c>scf_xxxx</c>）明文入库，界面上只显示前缀 + 尾部 4 位，
///     这样用户能认出「这是发版机器人那把」而无法反推完整令牌。
/// </summary>
public sealed class ScforgeApiKeyAppService(IScforgeDbContext db)
{
    private const int TokenBytes = 32; // 256 位熵，离线爆破不可行
    private const int PrefixLength = 8;
    private const int TailVisible = 4;
    /// <summary>签发一把新 Key。返回的 <c>token</c> 只在此刻有效。</summary>
    public async Task<(ScforgeApiKey Key, string Token)> CreateAsync(
        Guid userId,
        string userName,
        string name,
        IReadOnlyList<string>? scopes,
        DateTime? expiresAt,
        string? createdByCasdoorId,
        CancellationToken cancellationToken = default)
    {
        // 注意：归属人 userId 来自登录会话，不在这里做存在性校验 ——
        // SCForge 与 Identity 是两个独立 DbContext，项目约定不做跨域联表；
        // 拦截"用户不存在"会误伤刚注册、尚未同步到 Identity 域的新号。
        var normalizedScopes = ScforgeApiKeyScopes.From(scopes);
        if (normalizedScopes.Count == 0)
        {
            // 空作用域的 Key 什么也做不了，签发它只会让人误以为配好了 —— 直接拒绝。
            throw new ScforgeRuleException("请至少选择一个作用域（读取 / 发布 / 管理）");
        }

        if (string.IsNullOrWhiteSpace(name)) name = "未命名";

        // 过期时间只接受未来时刻，避免生成一张创建即失效的卡。
        if (expiresAt is not null && expiresAt.Value <= DateTime.UtcNow)
        {
            throw new ScforgeRuleException("过期时间必须晚于当前时间");
        }

        var token = GenerateToken(out var prefix);
        var key = new ScforgeApiKey
        {
            Prefix = prefix,
            KeyHash = ScforgeApiKeyScopes.HashToken(token),
            UserId = userId,
            UserName = userName,
            Name = name.Trim(),
            Scopes = ScforgeApiKeyScopes.Serialize(normalizedScopes),
            ExpiresAt = expiresAt,
            CreatedByCasdoorId = createdByCasdoorId,
            CreatedAt = DateTime.UtcNow,
        };

        db.ScforgeApiKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);
        return (key, token);
    }

    /// <summary>生成 <c>scf_&lt;base64url 前缀&gt;&lt;base64url 随机段&gt;</c>。</summary>
    private static string GenerateToken(out string prefix)
    {
        var random = RandomNumberGenerator.GetBytes(TokenBytes);
        var body = ToBase64Url(random);

        // 前缀取随机段开头 4 字节，够短好记，够长(8 字符)不至于撞上。
        prefix = ScforgeApiKeyScopes.TokenPrefix + ToBase64Url(random.AsSpan(0, 4).ToArray());
        return prefix + body;
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>列出某人的全部 Key（含已吊销），最近的在前。</summary>
    public async Task<List<ScforgeApiKey>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.ScforgeApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <summary>列出全部 Key（超管后台巡检用）。</summary>
    public async Task<List<ScforgeApiKey>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.ScforgeApiKeys
            .AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// 吊销一把 Key。只能吊销自己的（<paramref name="userId"/> 非空时）或超管指定任意人。
    /// 重复吊销是幂等的，不会报错 —— 运维脚本重跑不应失败。
    /// </summary>
    public async Task<bool> RevokeAsync(
        Guid id,
        Guid? ownerUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var key = await db.ScforgeApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
        if (key is null) return false;

        // 归属校验：非超管路径（ownerUserId 非空）只能动自己的 Key。
        if (ownerUserId is not null && key.UserId != ownerUserId.Value) return false;
        if (key.RevokedAt is not null) return true;

        key.RevokedAt = DateTime.UtcNow;
        key.RevokedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// 轮换：吊销旧的并签发一把新的，作用域与归属继承。
    /// 用法是「怀疑泄露时一键换锁」，避免手动重建导致权限配置漂移。
    /// </summary>
    public async Task<(ScforgeApiKey Key, string Token)> RotateAsync(
        ScforgeApiKey old,
        string? createdByCasdoorId,
        CancellationToken cancellationToken = default)
    {
        await RevokeAsync(old.Id, null, "轮换：已由新密钥取代", cancellationToken);
        return await CreateAsync(
            old.UserId,
            old.UserName,
            old.Name,
            ScforgeApiKeyScopes.Parse(old.Scopes),
            old.ExpiresAt,
            createdByCasdoorId,
            cancellationToken);
    }

    /// <summary>Key 的当前有效状态（供界面展示）。</summary>
    public static (bool Usable, string Status) Describe(ScforgeApiKey key)
    {
        if (key.RevokedAt is not null) return (false, "已吊销");
        if (key.ExpiresAt is not null && key.ExpiresAt.Value <= DateTime.UtcNow) return (false, "已过期");
        return (true, "有效");
    }
}
