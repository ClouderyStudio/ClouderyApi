using ClouderyApi.Modules.Mhop.Api.Contracts;
using ClouderyApi.Modules.Mhop.Application;
using ClouderyApi.Shared.Filters;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Mhop.Api;

/// <summary>
/// 论坛互助：板块、发帖、人类回复、点赞。
/// 发帖 / 回复先经 AI 自动审核：通过即直接公开；未通过或 AI 不可用时转人工审核并记录理由。
/// AI 自动回复随帖子自动公开时生成（见 MhopContentReviewService）。
/// 对应 Python 后端 routers/forum.py。
/// 本控制器只做 HTTP 绑定与 MhopOk / MhopStatus 包装，用例编排见 ForumAppService，映射见 MhopForumMapper。
/// </summary>
[ApiController]
[Route("mhop/forum")]
public class MhopForumController : MhopControllerBase
{
    private readonly ForumAppService _forum;

    public MhopForumController(ForumAppService forum)
    {
        _forum = forum;
    }

    // ---------------- 读取接口 ----------------

    [HttpGet("boards")]
    public async Task<IActionResult> Boards() => MhopOk(await _forum.BoardsAsync());

    [HttpGet("stats")]
    public async Task<IActionResult> PublicStats() => MhopOk(await _forum.PublicStatsAsync());

    [HttpGet("posts")]
    public async Task<IActionResult> ListPosts(
        [FromQuery] int page = 1,
        [FromQuery] int size = 10,
        [FromQuery] string keyword = "",
        [FromQuery] string board = "",
        [FromQuery] string sort = "latest")
        => MhopOk(await _forum.ListPostsAsync(page, size, keyword, board, sort));

    [HttpGet("posts/{postId:int}")]
    public async Task<IActionResult> GetPost(
        int postId,
        // 前端传的是 inc_view=1；ASP.NET 的 bool 绑定不接受 "1"，这里按字符串解析，
        // 兼容 FastAPI 的 1/true/yes/on 语义。
        [FromQuery(Name = "inc_view")] string? incView = null)
        => MhopOk(await _forum.GetPostAsync(postId, IsTruthy(incView)));

    // ---------------- 写入接口 ----------------

    /// <summary>
    /// 发帖。每次发帖都会触发一次 AI 自动审核（外加可能的 AI 自动回复），按 IP 限流保护模型开销。
    /// </summary>
    [HttpPost("posts")]
    [IpRateLimit(MaxRequests = 20, WindowSeconds = 300)]
    public async Task<IActionResult> CreatePost([FromBody] PostIn body)
        => MhopStatus(201, await _forum.CreatePostAsync(body));

    /// <summary>人类回复。同样会触发 AI 审核，按 IP 限流。</summary>
    [HttpPost("posts/{postId:int}/replies")]
    [IpRateLimit(MaxRequests = 30, WindowSeconds = 300)]
    public async Task<IActionResult> CreateReply(int postId, [FromBody] ReplyIn body)
        => MhopStatus(201, await _forum.CreateReplyAsync(postId, body));

    [HttpPost("likes/toggle")]
    public async Task<IActionResult> ToggleLike([FromBody] LikeIn body)
        => MhopOk(await _forum.ToggleLikeAsync(body));

    [HttpGet("likes/mine")]
    public async Task<IActionResult> MyLikes([FromQuery(Name = "target_type")] string targetType)
        => MhopOk(await _forum.MyLikesAsync(targetType));

    // ---------------- 作者自管理（个人主页：查看 / 编辑 / 撤回审核 / 重新提交 / 删除） ----------------

    /// <summary>个人主页统计：按状态汇总我的帖子 / 回复数量。</summary>
    [HttpGet("mine/summary")]
    public async Task<IActionResult> MySummary() => MhopOk(await _forum.MySummaryAsync());

    /// <summary>我的帖子（含审核中 / 已驳回 / 草稿），status 为空返回全部。</summary>
    [HttpGet("mine/posts")]
    public async Task<IActionResult> MyPosts(
        [FromQuery] int? status = null, [FromQuery] int page = 1, [FromQuery] int size = 10)
        => MhopOk(await _forum.MyPostsAsync(status, page, size));

    /// <summary>我的回复（含审核中 / 已驳回 / 草稿），status 为空返回全部。</summary>
    [HttpGet("mine/replies")]
    public async Task<IActionResult> MyReplies(
        [FromQuery] int? status = null, [FromQuery] int page = 1, [FromQuery] int size = 10)
        => MhopOk(await _forum.MyRepliesAsync(status, page, size));

    /// <summary>编辑自己的帖子：仅待审核 / 草稿可改，已通过或已驳回只能删除。</summary>
    [HttpPut("posts/{postId:int}")]
    public async Task<IActionResult> UpdatePost(int postId, [FromBody] PostIn body)
        => MhopOk(await _forum.UpdatePostAsync(postId, body, HttpContext.RequestAborted));

    /// <summary>取消审核：待审核 → 草稿，转为仅自己可见并可继续编辑。</summary>
    [HttpPost("posts/{postId:int}/withdraw")]
    public async Task<IActionResult> WithdrawPost(int postId)
        => MhopOk(await _forum.WithdrawPostAsync(postId));

    /// <summary>重新提交审核：草稿 → 待审核。</summary>
    [HttpPost("posts/{postId:int}/submit")]
    public async Task<IActionResult> SubmitPost(int postId)
        => MhopOk(await _forum.SubmitPostAsync(postId));

    /// <summary>删除自己的帖子：任意状态均可，连同其回复、点赞、AI 日志与图片一并清理。</summary>
    [HttpDelete("posts/{postId:int}")]
    public async Task<IActionResult> DeletePost(int postId)
        => MhopOk(await _forum.DeletePostAsync(postId, HttpContext.RequestAborted));

    /// <summary>编辑自己的回复：仅待审核 / 草稿可改。</summary>
    [HttpPut("replies/{replyId:int}")]
    public async Task<IActionResult> UpdateReply(int replyId, [FromBody] ReplyIn body)
        => MhopOk(await _forum.UpdateReplyAsync(replyId, body, HttpContext.RequestAborted));

    /// <summary>取消审核：待审核回复 → 草稿。</summary>
    [HttpPost("replies/{replyId:int}/withdraw")]
    public async Task<IActionResult> WithdrawReply(int replyId)
        => MhopOk(await _forum.WithdrawReplyAsync(replyId));

    /// <summary>重新提交审核：草稿回复 → 待审核。</summary>
    [HttpPost("replies/{replyId:int}/submit")]
    public async Task<IActionResult> SubmitReply(int replyId)
        => MhopOk(await _forum.SubmitReplyAsync(replyId));

    /// <summary>删除自己的回复：任意状态均可，连同其点赞、AI 日志与图片一并清理。</summary>
    [HttpDelete("replies/{replyId:int}")]
    public async Task<IActionResult> DeleteReply(int replyId)
        => MhopOk(await _forum.DeleteReplyAsync(replyId, HttpContext.RequestAborted));

    /// <summary>FastAPI 兼容的真值判定：1 / true / yes / on（忽略大小写与空白）。</summary>
    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }
}
