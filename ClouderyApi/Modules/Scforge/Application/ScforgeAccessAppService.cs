using System.Security.Cryptography;
using System.Text;
using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Domain;
using ClouderyApi.Modules.Scforge.Infrastructure;
using ClouderyApi.Shared.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Modules.Scforge.Application;

/// <summary>
/// 隐私访问的判定与凭据签发。
///
/// **为什么单独成服务**：访问判定横跨详情、下载、评论、投票多个入口，
/// 散落各处必然漂移（详情放行但下载 403 之类）。规则只在这里写一次。
///
/// 口令模式的凭据用**签名令牌**而不是服务端会话：令牌自带 HMAC 签名，
/// 校验是纯计算、不查库，因此口令改了一律失效（旧令牌签名对不上），
/// 也不需要额外的表或缓存来存「谁解锁过」。
/// </summary>
public sealed class ScforgeAccessAppService(
    IScforgeDbContext db,
    ScforgeAccessHasher hasher,
    IUserDirectory directory,
    IOptions<ScforgeOptions> options,
    ILogger<ScforgeAccessAppService> logger)
{
    /// <summary>解锁令牌头部：<c>X-Scforge-Access</c>。</summary>
    public const string TokenHeader = "X-Scforge-Access";

    private readonly ScforgeOptions _options = options.Value;

    /* ============================ 查询 ============================ */

    /// <summary>
    /// 调用者能否访问这个插件。
    /// 判定顺序：作者/管理员豁免 → 公开模式放行 → 按模式校验凭据。
    /// </summary>
    /// <param name="unlocked">调用者是否已提交过正确的解锁令牌（由 <see cref="IsUnlocked"/> 得出）。</param>
    public async Task<bool> CanAccessAsync(
        ScforgePlugin plugin,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        bool unlocked,
        CancellationToken cancellationToken = default)
    {
        // 作者本人与管理员在任何隐私模式下都放行：管理员要审核它，作者当然要看自己的东西。
        if (actor.IsAuthorOf(plugin.AuthorId) || admin.IsAdmin) return true;
        if (plugin.AccessMode == ScforgeAccessMode.Public) return true;

        if (plugin.AccessMode == ScforgeAccessMode.Password) return unlocked;

        // whitelist：必须登录（匿名没有 userId 可比）。
        if (!actor.IsAuthenticated) return false;

        return await db.ScforgeAccessGrants
            .AsNoTracking()
            .AnyAsync(g => g.PluginId == plugin.Id && g.UserId == actor.UserId, cancellationToken);
    }

    /// <summary>
    /// 访问被拒时抛 403。用 403 而不是 404 是刻意的：
    /// 隐私插件**本来就该**通过 slug 直达（作者要把链接发出去），
    /// 所以「存在但你没权限」不是需要隐藏的信息。
    ///
    /// **只用在下载与写操作上**：详情页无权时返回的是 200 + 脱敏外壳
    /// （见 <c>ScforgePluginAppService.GetAsync</c>），因为页面得渲染出解锁门；
    /// 下载没有「解锁门」这个交互，拿不到就是拿不到。
    /// </summary>
    public static void Deny(ScforgePlugin plugin) => throw new ScforgeApiException(
        403,
        plugin.AccessMode == ScforgeAccessMode.Password
            ? "该插件需要访问口令，请先解锁"
            : "该插件仅对指定人员可见");

    /* ============================ 凭据 ============================ */

    /// <summary>
    /// 校验当前请求携带的解锁令牌是否对这个插件有效。
    /// 令牌缺失、签名不符、过期、插件已换口令 —— 一律当作未解锁。
    /// </summary>
    public bool IsUnlocked(ScforgePlugin plugin, string? token)
    {
        if (plugin.AccessMode != ScforgeAccessMode.Password) return false;
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return false;

            // 格式：base64url(payload).base64url(expiresUnix).base64url(HMAC)
            // 签名覆盖的是**原始三段的前两段**，不是解码后的明文 ——
            // 签发端签的就是编码后的字符串，这里若拿明文去签会对不上，所有令牌一律失效。
            var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            var expiresRaw = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            var signature = Base64UrlDecode(parts[2]);

            var expected = Sign($"{parts[0]}.{parts[1]}");
            // 定长比较，避免按字节提前返回泄露信息。
            if (!CryptographicOperations.FixedTimeEquals(signature, expected)) return false;

            if (!long.TryParse(expiresRaw, out var expiresUnix)) return false;
            if (DateTimeOffset.FromUnixTimeSeconds(expiresUnix) <= DateTimeOffset.UtcNow) return false;

            // payload 形如 {pluginId}:{口令指纹}：插件要对得上，口令指纹要跟着当前口令走。
            var separator = payload.IndexOf(':');
            if (separator <= 0) return false;

            var pluginId = payload[..separator];
            var fingerprint = payload[(separator + 1)..];

            return string.Equals(pluginId, plugin.Id.ToString("D"), StringComparison.OrdinalIgnoreCase)
                   && string.Equals(fingerprint, PasswordFingerprint(plugin.AccessPasswordHash), StringComparison.Ordinal);
        }
        catch
        {
            // 令牌被截断或篡改成非法 base64：按未解锁处理，不把异常抛给调用方。
            return false;
        }
    }

    /// <summary>
    /// 校验口令并签发解锁令牌。
    /// 口令错时**不区分「插件不存在」与「口令不对」以外的信息**，也不回显任何提示。
    /// </summary>
    public async Task<ScforgeAccessUnlockDto> UnlockAsync(
        Guid pluginId,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var plugin = await db.ScforgePlugins
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pluginId, cancellationToken)
            ?? throw new ScforgeApiException(404, "插件不存在或已被删除");

        if (plugin.AccessMode != ScforgeAccessMode.Password)
        {
            // 非口令模式不需要解锁：public 直接放行，whitelist 走名单判定。
            throw new ScforgeRuleException("该插件不需要口令访问");
        }

        var candidate = (password ?? string.Empty);
        if (candidate.Length > ScforgeAccessMode.MaxPasswordLength)
        {
            throw new ScforgeRuleException($"口令不能超过 {ScforgeAccessMode.MaxPasswordLength} 个字符");
        }

        if (!hasher.Verify(candidate, plugin.AccessPasswordHash))
        {
            // 统一文案：不泄露「插件存在但口令不对」以外的东西，也不提示长度/格式规则。
            logger.LogWarning("SCForge：口令解锁失败，插件 {Slug}", plugin.Slug);
            throw new ScforgeApiException(403, "口令不正确");
        }

        var ttl = TimeSpan.FromMinutes(Math.Clamp(_options.AccessTokenMinutes, 5, 24 * 60));
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);

        var payload = $"{plugin.Id:D}:{PasswordFingerprint(plugin.AccessPasswordHash)}";
        var payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var expiresPart = Base64UrlEncode(Encoding.UTF8.GetBytes(expiresAt.ToUnixTimeSeconds().ToString()));
        var token = $"{payloadPart}.{expiresPart}.{Base64UrlEncode(Sign($"{payloadPart}.{expiresPart}"))}";

        logger.LogInformation("SCForge：插件 {Slug} 口令解锁成功，有效期至 {Expires:O}", plugin.Slug, expiresAt);

        return new ScforgeAccessUnlockDto
        {
            Success = true,
            Token = token,
            ExpiresAt = expiresAt,
            AccessMode = plugin.AccessMode,
        };
    }

    /* ============================ 名单维护 ============================ */

    /// <summary>
    /// 白名单编辑器用的「搜人」。
    ///
    /// 名单里存的是 Identity 域的用户 GUID，让作者手填 GUID 是不人道的，
    /// 因此这里按用户名 / 邮箱模糊搜出候选。**任何登录用户都能搜**——
    /// 用户名与邮箱在站内本就可见（评论、审核列表都在用），
    /// 但这层「谁在给谁开权限」的语义只对插件作者有意义，搜索结果本身不含任何隐私内容。
    /// </summary>
    public async Task<List<ScforgeAccessCandidateDto>> SearchCandidatesAsync(
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        var users = await directory.SearchAsync(keyword, 20, cancellationToken);

        return users
            .Select(u => new ScforgeAccessCandidateDto
            {
                UserId = u.Id.ToString(),
                Username = u.Username,
                Email = u.Email,
                Avatar = u.Avatar,
            })
            .ToList();
    }

    /// <summary>返回插件的授权名单（仅作者本人 / 有内容管理权限的管理员可读）。</summary>
    public async Task<List<ScforgeAccessGrantDto>> ListGrantsAsync(
        Guid pluginId,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanManageAsync(pluginId, actor, admin, cancellationToken);

        var rows = await db.ScforgeAccessGrants
            .AsNoTracking()
            .Where(g => g.PluginId == pluginId)
            .OrderBy(g => g.UserName)
            .ThenBy(g => g.UserId)
            .ToListAsync(cancellationToken);

        return rows.Select(g => new ScforgeAccessGrantDto
        {
            UserId = g.UserId,
            Username = g.UserName,
            CreatedAt = g.CreatedAt,
        }).ToList();
    }

    /// <summary>把名单整体替换为给定的用户集合（作者在编辑页增删后一次提交）。</summary>
    public async Task<List<ScforgeAccessGrantDto>> ReplaceGrantsAsync(
        Guid pluginId,
        List<string>? userIds,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        var plugin = await EnsureCanManageAsync(pluginId, actor, admin, cancellationToken);

        var ids = (userIds ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count > ScforgeCatalog.MaxAccessGrants)
        {
            throw new ScforgeRuleException($"授权名单最多 {ScforgeCatalog.MaxAccessGrants} 人");
        }

        // 作者把自己加进去没有意义（本来就放行），反而会让名单看起来有内容。
        ids.RemoveAll(u => string.Equals(u, plugin.AuthorId, StringComparison.Ordinal));

        var existing = await db.ScforgeAccessGrants
            .Where(g => g.PluginId == pluginId)
            .ToListAsync(cancellationToken);

        // 只在 whitelist 模式下有意义；其它模式清空，避免「切回来授权还在」的误解。
        if (plugin.AccessMode != ScforgeAccessMode.Whitelist)
        {
            if (existing.Count > 0) db.ScforgeAccessGrants.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);
            return [];
        }

        var keep = ids.ToHashSet(StringComparer.Ordinal);
        var stale = existing.Where(g => !keep.Contains(g.UserId)).ToList();
        if (stale.Count > 0) db.ScforgeAccessGrants.RemoveRange(stale);

        var already = existing.Where(g => keep.Contains(g.UserId)).Select(g => g.UserId).ToHashSet(StringComparer.Ordinal);
        foreach (var userId in ids.Where(u => !already.Contains(u)))
        {
            db.ScforgeAccessGrants.Add(new ScforgeAccessGrant
            {
                PluginId = pluginId,
                UserId = userId,
                // 用户名留待作者在后台补齐快照；这里用 Id 兜底，列表仍能显示。
                UserName = userId,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("SCForge：{Actor} 更新了 {Slug} 的授权名单（{Count} 人）", actor.DisplayName, plugin.Slug, ids.Count);

        return await ListGrantsAsync(pluginId, actor, admin, cancellationToken);
    }

    /* ============================ 应用到插件 ============================ */

    /// <summary>
    /// 切换访问模式，顺带把口令哈希 / 授权名单收拾干净。
    /// 每次改动都走这里，保证「模式」与「配套数据」不会各说各话。
    /// </summary>
    internal async Task ApplyModeAsync(
        ScforgePlugin plugin,
        string mode,
        string? newPassword,
        string? hint,
        bool passwordProvided,
        CancellationToken cancellationToken)
    {
        var previous = plugin.AccessMode;
        plugin.AccessMode = mode;

        if (mode == ScforgeAccessMode.Password)
        {
            if (passwordProvided)
            {
                var password = newPassword ?? string.Empty;
                if (password.Length < ScforgeAccessMode.MinPasswordLength)
                {
                    throw new ScforgeRuleException($"访问口令至少 {ScforgeAccessMode.MinPasswordLength} 个字符");
                }
                plugin.AccessPasswordHash = hasher.Hash(password);
            }
            else if (previous != ScforgeAccessMode.Password || string.IsNullOrEmpty(plugin.AccessPasswordHash))
            {
                // 首次切到口令模式必须给口令，否则这个插件谁都进不去。
                throw new ScforgeRuleException("设为口令访问时必须填写访问口令");
            }

            plugin.AccessHint = string.IsNullOrWhiteSpace(hint) ? null : hint.Trim();
        }
        else
        {
            // 切走时清空口令哈希：留一份 PBKDF2 在库里没有意义，只是白给攻击者离线爆破的靶子。
            plugin.AccessPasswordHash = null;
            plugin.AccessHint = null;
        }

        var grants = await db.ScforgeAccessGrants
            .Where(g => g.PluginId == plugin.Id)
            .ToListAsync(cancellationToken);

        if (mode != ScforgeAccessMode.Whitelist && grants.Count > 0)
        {
            db.ScforgeAccessGrants.RemoveRange(grants);
        }
    }

    /* ============================ 内部 ============================ */

    private async Task<ScforgePlugin> EnsureCanManageAsync(
        Guid pluginId,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken)
    {
        actor.RequireUserId();

        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == pluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");

        if (!actor.IsAuthorOf(plugin.AuthorId) && !admin.CanManageContent)
        {
            throw new ScforgeApiException(403, "只有作者（或有内容管理权限的管理员）可以调整访问名单");
        }

        return plugin;
    }

    /// <summary>
    /// 口令指纹：令牌里带一段它，短哈希即可（用途是「口令有没有被换过」的判据，
    /// 不是保密凭据），换口令后旧令牌自然失效。
    /// </summary>
    private static string PasswordFingerprint(string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return "none";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(hash));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }

    private byte[] Sign(string value) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.AccessTokenSecret), Encoding.UTF8.GetBytes(value));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            0 => padded,
            _ => throw new FormatException("非法的 base64url 长度"),
        };
        return Convert.FromBase64String(padded);
    }
}
