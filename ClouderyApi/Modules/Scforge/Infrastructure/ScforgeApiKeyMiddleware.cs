using System.Security.Claims;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Scforge.Infrastructure;

/// <summary>
/// 把 <c>Authorization: Bearer scf_…</c> 里的 SCForge API Key 换成一条已认证的 Claims 身份。
///
/// 为什么做成中间件而不是改 <see cref="ScforgeCurrentUser"/>：
/// 后者从 Cookie 会话的 Claims 取身份，是同步构造的；Key 的校验需要查库（异步）。
/// 中间件在 <c>UseAuthentication</c> 之后、控制器之前跑一次，
/// 校验通过就写入 <c>HttpContext.User</c>，于是
/// 「作者本人」判定、<see cref="Application.ScforgeActor"/>、应用层用例全部零改动。
///
/// 三条硬规则：
///   1. **Cookie 会话优先**：已经登录了就当 Cookie 用户，不看 Bearer —— 避免用 Key 冒充降级。
///   2. 只认本模块的令牌前缀 <c>scf_</c>，别的 Bearer（如 MHOP JWT）原样放行给后续处理器。
///   3. 校验失败**不直接拒绝**，而是让请求以匿名身份继续 —— 由各写端点按既有约定返回
///      <c>401 {"success":false,"message":"请先登录"}</c>，保持对外错误形状不变（AGENTS.md 硬约束）。
///   4. 查库本身失败（表未迁移、连接抖动）同样按"校验失败"处理：降级为匿名并记警告。
///      本中间件在管道早期、对**所有**端点执行，一旦让它把异常抛出去，整个站点的公开接口
///      都会被一个伪造的 Authorization 头打成 500 —— 那是可被匿名触发的放大故障。
/// </summary>
public sealed class ScforgeApiKeyMiddleware(RequestDelegate next)
{
    /// <summary>本模块令牌的固定前缀；用于快速识别与在流量里做封禁扫描。定义在 Domain 层，与签发方共用。</summary>
    public const string TokenPrefix = ScforgeApiKeyScopes.TokenPrefix;

    public async Task InvokeAsync(HttpContext context, IScforgeDbContext db, ILogger<ScforgeApiKeyMiddleware> logger)
    {
        // 规则 1：Cookie 已认证则不动，避免 Key 覆盖真实登录态。
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var token = ReadToken(context);
            if (token is not null)
            {
                ScforgeApiKey? key;
                // 规则 4：查库失败降级为匿名（fail-closed），绝不把异常抛进管道。
                try
                {
                    key = await ResolveAsync(db, token, context.RequestAborted);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    throw; // 客户端主动断开：正常取消，不必降级
                }
                catch (Exception ex)
                {
                    key = null;
                    logger.LogWarning(ex, "SCForge API Key 校验查库失败，本次请求按匿名处理（请确认 scforge_api_keys 表已迁移）");
                }

                if (key is not null)
                {
                    context.User = BuildPrincipal(key);
                }
            }
        }

        await next(context);
    }

    /// <summary>从 Authorization 头取出 <c>scf_</c> 令牌；不是本模块的令牌返回 null。</summary>
    private static string? ReadToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return null;

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var token = header[prefix.Length..].Trim();
        return token.StartsWith(TokenPrefix, StringComparison.Ordinal) ? token : null;
    }

    /// <summary>按哈希查 Key，并校验吊销与过期。返回 null 表示不可用。</summary>
    private static async Task<ScforgeApiKey?> ResolveAsync(IScforgeDbContext db, string token, CancellationToken cancellationToken)
    {
        if (token.Length < 20) return null; // 明显短于合法长度，直接省掉一次查库

        var record = await db.ScforgeApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == ScforgeApiKeyScopes.HashToken(token), cancellationToken);

        if (record is null) return null;
        if (record.RevokedAt is not null) return null;

        // 过期判定用 UTC，与全库时间约定一致。
        if (record.ExpiresAt is not null && record.ExpiresAt.Value <= DateTime.UtcNow) return null;

        return record;
    }

    /// <summary>把 Key 换成一个「作者本人」身份，让 <c>IsAuthorOf</c> 继续成立。</summary>
    private static ClaimsPrincipal BuildPrincipal(ScforgeApiKey key)
    {
        var identity = new ClaimsIdentity(
            [
                // ClaimTypes.NameIdentifier = Identity 域 Users.Id，与 Cookie 会话同键，
                // 这样 ScforgeCurrentUser / ScforgeAdminAccessor 的解析逻辑完全复用。
                new Claim(ClaimTypes.NameIdentifier, key.UserId.ToString()),
                new Claim(ClaimTypes.Name, key.UserName),
                // 刻意**不写** CasdoorId 那个 Claim：
                // ScforgeAdminAccessor 的第一判据是"CasdoorId 命中 Authorization:Admins 白名单 → 超管"，
                // 那条引导通道只应服务于网页登录。留空则 Key 请求会落到第二判据（按 UserId 查
                // scforge_admins 表），超管的身份记录本就在表里，权限判定依然正确 ——
                // 即机器凭据不会因为白名单而被凭空提权。
                // 标记这是机器凭据：控制器可据此要求作用域（scope），并让后台审计显示来源。
                new Claim(ScforgeApiKeyClaim.Method, "api-key"),
                new Claim(ScforgeApiKeyClaim.KeyId, key.Id.ToString()),
                new Claim(ScforgeApiKeyClaim.Prefix, key.Prefix),
                new Claim(ScforgeApiKeyClaim.Scopes, string.Join(',', ScforgeApiKeyScopes.Parse(key.Scopes))),
            ],
            // 第二个参数是认证类型：非 null 即视为已认证，配合上面的认证方案名区分来源。
            authenticationType: "ScforgeApiKey");

        return new ClaimsPrincipal(identity);
    }
}

/// <summary>API Key 通道写入 HttpContext.User 的自定义 Claim 类型名。</summary>
public static class ScforgeApiKeyClaim
{
    public const string Method = "scforge:auth-method";
    public const string KeyId = "scforge:key-id";
    public const string Prefix = "scforge:key-prefix";
    public const string Scopes = "scforge:scopes";
}
