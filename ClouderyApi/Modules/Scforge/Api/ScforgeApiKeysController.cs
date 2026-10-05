using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Application.Mapping;
using ClouderyApi.Modules.Scforge.Domain;
using ClouderyApi.Modules.Scforge.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Scforge.Api;

/// <summary>
/// API Key 的**自助**管理（路由前缀 <c>/scforge/api-keys</c>）。
///
/// 与后台的 <c>/scforge/admin/api-keys</c> 分开：这里只管「登录用户管自己的 Key」，
/// 后台是超管巡检全站（含他人 Key 的吊销）。
///
/// 认证方式与 SCForge 其余写端点一致：不挂 <c>[Authorize]</c>，由
/// <see cref="ScforgeControllerBase"/> 的异常翻译产出
/// <c>{"detail":"…"}</c>（统一错误体）；调用者身份由 Cookie 会话或 API Key 两条通道之一提供。
/// </summary>
[Route("scforge/api-keys")]
public sealed class ScforgeApiKeysController(
    ScforgeApiKeyAppService keys,
    ScforgeCurrentUser current) : ScforgeControllerBase
{
    /// <summary>可授予的作用域清单（含中文名与说明），供界面渲染勾选项。</summary>
    [HttpGet("scopes")]
    public IActionResult Scopes() => Ok(new
    {
        all = ScforgeApiKeyScopes.All,
        selfService = ScforgeApiKeyScopes.SelfService,
        items = ScforgeApiKeyScopes.All
            .Select(s => new
            {
                key = s,
                label = ScforgeApiKeyScopes.Labels.GetValueOrDefault(s, s),
                description = ScforgeApiKeyScopes.Descriptions.GetValueOrDefault(s, string.Empty),
            })
            .ToList(),
    });

    /// <summary>我的 Key 列表（含已吊销，便于确认"那把旧的真的失效了"）。</summary>
    [HttpGet]
    public Task<IActionResult> Mine(CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var userId = ParseUserId();
            var mine = await keys.ListForUserAsync(userId, cancellationToken);
            return Ok(new { items = ScforgeApiKeyMapper.ToOutList(mine) });
        });

    /// <summary>
    /// 签发一把新 Key。**响应里的 token 只出现这一次** —— 请立刻保存，之后无法再取回。
    /// </summary>
    [HttpPost]
    public Task<IActionResult> Create(
        [FromBody] ScforgeApiKeyCreateForm form,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            // 管理作用域不开放自助申请：能改能删的机器凭据必须由超管在后台签发。
            var requested = ScforgeApiKeyScopes.From(form.Scopes)
                .Where(s => ScforgeApiKeyScopes.SelfService.Contains(s, StringComparer.Ordinal))
                .ToList();

            var (key, token) = await keys.CreateAsync(
                ParseUserId(),
                current.DisplayName,
                form.Name ?? "未命名",
                requested,
                form.ExpiresAt,
                current.CasdoorId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                // 一次性明文，配套的 key 已经是掩码形态。
                token,
                key = ScforgeApiKeyMapper.ToOut(key),
                notice = "令牌只显示这一次，请立即保存；之后任何接口（包括超管）都无法取回。",
            });
        });

    /// <summary>吊销自己的某把 Key。</summary>
    [HttpPost("{id:guid}/revoke")]
    public Task<IActionResult> Revoke(
        Guid id,
        [FromBody] ScforgeApiKeyRevokeForm? form,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            // ownerUserId 非 null = 只能动自己的 Key。
            var ok = await keys.RevokeAsync(id, ParseUserId(), form?.Reason, cancellationToken);
            if (!ok)
            {
                throw new ScforgeApiException(404, "找不到这把 API Key，或它不属于你");
            }
            return Ok(new { success = true });
        });

    /// <summary>
    /// 轮换：吊销旧的、签发一把同权限的新 Key（用于泄露后一键换锁）。
    /// 响应同样只带一次明文。
    /// </summary>
    [HttpPost("{id:guid}/rotate")]
    public Task<IActionResult> Rotate(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var userId = ParseUserId();
            var mine = await keys.ListForUserAsync(userId, cancellationToken);
            var old = mine.FirstOrDefault(k => k.Id == id);
            if (old is null)
            {
                throw new ScforgeApiException(404, "找不到这把 API Key，或它不属于你");
            }

            var (key, token) = await keys.RotateAsync(old, current.CasdoorId, cancellationToken);
            return Ok(new
            {
                success = true,
                token,
                key = ScforgeApiKeyMapper.ToOut(key),
                notice = "旧 Key 已吊销，新令牌只显示这一次。",
            });
        });

    /// <summary>
    /// 取当前身份的 <c>UserId</c>；未登录或类型不符一律按 401 处理。
    /// 单独抽出来是因为这些端点都靠它拿"我是谁"，而 ClaimTypes.NameIdentifier 存的是字符串。
    /// </summary>
    private Guid ParseUserId()
    {
        var raw = current.UserId;
        if (!IsAuthenticated(raw) || !Guid.TryParse(raw, out var userId))
        {
            throw new ScforgeApiException(401, "请先登录");
        }
        return userId;
    }

    private static bool IsAuthenticated(string? raw) => !string.IsNullOrEmpty(raw);
}
