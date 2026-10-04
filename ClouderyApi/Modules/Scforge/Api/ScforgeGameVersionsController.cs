using ClouderyApi.Modules.Scforge.Application;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Scforge.Api;

/// <summary>受支持的游戏版本（公开只读）：发布页与筛选面板的取值来源。</summary>
[Route("scforge/game-versions")]
public sealed class ScforgeGameVersionsController(ScforgeGameVersionAppService gameVersions) : ScforgeControllerBase
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken cancellationToken = default) =>
        GuardAsync(async () => Ok(new { items = await gameVersions.ListAsync(cancellationToken) }));
}
