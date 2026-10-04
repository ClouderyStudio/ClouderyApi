using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Scforge.Api;

/// <summary>后台维护游戏版本（仅超级管理员可写）。</summary>
[Route("scforge/admin/game-versions")]
public sealed class ScforgeAdminGameVersionsController(
    ScforgeGameVersionAppService gameVersions,
    ScforgeAdminAccessor adminAccessor,
    ScforgeCurrentUser current) : ScforgeControllerBase
{
    /// <summary>后台列表：带引用计数，便于判断能否删除。</summary>
    [HttpGet]
    public Task<IActionResult> List(CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var items = await gameVersions.ListForAdminAsync(admin, cancellationToken);
            return Ok(new { items });
        });

    /// <summary>添加一个版本（如 x26.08.01，可标记为内测）。</summary>
    [HttpPost]
    public Task<IActionResult> Create([FromBody] ScforgeGameVersionIn body, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var created = await gameVersions.CreateAsync(body, ToActor(current), admin, cancellationToken);
            return Ok(new { success = true, version = created });
        });

    /// <summary>删除一个版本；仍被资源引用时会被拒绝。</summary>
    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            await gameVersions.DeleteAsync(id, admin, cancellationToken);
            return Ok(new { success = true });
        });
}
