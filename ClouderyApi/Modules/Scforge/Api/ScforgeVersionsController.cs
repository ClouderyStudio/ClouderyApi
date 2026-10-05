using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Scforge.Api;

/// <summary>
/// 版本接口（路由前缀 <c>/scforge/versions</c>）：
/// 下载 + 作者侧维护（改元数据 / 换文件 / 删除 / 重新提交审核）。
/// 维护动作只属于作者，且改动后版本会回到待审核。
/// </summary>
[Route("scforge/versions")]
public sealed class ScforgeVersionsController(
    ScforgePluginAppService plugins,
    ScforgeAdminAccessor adminAccessor,
    ScforgeCurrentUser current) : ScforgeControllerBase
{
    /// <summary>
    /// 下载某个版本。下载入口统一走这里（而不是静态目录），
    /// 这样未通过审核的版本不会被直接拿到，下载次数也才有唯一口径。
    ///
    /// 隐私插件的包**同样只能从这里走**，且必须带解锁令牌或在授权名单内 ——
    /// 拿到 versionId 的人可以直接打这个端点绕过界面，所以访问判定必须落在这里，
    /// 不能只依赖详情页的脱敏。
    /// </summary>
    [HttpGet("{id:guid}/download")]
    public Task<IActionResult> Download(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var token = Request.Headers.TryGetValue(ScforgeAccessAppService.TokenHeader, out var raw)
                ? raw.ToString()
                : null;

            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var download = await plugins.OpenDownloadAsync(id, ToActor(current), admin, token, cancellationToken);
            // File(...) 会按 RFC 5987 生成 filename*，中文文件名也能正确落地。
            return File(download.Stream, "application/octet-stream", download.FileName);
        });

    /// <summary>编辑版本元数据（仅作者）：重新进入待审核。</summary>
    [HttpPatch("{id:guid}")]
    public Task<IActionResult> Update(
        Guid id,
        [FromBody] ScforgeVersionEditIn body,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var version = await plugins.UpdateVersionAsync(id, body, ToActor(current), cancellationToken);
            return Ok(new { success = true, version });
        });

    /// <summary>替换版本的插件包文件（仅作者）：重新进入待审核。</summary>
    [HttpPost("{id:guid}/file")]
    [RequestSizeLimit(80L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 80L * 1024 * 1024)]
    public Task<IActionResult> ReplaceFile(
        Guid id,
        [FromForm] ScforgeVersionFileForm form,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var version = await plugins.ReplaceVersionFileAsync(id, form, ToActor(current), cancellationToken);
            return Ok(new { success = true, version });
        });

    /// <summary>把被驳回的版本重新提交审核。</summary>
    [HttpPost("{id:guid}/resubmit")]
    public Task<IActionResult> Resubmit(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var version = await plugins.ResubmitVersionAsync(id, ToActor(current), cancellationToken);
            return Ok(new { success = true, version });
        });

    /// <summary>删除一个版本（仅作者）。删除是下架动作，不需要审核。</summary>
    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            await plugins.DeleteVersionAsync(id, ToActor(current), cancellationToken);
            return Ok(new { success = true, message = "版本已删除" });
        });
}
