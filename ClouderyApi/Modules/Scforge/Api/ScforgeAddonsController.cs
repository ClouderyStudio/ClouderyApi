using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Domain;
using ClouderyApi.Modules.Scforge.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Scforge.Api;

/// <summary>
/// 资源目录与发布接口（路由前缀 <c>/scforge/addons</c>）。
///
/// 「资源」= 插件（<c>kind=plugin</c>，仅服务端）与模组（<c>kind=mod</c>，会下发到客户端）两类，
/// 共用同一棵资源树与同一套接口，靠 <c>kind</c> 字段（查询参数 / 响应字段）区分；
/// 界面上的「插件」「模组」两个板块只是对 <c>kind</c> 的预设筛选。
///
/// 读取接口匿名可用（只看已通过审核的内容），登录后额外回传「我的投票」；
/// 写入接口要求 Cookie 会话，**发布与编辑只属于作者本人**；
/// 管理员的内容管理走 <c>/scforge/admin</c>。
/// </summary>
[Route("scforge/addons")]
public sealed class ScforgeAddonsController(
    ScforgePluginAppService plugins,
    ScforgeVoteAppService votes,
    ScforgeAdminAccessor adminAccessor,
    ScforgeApiKeyAccessor apiKeys,
    ScforgeCurrentUser current) : ScforgeControllerBase
{
    /// <summary>分页搜索：q / category / tag / gameVersion / sort / kind / page / pageSize。kind 省略即插件与模组都返回。</summary>
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

    /// <summary>首页精选：显式精选优先，不足时按净评分补齐；kind 可限定只看插件或只看模组。</summary>
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

    /// <summary>最近更新；kind 可限定只看插件或只看模组。</summary>
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

    /// <summary>当前用户发布的资源（含待审核与已驳回）。</summary>
    [HttpGet("mine")]
    public Task<IActionResult> Mine(CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            apiKeys.Require(ScforgeApiKeyScopes.Read);
            var items = await plugins.MineAsync(ToActor(current), cancellationToken);
            return Ok(new { items });
        });

    /// <summary>「我的资源」页的统计卡（含三种审核状态的数量）。</summary>
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
            var addon = await plugins.GetAsync(idOrSlug, ToActor(current), admin, cancellationToken);
            return Ok(new { addon });
        });

    /// <summary>
    /// 发布新资源（multipart/form-data）。提交后进入待审核，管理员通过后才对公众可见。
    /// kind 决定是插件（plugin）还是模组（mod），创建后不可更改，包文件按 kind 校验扩展名。
    /// 请求体上限按「资源包上限 + 图片预算」放宽，否则大包会被 Kestrel 的 30MB 默认值挡下。
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(80L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 80L * 1024 * 1024)]
    public Task<IActionResult> Create([FromForm] ScforgeAddonCreateForm form, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            // API Key 通道要求 publish 作用域；Cookie 会话不受此限（网页后台照常发布）。
            apiKeys.Require(ScforgeApiKeyScopes.Publish);
            var addon = await plugins.CreateAsync(form, ToActor(current), cancellationToken);
            return Ok(new { success = true, addon });
        });

    /// <summary>编辑资源资料（作者，multipart）：编辑会重新进入待审核。</summary>
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(32L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 32L * 1024 * 1024)]
    public Task<IActionResult> Update(
        Guid id,
        [FromForm] ScforgeAddonEditForm form,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            // 与创建同属"发布"：上传新版本会进入待审核。
            apiKeys.Require(ScforgeApiKeyScopes.Publish);
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var addon = await plugins.UpdateAsync(id, form, actor, admin, cancellationToken);
            return Ok(new { success = true, addon });
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
            apiKeys.Require(ScforgeApiKeyScopes.Publish);
            var version = await plugins.AddVersionAsync(id, form, ToActor(current), cancellationToken);
            return Ok(new { success = true, version });
        });

    /// <summary>把被驳回的资源重新提交审核。</summary>
    [HttpPost("{id:guid}/resubmit")]
    public Task<IActionResult> Resubmit(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            apiKeys.Require(ScforgeApiKeyScopes.Manage);
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var addon = await plugins.ResubmitAsync(id, actor, admin, cancellationToken);
            return Ok(new { success = true, addon });
        });

    /// <summary>删除资源（连同版本、评论与投票）。</summary>
    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            apiKeys.Require(ScforgeApiKeyScopes.Manage);
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            await plugins.DeleteAsync(id, actor, admin, cancellationToken);
            return Ok(new { success = true, message = "资源已删除" });
        });

    /// <summary>当前用户对某资源的投票状态（匿名可调，恒为 0）。</summary>
    [HttpGet("{id:guid}/vote")]
    public Task<IActionResult> VoteStatus(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var addon = await plugins.GetAsync(id.ToString(), ToActor(current), admin, cancellationToken);
            return Ok(new
            {
                upvotes = addon.Upvotes,
                downvotes = addon.Downvotes,
                score = addon.Score,
                myVote = addon.MyVote,
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
