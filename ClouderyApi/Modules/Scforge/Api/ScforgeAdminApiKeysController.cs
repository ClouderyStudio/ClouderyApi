using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Application.Mapping;
using ClouderyApi.Modules.Scforge.Domain;
using ClouderyApi.Modules.Scforge.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Scforge.Api;

/// <summary>
/// API Key 的后台管理（路由前缀 <c>/scforge/admin/api-keys</c>，**仅超级管理员**）。
///
/// 与自助端点 <c>/scforge/api-keys</c> 的分工：
///   • 自助：登录用户管自己的 Key，作用域不能含「管理」；
///   • 后台：超管巡检全站（谁签了、什么时候用过、还剩几把），
///     并可为**指定用户**签发带管理作用域的 Key —— 这是自助流程做不到的唯一能力。
///
/// 鉴权沿用 <see cref="ScforgeAdminContext.RequireSuper"/>，与既有的管理员管理一致。
/// </summary>
[Route("scforge/admin/api-keys")]
public sealed class ScforgeAdminApiKeysController(
    ScforgeApiKeyAppService keys,
    ScforgeAdminAccessor adminAccessor,
    ScforgeCurrentUser current) : ScforgeControllerBase
{
    /// <summary>全站 Key 列表（含已吊销），供后台巡检。</summary>
    [HttpGet]
    public Task<IActionResult> List(CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            admin.RequireSuper();
            var all = await keys.ListAllAsync(cancellationToken);
            return Ok(new { items = ScforgeApiKeyMapper.ToOutList(all) });
        });

    /// <summary>
    /// 为指定用户签发一把 Key（超管）。可授予「管理」作用域。
    /// </summary>
    [HttpPost]
    public Task<IActionResult> CreateForUser(
        [FromQuery] Guid userId,
        [FromQuery] string userName,
        [FromBody] ScforgeApiKeyCreateForm form,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            admin.RequireSuper();

            if (userId == Guid.Empty)
            {
                throw new ScforgeRuleException("缺少 userId");
            }

            var (key, token) = await keys.CreateAsync(
                userId,
                string.IsNullOrWhiteSpace(userName) ? "未知用户" : userName,
                form.Name ?? "未命名",
                form.Scopes,
                form.ExpiresAt,
                current.CasdoorId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                token,
                key = ScforgeApiKeyMapper.ToOut(key),
                notice = "令牌只显示这一次，请立即转交本人。",
            });
        });

    /// <summary>吊销任意一把 Key（超管）。用户自助吊销走 <c>/scforge/api-keys/{id}/revoke</c>。</summary>
    [HttpPost("{id:guid}/revoke")]
    public Task<IActionResult> Revoke(
        Guid id,
        [FromBody] ScforgeApiKeyRevokeForm? form,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            admin.RequireSuper();

            // ownerUserId 传 null = 超管视角，可动任何人的 Key。
            var ok = await keys.RevokeAsync(id, null, form?.Reason ?? "管理员吊销", cancellationToken);
            if (!ok)
            {
                throw new ScforgeApiException(404, "找不到这把 API Key");
            }
            return Ok(new { success = true, message = "已吊销" });
        });

    /// <summary>轮换任意一把 Key（超管）。响应只带一次明文。</summary>
    [HttpPost("{id:guid}/rotate")]
    public Task<IActionResult> Rotate(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            admin.RequireSuper();

            var all = await keys.ListAllAsync(cancellationToken);
            var old = all.FirstOrDefault(k => k.Id == id);
            if (old is null)
            {
                throw new ScforgeApiException(404, "找不到这把 API Key");
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
}
