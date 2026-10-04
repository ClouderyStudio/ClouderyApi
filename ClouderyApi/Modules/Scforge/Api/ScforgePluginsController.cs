using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Scforge.Api;

/// <summary>
/// 插件目录与发布接口（路由前缀 <c>/scforge/plugins</c>）。
///
/// 读取接口匿名可用（只看已通过审核的内容），登录后额外回传「我的投票」；
/// 写入接口要求 Cookie 会话，**发布与编辑只属于作者本人**；
/// 管理员的内容管理走 <c>/scforge/admin</c>。
/// </summary>
[Route("scforge/plugins")]
public sealed class ScforgePluginsController(
    ScforgePluginAppService plugins,
    ScforgeVoteAppService votes,
    ScforgeAdminAccessor adminAccessor,
    ScforgeCurrentUser current) : ScforgeControllerBase
{
    /// <summary>分页搜索：q / category / tag / gameVersion / sort / page / pageSize。</summary>
    [HttpGet]
    public Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] string? tag,
        [FromQuery] string? gameVersion,
        [FromQuery] string? sort,
        [FromQuery] string? kind,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var result = await plugins.SearchAsync(
                new ScforgeSearchQuery(q, category, tag, gameVersion, sort ?? "relevance", page, pageSize, kind),
                ToActor(current),
                cancellationToken);
            return Ok(result);
        });

    /// <summary>首页精选：显式精选优先，不足时按净评分补齐。</summary>
    [HttpGet("featured")]
    public Task<IActionResult> Featured(
        [FromQuery] int limit = 6,
        [FromQuery] string? kind = null,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var items = await plugins.FeaturedAsync(limit, kind, ToActor(current), cancellationToken);
            return Ok(new { items });
        });

    /// <summary>最近更新。</summary>
    [HttpGet("recent")]
    public Task<IActionResult> Recent(
        [FromQuery] int limit = 8,
        [FromQuery] string? kind = null,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var items = await plugins.RecentAsync(limit, kind, ToActor(current), cancellationToken);
            return Ok(new { items });
        });

    /// <summary>当前用户发布的插件（含待审核与已驳回）。</summary>
    [HttpGet("mine")]
    public Task<IActionResult> Mine(CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var items = await plugins.MineAsync(ToActor(current), cancellationToken);
            return Ok(new { items });
        });

    /// <summary>「我的插件」页的统计卡（含三种审核状态的数量）。</summary>
    [HttpGet("mine/summary")]
    public Task<IActionResult> MineSummary(CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var summary = await plugins.MineSummaryAsync(ToActor(current), cancellationToken);
            return Ok(summary);
        });

    /// <summary>详情：<paramref name="idOrSlug"/> 接受 GUID 或 slug。</summary>
    [HttpGet("{idOrSlug}")]
    public Task<IActionResult> Detail(string idOrSlug, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var plugin = await plugins.GetAsync(idOrSlug, ToActor(current), admin, cancellationToken);
            return Ok(new { plugin });
        });

    /// <summary>
    /// 发布新插件（multipart/form-data）。提交后进入待审核，管理员通过后才对公众可见。
    /// 请求体上限按「插件包上限 + 图片预算」放宽，否则大包会被 Kestrel 的 30MB 默认值挡下。
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(80L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 80L * 1024 * 1024)]
    public Task<IActionResult> Create([FromForm] ScforgePluginCreateForm form, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var plugin = await plugins.CreateAsync(form, ToActor(current), cancellationToken);
            return Ok(new { success = true, plugin });
        });

    /// <summary>编辑插件资料（作者，multipart）：编辑会重新进入待审核。</summary>
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(32L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 32L * 1024 * 1024)]
    public Task<IActionResult> Update(
        Guid id,
        [FromForm] ScforgePluginEditForm form,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var plugin = await plugins.UpdateAsync(id, form, actor, admin, cancellationToken);
            return Ok(new { success = true, plugin });
        });

    /// <summary>追加一个版本（multipart）：新版本进入待审核。</summary>
    [HttpPost("{id:guid}/versions")]
    [RequestSizeLimit(80L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 80L * 1024 * 1024)]
    public Task<IActionResult> AddVersion(
        Guid id,
        [FromForm] ScforgeVersionCreateForm form,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var version = await plugins.AddVersionAsync(id, form, ToActor(current), cancellationToken);
            return Ok(new { success = true, version });
        });

    /// <summary>把被驳回的插件重新提交审核。</summary>
    [HttpPost("{id:guid}/resubmit")]
    public Task<IActionResult> Resubmit(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var plugin = await plugins.ResubmitAsync(id, actor, admin, cancellationToken);
            return Ok(new { success = true, plugin });
        });

    /// <summary>删除插件（连同版本、评论与投票）。</summary>
    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            await plugins.DeleteAsync(id, actor, admin, cancellationToken);
            return Ok(new { success = true, message = "插件已删除" });
        });

    /// <summary>当前用户对某插件的投票状态（匿名可调，恒为 0）。</summary>
    [HttpGet("{id:guid}/vote")]
    public Task<IActionResult> VoteStatus(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var plugin = await plugins.GetAsync(id.ToString(), ToActor(current), admin, cancellationToken);
            return Ok(new
            {
                upvotes = plugin.Upvotes,
                downvotes = plugin.Downvotes,
                score = plugin.Score,
                myVote = plugin.MyVote,
            });
        });

    [HttpPut("{id:guid}/vote/up")]
    public Task<IActionResult> VoteUp(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () => Ok(await votes.VotePluginAsync(id, ScforgeVoteDirection.Up, ToActor(current), cancellationToken)));

    [HttpPut("{id:guid}/vote/down")]
    public Task<IActionResult> VoteDown(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () => Ok(await votes.VotePluginAsync(id, ScforgeVoteDirection.Down, ToActor(current), cancellationToken)));

    /// <summary>撤销投票（幂等：没有票也算成功）。</summary>
    [HttpDelete("{id:guid}/vote")]
    public Task<IActionResult> VoteClear(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () => Ok(await votes.VotePluginAsync(id, ScforgeVoteDirection.Clear, ToActor(current), cancellationToken)));
}
