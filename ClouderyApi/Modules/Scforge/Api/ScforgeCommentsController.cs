using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Scforge.Api;

/// <summary>
/// 评论与评论投票（路由前缀 <c>/scforge</c>）。
///
/// 读取匿名可用（未登录时 myVote 恒为 0、canEdit / canDelete 为 false）；
/// 写入要求 Cookie 会话；编辑仅限作者，删除允许作者或管理员（巡查）。
/// 只有已通过审核的资源（插件 / 模组）对公众开放评论。
/// </summary>
[Route("scforge")]
public sealed class ScforgeCommentsController(
    ScforgeCommentAppService comments,
    ScforgeVoteAppService votes,
    ScforgeAdminAccessor adminAccessor,
    ScforgeCurrentUser current) : ScforgeControllerBase
{
    /// <summary>资源的评论树（顶层评论 + 嵌套回复）。</summary>
    [HttpGet("addons/{addonId:guid}/comments")]
    public Task<IActionResult> List(Guid addonId, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            return Ok(await comments.ListAsync(addonId, actor, admin, cancellationToken));
        });

    /// <summary>发表评论；带 parentId 时是对某条评论的回复。</summary>
    [HttpPost("addons/{addonId:guid}/comments")]
    public Task<IActionResult> Create(
        Guid addonId,
        [FromBody] ScforgeCommentCreateIn body,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var comment = await comments.CreateAsync(addonId, body, actor, admin, cancellationToken);
            return Ok(new { success = true, comment });
        });

    /// <summary>编辑自己的评论。</summary>
    [HttpPatch("comments/{id:guid}")]
    public Task<IActionResult> Update(
        Guid id,
        [FromBody] ScforgeCommentUpdateIn body,
        CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            var comment = await comments.UpdateAsync(id, body, actor, admin, cancellationToken);
            return Ok(new { success = true, comment });
        });

    /// <summary>删除评论及其全部回复。</summary>
    [HttpDelete("comments/{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            var actor = ToActor(current);
            var admin = await adminAccessor.ResolveAsync(cancellationToken);
            await comments.DeleteAsync(id, actor, admin, cancellationToken);
            return Ok(new { success = true, message = "评论已删除" });
        });

    [HttpPut("comments/{id:guid}/vote/up")]
    public Task<IActionResult> VoteUp(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () => Ok(await votes.VoteCommentAsync(id, ScforgeVoteDirection.Up, ToActor(current), cancellationToken)));

    [HttpPut("comments/{id:guid}/vote/down")]
    public Task<IActionResult> VoteDown(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () => Ok(await votes.VoteCommentAsync(id, ScforgeVoteDirection.Down, ToActor(current), cancellationToken)));

    [HttpDelete("comments/{id:guid}/vote")]
    public Task<IActionResult> VoteClear(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(async () => Ok(await votes.VoteCommentAsync(id, ScforgeVoteDirection.Clear, ToActor(current), cancellationToken)));
}
