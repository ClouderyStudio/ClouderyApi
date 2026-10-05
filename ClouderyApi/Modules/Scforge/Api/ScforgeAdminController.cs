using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Scforge.Api;

/// <summary>
/// 管理后台接口（路由前缀 <c>/scforge/admin</c>）。
///
/// 全部端点都要求登录，并按权限细分：
///   • <c>me</c>：任何登录用户都可调，用来判断要不要显示后台入口；
///   • <c>summary</c> / <c>addons</c>（全量）：管理员；
///   • 审核队列与审核动作：review 权限；
///   • 编辑 / 删除任意资源：content 权限；
///   • 管理员与权限管理：超级管理员。
/// </summary>
[Route("scforge/admin")]
public sealed class ScforgeAdminController(
    ScforgeAdminAppService app,
    ScforgePluginAppService plugins,
    ScforgeAdminAccessor adminAccessor,
    ScforgeCurrentUser current) : ScforgeControllerBase
{
    /// <summary>当前用户的后台身份（未登录或非管理员时 isAdmin=false，便于前端做守卫）。</summary>
    [HttpGet("me")]
    public Task<IActionResult> Me(CancellationToken cancellationToken = default) =>
        GuardAsync(async () => Ok(await app.GetContextAsync(cancellationToken)));

    [HttpGet("summary")]
    public Task<IActionResult> Summary(CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            return Ok(await app.SummaryAsync(admin, cancellationToken));
        });

    /// <summary>审核队列：资源（插件 / 模组，默认待审核）。</summary>
    [HttpGet("review/addons")]
    public Task<IActionResult> ReviewAddons(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            return Ok(await app.ReviewQueuePluginsAsync(status, page, pageSize, actor, admin, cancellationToken));
        });

    /// <summary>审核队列：版本（默认待审核）。</summary>
    [HttpGet("review/versions")]
    public Task<IActionResult> ReviewVersions(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            return Ok(await app.ReviewQueueVersionsAsync(status, page, pageSize, admin, cancellationToken));
        });

    /// <summary>审核一个资源提交：通过或驳回（驳回必须给理由）。</summary>
    [HttpPost("addons/{id:guid}/review")]
    public Task<IActionResult> ReviewAddon(
        Guid id,
        [FromBody] ScforgeReviewIn body,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var addon = await app.ReviewPluginAsync(id, body, actor, admin, cancellationToken);
            return Ok(new { success = true, addon });
        });

    /// <summary>审核一个版本提交。</summary>
    [HttpPost("versions/{id:guid}/review")]
    public Task<IActionResult> ReviewVersion(
        Guid id,
        [FromBody] ScforgeReviewIn body,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var version = await app.ReviewVersionAsync(id, body, admin, cancellationToken);
            return Ok(new { success = true, version });
        });

    /// <summary>全量资源列表（含待审 / 已驳回），供内容管理员巡检。</summary>
    [HttpGet("addons")]
    public Task<IActionResult> AllAddons(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            return Ok(await app.AllPluginsAsync(q, status, page, pageSize, actor, admin, cancellationToken));
        });

    /// <summary>内容管理：编辑任意资源资料（multipart）。</summary>
    [HttpPut("addons/{id:guid}")]
    [RequestSizeLimit(32L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 32L * 1024 * 1024)]
    public Task<IActionResult> UpdateAddon(
        Guid id,
        [FromForm] ScforgeAddonEditForm form,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var addon = await plugins.UpdateAsync(id, form, actor, admin, cancellationToken);
            return Ok(new { success = true, addon });
        });

    /// <summary>内容管理：删除资源。</summary>
    [HttpDelete("addons/{id:guid}")]
    public Task<IActionResult> DeleteAddon(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            await plugins.DeleteAsync(id, actor, admin, cancellationToken);
            return Ok(new { success = true, message = "资源已删除" });
        });

    /// <summary>按用户名 / 邮箱搜索可指定的用户（仅超管）。</summary>
    [HttpGet("users")]
    public Task<IActionResult> SearchUsers(
        [FromQuery] string? q,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var items = await app.SearchUsersAsync(q, admin, cancellationToken);
            return Ok(new { items });
        });

    /// <summary>管理员列表（仅超管）。</summary>
    [HttpGet("admins")]
    public Task<IActionResult> ListAdmins(CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var items = await app.ListAdminsAsync(admin, cancellationToken);
            return Ok(new { items });
        });

    /// <summary>指定管理员或调整其角色与权限（仅超管）。</summary>
    [HttpPost("admins")]
    public Task<IActionResult> UpsertAdmin(
        [FromBody] ScforgeAdminIn body,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var record = await app.UpsertAdminAsync(body, actor, admin, cancellationToken);
            return Ok(new { success = true, admin = record });
        });

    /// <summary>撤销一名管理员（仅超管）。</summary>
    [HttpDelete("admins/{id:guid}")]
    public Task<IActionResult> DeleteAdmin(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            await app.DeleteAdminAsync(id, actor, admin, cancellationToken);
            return Ok(new { success = true, message = "已撤销该管理员" });
        });
}
