using System.Security.Claims;
using ClouderyApi.Modules.Scforge.Domain;

namespace ClouderyApi.Modules.Scforge.Infrastructure;

/// <summary>
/// 把「当前请求是否由 API Key 发起、这把 Key 有哪些作用域」这件事收敛到一处。
///
/// 控制器用它做两件事：
///   1. 写端点要求特定作用域（发布要 <c>publish</c>，编辑要 <c>manage</c>）——
///      **Cookie 会话默认全通过**，作用域只约束机器凭据，不影响网页后台的正常操作。
///   2. 生成响应时回带 <c>X-Scforge-Auth</c> 头，脚本能自查自己走的是哪条通道。
/// </summary>
public sealed class ScforgeApiKeyAccessor(IHttpContextAccessor accessor)
{
    /// <summary>当前请求是否由 API Key 认证（false = Cookie 会话或匿名）。</summary>
    public bool IsApiKey =>
        string.Equals(
            accessor.HttpContext?.User.FindFirst(ScforgeApiKeyClaim.Method)?.Value,
            "api-key",
            StringComparison.Ordinal);

    /// <summary>当前这把 Key 的作用域；Cookie 会话返回全部（网页操作不受限）。</summary>
    public IReadOnlyList<string> Scopes
    {
        get
        {
            var raw = accessor.HttpContext?.User.FindFirst(ScforgeApiKeyClaim.Scopes)?.Value;
            if (string.IsNullOrWhiteSpace(raw)) return ScforgeApiKeyScopes.All;
            return ScforgeApiKeyScopes.From(raw.Split(',', StringSplitOptions.RemoveEmptyEntries));
        }
    }

    /// <summary>是否允许当前身份执行要求 <paramref name="scope"/> 的操作。</summary>
    public bool Allows(string scope) => !IsApiKey || Scopes.Contains(scope, StringComparer.Ordinal);

    /// <summary>
    /// 要求某个作用域；不足则抛 403 业务异常（由 <c>ScforgeControllerBase.GuardAsync</c> 翻译成
    /// <c>{"success":false,"message":…}</c>，与既有错误形状一致）。
    ///
    /// 注意抛 403 而不是 401：身份是有效的，只是这把 Key 的权限不够 ——
    /// 这与「没登录」是两种不同的运维问题，脚本据此能区分是密钥失效还是范围不够。
    /// </summary>
    public void Require(string scope)
    {
        if (!Allows(scope))
        {
            throw new ScforgeApiException(403, $"当前 API Key 缺少「{ScforgeApiKeyScopes.Labels.GetValueOrDefault(scope, scope)}」作用域");
        }
    }
}
