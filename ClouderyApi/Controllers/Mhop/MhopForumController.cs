using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>
/// 论坛互助：板块、发帖、人类回复、点赞。
/// 发帖 / 回复先经 AI 自动审核：通过即直接公开；未通过或 AI 不可用时转人工审核并记录理由。
/// AI 自动回复随帖子自动公开时生成（见 MhopContentReviewService）。
/// 对应 Python 后端 routers/forum.py。
/// </summary>
[ApiController]
[Route("mhop/forum")]
public class MhopForumController : MhopControllerBase
{
    private readonly MhopDbContext _db;
    private readonly MhopCurrentUserAccessor _current;
    private readonly MhopOnlineTracker _online;
    private readonly MhopUploadService _uploads;
    private readonly MhopContentService _content;
    private readonly MhopContentReviewService _review;

    public MhopForumController(
        MhopDbContext db,
        MhopCurrentUserAccessor current,
        MhopOnlineTracker online,
        MhopUploadService uploads,
        MhopContentService content,
        MhopContentReviewService review)
    {
        _db = db;
        _current = current;
        _online = online;
        _uploads = uploads;
        _content = content;
        _review = review;
    }

    // ---------------- 读取接口 ----------------

    [HttpGet("boards")]
    public async Task<IActionResult> Boards()
    {
        var counts = await _db.MhopPosts
            .Where(p => p.Status == ContentStatus.Published)
            .GroupBy(p => p.Board)
            .Select(g => new { Board = g.Key, Count = g.Count() })
            .ToListAsync();
        var countMap = counts.ToDictionary(x => x.Board, x => x.Count);

        // 全部使用小写键名，蛇形命名策略不会改写，保证与 Python 返回一致
        var result = MhopBoards.All.Select(b => new
        {
            slug = b.Slug,
            name = b.Name,
            color = b.Color,
            desc = b.Desc,
            count = countMap.GetValueOrDefault(b.Slug, 0),
        });
        return MhopOk(result);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> PublicStats() => MhopOk(new
    {
        posts = await _db.MhopPosts.CountAsync(p => p.Status == ContentStatus.Published),
        replies = await _db.MhopReplies.CountAsync(r => r.Status == ContentStatus.Published),
        users = await _db.MhopUsers.CountAsync(),
        online = _online.Count(),
    });

    [HttpGet("posts")]
    public async Task<IActionResult> ListPosts(
        [FromQuery] int page = 1,
        [FromQuery] int size = 10,
        [FromQuery] string keyword = "",
        [FromQuery] string board = "",
        [FromQuery] string sort = "latest")
    {
        if (page < 1) page = 1;
        size = Math.Clamp(size, 1, 50);

        var query = _db.MhopPosts.Where(p => p.Status == ContentStatus.Published);
        var trimmedKeyword = (keyword ?? string.Empty).Trim();
        if (trimmedKeyword.Length > 0)
            query = query.Where(p => p.Content.Contains(trimmedKeyword));
        if (!string.IsNullOrWhiteSpace(board))
        {
            if (!MhopBoards.Slugs.Contains(board))
                throw new MhopApiException(400, "板块不存在");
            query = query.Where(p => p.Board == board);
        }

        if (sort == "new")
        {
            query = query.OrderByDescending(p => p.CreatedAt);
        }
        else
        {
            // 最新回复：取每帖最新一条已通过且未撤回回复的时间，无回复则按发帖时间
            query = query.OrderByDescending(p =>
                _db.MhopReplies
                    .Where(r => r.PostId == p.Id && r.Status == ContentStatus.Published && !r.Recalled)
                    .Select(r => (DateTime?)r.CreatedAt)
                    .Max() ?? p.CreatedAt);
        }

        var total = await query.CountAsync();
        var posts = await query.Skip((page - 1) * size).Take(size).ToListAsync();

        var postIds = posts.Select(p => p.Id).ToList();
        var replies = postIds.Count == 0
            ? new List<MhopReply>()
            : await _db.MhopReplies.Where(r => postIds.Contains(r.PostId)).ToListAsync();
        var repliesByPost = replies.GroupBy(r => r.PostId).ToDictionary(g => g.Key, g => g.ToList());

        var current = await _current.GetOptionalAsync();
        var users = await LoadAuthorMapAsync(posts, replies);
        var likeCounts = await LikeCountMapAsync(LikeTargetType.Post, postIds);
        var liked = await LikedSetAsync(current?.Id, LikeTargetType.Post, postIds);

        var items = posts.Select(p => ToPostOut(
            p,
            users,
            repliesByPost.GetValueOrDefault(p.Id, []),
            current?.Id,
            likeCounts,
            liked)).ToList();

        return MhopOk(new PostListOut { Total = total, Page = page, Size = size, Items = items });
    }

    [HttpGet("posts/{postId:int}")]
    public async Task<IActionResult> GetPost(
        int postId,
        // 前端传的是 inc_view=1；ASP.NET 的 bool 绑定不接受 "1"，这里按字符串解析，
        // 兼容 FastAPI 的 1/true/yes/on 语义。
        [FromQuery(Name = "inc_view")] string? incView = null)
    {
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null || post.Status != ContentStatus.Published)
            throw new MhopApiException(404, "帖子不存在或正在审核中");

        if (IsTruthy(incView))
        {
            post.AddView();
            await _db.SaveChangesAsync();
        }

        // 已撤回的 AI 回复保留占位（正文在 _reply_out 中屏蔽），AI 回复置顶
        var replies = await _db.MhopReplies
            .Where(r => r.PostId == postId && r.Status == ContentStatus.Published)
            .OrderBy(r => r.IsAi ? 0 : 1)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync();

        var current = await _current.GetOptionalAsync();
        var users = await LoadAuthorMapAsync([post], replies);
        var replyIds = replies.Select(r => r.Id).ToList();
        var replyLikeCounts = await LikeCountMapAsync(LikeTargetType.Reply, replyIds);
        var replyLiked = await LikedSetAsync(current?.Id, LikeTargetType.Reply, replyIds);
        var postLikes = await LikeCountMapAsync(LikeTargetType.Post, [post.Id]);
        var postLiked = await LikedSetAsync(current?.Id, LikeTargetType.Post, [post.Id]);

        var detail = new PostDetailOut();
        CopyPostFields(detail, ToPostOut(post, users, replies, current?.Id, postLikes, postLiked));
        detail.Replies = replies.Select(r => ToReplyOut(r, users, replyLikeCounts, replyLiked)).ToList();
        return MhopOk(detail);
    }

    // ---------------- 写入接口 ----------------

    [HttpPost("posts")]
    public async Task<IActionResult> CreatePost([FromBody] PostIn body)
    {
        var current = await _current.RequirePhoneVerifiedAsync();
        var content = ContentText.ForPost(body.Content);
        var board = BoardSlug.Create(body.Board);

        var crisis = MhopModeration.DetectCrisis(content.Value);
        var images = MhopImageRefs.Normalize(body.Images);
        // 先送 AI 自动审核：通过即公开，否则转人工
        var post = MhopPost.NewAuthorPost(current.Id, body.IsAnonymous, content, board, images, crisis);
        _db.MhopPosts.Add(post);
        await _db.SaveChangesAsync();

        // AI 自动审核异步执行：通过则自动公开并生成 AI 回复，未通过则保留待审核并记录理由
        _review.QueuePostReview(post.Id);

        var users = new Dictionary<int, MhopUser> { [current.Id] = current };
        return MhopStatus(201, ToPostOut(post, users, [], current.Id, null, null));
    }

    [HttpPost("posts/{postId:int}/replies")]
    public async Task<IActionResult> CreateReply(int postId, [FromBody] ReplyIn body)
    {
        var current = await _current.RequirePhoneVerifiedAsync();
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        // 草稿仅作者可见，禁止他人（含作者本人）对其回复
        if (post is null || post.Status is ContentStatus.Rejected or ContentStatus.Draft)
            throw new MhopApiException(404, "帖子不存在或已被移除");

        var content = ContentText.ForReply(body.Content);
        var crisis = MhopModeration.DetectCrisis(content.Value);
        var sensitiveWords = MhopModeration.HitSensitive(content.Value);
        var images = MhopImageRefs.Normalize(body.Images);
        var reply = MhopReply.NewAuthorReply(postId, current.Id, body.IsAnonymous, content, images, crisis);
        // 命中违规词直接拦截，否则待审核
        if (sensitiveWords.Count > 0) reply.RejectBySensitiveWords(sensitiveWords);
        _db.MhopReplies.Add(reply);
        await _db.SaveChangesAsync();

        // 命中敏感词已直接驳回；其余送 AI 自动审核，通过即公开，否则转人工
        if (reply.Status == ContentStatus.Pending) _review.QueueReplyReview(reply.Id);

        var users = new Dictionary<int, MhopUser> { [current.Id] = current };
        return MhopStatus(201, ToReplyOut(reply, users, null, null));
    }

    [HttpPost("likes/toggle")]
    public async Task<IActionResult> ToggleLike([FromBody] LikeIn body)
    {
        var current = await _current.RequireAsync();
        var target = LikeTargetType.Parse(body.TargetType);

        if (target.IsPost)
        {
            var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == body.TargetId);
            if (post is null || post.Status is ContentStatus.Rejected or ContentStatus.Draft)
                throw new MhopApiException(404, "内容不存在或已被移除");
        }
        else
        {
            var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == body.TargetId);
            if (reply is null || reply.Status == ContentStatus.Rejected)
                throw new MhopApiException(404, "内容不存在或已被移除");
            if (reply.Status != ContentStatus.Published || reply.Recalled)
                throw new MhopApiException(400, "该回复暂不可点赞");
        }

        var existing = await _db.MhopLikes.FirstOrDefaultAsync(l =>
            l.UserId == current.Id && l.TargetType == target.Value && l.TargetId == body.TargetId);

        var liked = existing is null;
        if (existing is not null) _db.MhopLikes.Remove(existing);
        else _db.MhopLikes.Add(new MhopLike
        {
            UserId = current.Id,
            TargetType = target.Value,
            TargetId = body.TargetId,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var count = await _db.MhopLikes.CountAsync(l =>
            l.TargetType == target.Value && l.TargetId == body.TargetId);
        return MhopOk(new { liked, like_count = count });
    }

    [HttpGet("likes/mine")]
    public async Task<IActionResult> MyLikes([FromQuery(Name = "target_type")] string targetType)
    {
        var target = LikeTargetType.Parse(targetType);

        var current = await _current.GetOptionalAsync();
        if (current is null) return MhopOk(new { ids = Array.Empty<int>() });

        var ids = await _db.MhopLikes
            .Where(l => l.UserId == current.Id && l.TargetType == target.Value)
            .Select(l => l.TargetId)
            .ToListAsync();
        return MhopOk(new { ids });
    }

    // ---------------- 作者自管理（个人主页：查看 / 编辑 / 撤回审核 / 重新提交 / 删除） ----------------

    /// <summary>个人主页统计：按状态汇总我的帖子 / 回复数量。</summary>
    [HttpGet("mine/summary")]
    public async Task<IActionResult> MySummary()
    {
        var current = await _current.RequireAsync();
        var postRows = await _db.MhopPosts.Where(p => p.UserId == current.Id)
            .GroupBy(p => p.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var replyRows = await _db.MhopReplies.Where(r => r.UserId == current.Id)
            .GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var postMap = postRows.ToDictionary(x => x.Status, x => x.Count);
        var replyMap = replyRows.ToDictionary(x => x.Status, x => x.Count);

        return MhopOk(new
        {
            posts = new
            {
                total = postMap.Values.Sum(),
                pending = postMap.GetValueOrDefault(ContentStatus.Pending, 0),
                published = postMap.GetValueOrDefault(ContentStatus.Published, 0),
                rejected = postMap.GetValueOrDefault(ContentStatus.Rejected, 0),
                draft = postMap.GetValueOrDefault(ContentStatus.Draft, 0),
            },
            replies = new
            {
                total = replyMap.Values.Sum(),
                pending = replyMap.GetValueOrDefault(ContentStatus.Pending, 0),
                published = replyMap.GetValueOrDefault(ContentStatus.Published, 0),
                rejected = replyMap.GetValueOrDefault(ContentStatus.Rejected, 0),
                draft = replyMap.GetValueOrDefault(ContentStatus.Draft, 0),
            },
        });
    }

    /// <summary>我的帖子（含审核中 / 已驳回 / 草稿），status 为空返回全部。</summary>
    [HttpGet("mine/posts")]
    public async Task<IActionResult> MyPosts(
        [FromQuery] int? status = null, [FromQuery] int page = 1, [FromQuery] int size = 10)
    {
        var current = await _current.RequireAsync();
        if (page < 1) page = 1;
        size = Math.Clamp(size, 1, 50);

        var query = _db.MhopPosts.Where(p => p.UserId == current.Id);
        if (status.HasValue)
        {
            if (status.Value is < ContentStatus.Pending or > ContentStatus.Draft) throw new MhopApiException(400, "非法状态");
            query = query.Where(p => p.Status == status.Value);
        }

        var total = await query.CountAsync();
        var posts = await query.OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * size).Take(size).ToListAsync();

        var postIds = posts.Select(p => p.Id).ToList();
        var replies = postIds.Count == 0
            ? new List<MhopReply>()
            : await _db.MhopReplies.Where(r => postIds.Contains(r.PostId)).ToListAsync();
        var repliesByPost = replies.GroupBy(r => r.PostId).ToDictionary(g => g.Key, g => g.ToList());
        var likeCounts = await LikeCountMapAsync(LikeTargetType.Post, postIds);

        var items = posts.Select(p =>
        {
            var postReplies = repliesByPost.GetValueOrDefault(p.Id, []);
            return new
            {
                id = p.Id,
                content = p.Content,
                board = p.Board,
                status = p.Status,
                crisis = p.Crisis,
                is_anonymous = p.IsAnonymous,
                images = ParseImages(p.Images),
                reply_count = postReplies.Count(r => r.Status == ContentStatus.Published && !r.Recalled),
                view_count = p.ViewCount,
                like_count = likeCounts.GetValueOrDefault(p.Id, 0),
                review_note = p.ReviewNote ?? string.Empty,
                ai_flag = p.AiFlag ?? string.Empty,
                ai_review_note = p.AiReviewNote ?? string.Empty,
                editable = p.IsEditable,
                can_withdraw = p.CanWithdraw,
                can_submit = p.CanSubmit,
                created_at = p.CreatedAt,
            };
        }).ToList();

        return MhopOk(new { total, page, size, items });
    }

    /// <summary>我的回复（含审核中 / 已驳回 / 草稿），status 为空返回全部。</summary>
    [HttpGet("mine/replies")]
    public async Task<IActionResult> MyReplies(
        [FromQuery] int? status = null, [FromQuery] int page = 1, [FromQuery] int size = 10)
    {
        var current = await _current.RequireAsync();
        if (page < 1) page = 1;
        size = Math.Clamp(size, 1, 50);

        var query = _db.MhopReplies.Where(r => r.UserId == current.Id);
        if (status.HasValue)
        {
            if (status.Value is < ContentStatus.Pending or > ContentStatus.Draft) throw new MhopApiException(400, "非法状态");
            query = query.Where(r => r.Status == status.Value);
        }

        var total = await query.CountAsync();
        var replies = await query.OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * size).Take(size).ToListAsync();

        var postIds = replies.Select(r => r.PostId).Distinct().ToList();
        var posts = postIds.Count == 0
            ? new Dictionary<int, MhopPost>()
            : await _db.MhopPosts.AsNoTracking().Where(p => postIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var likeCounts = await LikeCountMapAsync(LikeTargetType.Reply, replies.Select(r => r.Id).ToList());

        var items = replies.Select(r =>
        {
            var postContent = posts.TryGetValue(r.PostId, out var parent) ? parent.Content : string.Empty;
            return new
            {
                id = r.Id,
                post_id = r.PostId,
                post_excerpt = postContent.Length > 80 ? postContent[..80] + "…" : postContent,
                // 帖子本身未公开时，回复审核通过也不会公开展示
                post_status = posts.TryGetValue(r.PostId, out var p2) ? p2.Status : (int?)null,
                content = r.Content,
                status = r.Status,
                crisis = r.Crisis,
                is_anonymous = r.IsAnonymous,
                images = ParseImages(r.Images),
                like_count = likeCounts.GetValueOrDefault(r.Id, 0),
                review_note = r.ReviewNote ?? string.Empty,
                ai_flag = r.AiFlag ?? string.Empty,
                ai_review_note = r.AiReviewNote ?? string.Empty,
                editable = r.IsEditable,
                can_withdraw = r.CanWithdraw,
                can_submit = r.CanSubmit,
                created_at = r.CreatedAt,
            };
        }).ToList();

        return MhopOk(new { total, page, size, items });
    }

    /// <summary>编辑自己的帖子：仅待审核 / 草稿可改，已通过或已驳回只能删除。</summary>
    [HttpPut("posts/{postId:int}")]
    public async Task<IActionResult> UpdatePost(int postId, [FromBody] PostIn body)
    {
        var current = await _current.RequireAsync();
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null || post.UserId != current.Id) throw new MhopApiException(404, "帖子不存在");

        var images = MhopImageRefs.Normalize(body.Images);
        // 编辑时被移除的图片此后不再被引用，落库成功后清理存储对象
        var removedImages = post.ApplyAuthorEdit(body.Content, body.Board, body.IsAnonymous, images, Screen(body.Content));

        await _db.SaveChangesAsync();
        // 正文已变更：待审核内容重新送 AI 审核；草稿等作者重新提交时再审
        if (post.Status == ContentStatus.Pending) _review.QueuePostReview(post.Id);
        await _uploads.DeleteAsync(removedImages, HttpContext.RequestAborted);

        return MhopOk(new { ok = true, status = post.Status, crisis = post.Crisis });
    }

    /// <summary>取消审核：待审核 → 草稿，转为仅自己可见并可继续编辑。</summary>
    [HttpPost("posts/{postId:int}/withdraw")]
    public async Task<IActionResult> WithdrawPost(int postId)
    {
        var current = await _current.RequireAsync();
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null || post.UserId != current.Id) throw new MhopApiException(404, "帖子不存在");
        post.Withdraw();
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true, status = post.Status });
    }

    /// <summary>重新提交审核：草稿 → 待审核。</summary>
    [HttpPost("posts/{postId:int}/submit")]
    public async Task<IActionResult> SubmitPost(int postId)
    {
        var current = await _current.RequireAsync();
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null || post.UserId != current.Id) throw new MhopApiException(404, "帖子不存在");
        post.SubmitForReview();
        await _db.SaveChangesAsync();
        _review.QueuePostReview(post.Id);
        return MhopOk(new { ok = true, status = post.Status });
    }

    /// <summary>删除自己的帖子：任意状态均可，连同其回复、点赞、AI 日志与图片一并清理。</summary>
    [HttpDelete("posts/{postId:int}")]
    public async Task<IActionResult> DeletePost(int postId)
    {
        var current = await _current.RequireAsync();
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null || post.UserId != current.Id) throw new MhopApiException(404, "帖子不存在");

        await _content.DeletePostAsync(post, HttpContext.RequestAborted);
        return MhopOk(new { ok = true });
    }

    /// <summary>编辑自己的回复：仅待审核 / 草稿可改。</summary>
    [HttpPut("replies/{replyId:int}")]
    public async Task<IActionResult> UpdateReply(int replyId, [FromBody] ReplyIn body)
    {
        var current = await _current.RequireAsync();
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null || reply.UserId != current.Id) throw new MhopApiException(404, "回复不存在");

        var images = MhopImageRefs.Normalize(body.Images);
        // 编辑时被移除的图片此后不再被引用，落库成功后清理存储对象
        var removedImages = reply.ApplyAuthorEdit(body.Content, body.IsAnonymous, images, Screen(body.Content));

        await _db.SaveChangesAsync();
        if (reply.Status == ContentStatus.Pending) _review.QueueReplyReview(reply.Id);
        await _uploads.DeleteAsync(removedImages, HttpContext.RequestAborted);
        return MhopOk(new { ok = true, status = reply.Status });
    }

    /// <summary>取消审核：待审核回复 → 草稿。</summary>
    [HttpPost("replies/{replyId:int}/withdraw")]
    public async Task<IActionResult> WithdrawReply(int replyId)
    {
        var current = await _current.RequireAsync();
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null || reply.UserId != current.Id) throw new MhopApiException(404, "回复不存在");
        reply.Withdraw();
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true, status = reply.Status });
    }

    /// <summary>重新提交审核：草稿回复 → 待审核。</summary>
    [HttpPost("replies/{replyId:int}/submit")]
    public async Task<IActionResult> SubmitReply(int replyId)
    {
        var current = await _current.RequireAsync();
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null || reply.UserId != current.Id) throw new MhopApiException(404, "回复不存在");
        reply.SubmitForReview();
        await _db.SaveChangesAsync();
        _review.QueueReplyReview(reply.Id);
        return MhopOk(new { ok = true, status = reply.Status });
    }

    /// <summary>删除自己的回复：任意状态均可，连同其点赞、AI 日志与图片一并清理。</summary>
    [HttpDelete("replies/{replyId:int}")]
    public async Task<IActionResult> DeleteReply(int replyId)
    {
        var current = await _current.RequireAsync();
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null || reply.UserId != current.Id) throw new MhopApiException(404, "回复不存在");

        await _content.DeleteReplyAsync(reply, HttpContext.RequestAborted);
        return MhopOk(new { ok = true });
    }

    // ---------------- 映射辅助 ----------------

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    private static List<string> ParseImages(string? raw) => MhopImageRefs.Parse(raw);

    /// <summary>内容安全初筛：危机信号 + 敏感词（领域方法据此改变状态）。</summary>
    private static ContentScreening Screen(string? rawContent)
    {
        var content = (rawContent ?? string.Empty).Trim();
        return new ContentScreening(MhopModeration.DetectCrisis(content), MhopModeration.HitSensitive(content));
    }

    private static (string Author, string Avatar, string Badge) AuthorForPost(
        MhopPost post, IReadOnlyDictionary<int, MhopUser> users)
    {
        if (!post.IsAnonymous && post.UserId.HasValue)
        {
            if (users.TryGetValue(post.UserId.Value, out var user))
                return (user.Username, user.Avatar ?? string.Empty, user.Badge ?? string.Empty);
            return ("实名用户", string.Empty, string.Empty);
        }
        return ("匿名朋友", string.Empty, string.Empty);
    }

    private static (string Author, string Avatar, string Badge) AuthorForReply(
        MhopReply reply, IReadOnlyDictionary<int, MhopUser> users)
    {
        if (reply.IsAi) return ("AI 心理助手", string.Empty, string.Empty);
        if (!reply.IsAnonymous && reply.UserId.HasValue)
        {
            if (users.TryGetValue(reply.UserId.Value, out var user))
                return (user.Username, user.Avatar ?? string.Empty, user.Badge ?? string.Empty);
            return ("实名用户", string.Empty, string.Empty);
        }
        return ("匿名朋友", string.Empty, string.Empty);
    }

    private async Task<Dictionary<int, MhopUser>> LoadAuthorMapAsync(
        IReadOnlyCollection<MhopPost> posts, IReadOnlyCollection<MhopReply> replies)
    {
        var ids = posts.Where(p => !p.IsAnonymous).Select(p => p.UserId)
            .Concat(replies.Where(r => !r.IsAi && !r.IsAnonymous).Select(r => r.UserId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return new Dictionary<int, MhopUser>();

        return await _db.MhopUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);
    }

    private async Task<Dictionary<int, int>> LikeCountMapAsync(LikeTargetType target, List<int> ids)
    {
        if (ids.Count == 0) return new Dictionary<int, int>();
        var rows = await _db.MhopLikes
            .Where(l => l.TargetType == target.Value && ids.Contains(l.TargetId))
            .GroupBy(l => l.TargetId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(x => x.Id, x => x.Count);
    }

    private async Task<HashSet<int>> LikedSetAsync(int? userId, LikeTargetType target, List<int> ids)
    {
        if (!userId.HasValue || ids.Count == 0) return new HashSet<int>();
        var list = await _db.MhopLikes
            .Where(l => l.UserId == userId.Value && l.TargetType == target.Value && ids.Contains(l.TargetId))
            .Select(l => l.TargetId)
            .ToListAsync();
        return list.ToHashSet();
    }

    private static ReplyOut ToReplyOut(
        MhopReply reply,
        IReadOnlyDictionary<int, MhopUser> users,
        IReadOnlyDictionary<int, int>? likeCounts,
        IReadOnlySet<int>? liked)
    {
        var (author, avatar, badge) = AuthorForReply(reply, users);
        return new ReplyOut
        {
            Id = reply.Id,
            PostId = reply.PostId,
            Content = reply.Recalled ? string.Empty : reply.Content,
            Status = reply.Status,
            IsAi = reply.IsAi,
            IsAnonymous = reply.IsAnonymous,
            Author = author,
            AuthorAvatar = avatar,
            AuthorBadge = badge,
            Crisis = reply.Crisis,
            Recalled = reply.Recalled,
            RecallReason = reply.RecallReason ?? string.Empty,
            LikeCount = likeCounts?.GetValueOrDefault(reply.Id, 0) ?? 0,
            Liked = liked?.Contains(reply.Id) ?? false,
            Images = ParseImages(reply.Images),
            CreatedAt = reply.CreatedAt,
        };
    }

    private static PostOut ToPostOut(
        MhopPost post,
        IReadOnlyDictionary<int, MhopUser> users,
        IReadOnlyCollection<MhopReply> replies,
        int? currentUserId,
        IReadOnlyDictionary<int, int>? likeCounts,
        IReadOnlySet<int>? liked)
    {
        var visible = replies.Where(r => r.Status == ContentStatus.Published && !r.Recalled).ToList();
        var last = visible.OrderByDescending(r => r.CreatedAt).FirstOrDefault();
        var (author, avatar, badge) = AuthorForPost(post, users);
        var lastAuthor = last is null ? string.Empty : AuthorForReply(last, users).Author;

        return new PostOut
        {
            Id = post.Id,
            Content = post.Content,
            Board = post.Board,
            Status = post.Status,
            Crisis = post.Crisis,
            IsAnonymous = post.IsAnonymous,
            Author = author,
            AuthorAvatar = avatar,
            AuthorBadge = badge,
            ReplyCount = visible.Count,
            ViewCount = post.ViewCount,
            AiReplied = visible.Any(r => r.IsAi),
            Mine = currentUserId.HasValue && post.UserId == currentUserId.Value,
            LikeCount = likeCounts?.GetValueOrDefault(post.Id, 0) ?? 0,
            Liked = liked?.Contains(post.Id) ?? false,
            Images = ParseImages(post.Images),
            LastReplyAt = last?.CreatedAt,
            LastReplyAuthor = lastAuthor,
            CreatedAt = post.CreatedAt,
        };
    }

    private static void CopyPostFields(PostOut target, PostOut source)
    {
        target.Id = source.Id;
        target.Content = source.Content;
        target.Board = source.Board;
        target.Status = source.Status;
        target.Crisis = source.Crisis;
        target.IsAnonymous = source.IsAnonymous;
        target.Author = source.Author;
        target.AuthorAvatar = source.AuthorAvatar;
        target.AuthorBadge = source.AuthorBadge;
        target.ReplyCount = source.ReplyCount;
        target.ViewCount = source.ViewCount;
        target.AiReplied = source.AiReplied;
        target.Mine = source.Mine;
        target.LikeCount = source.LikeCount;
        target.Liked = source.Liked;
        target.Images = source.Images;
        target.LastReplyAt = source.LastReplyAt;
        target.LastReplyAuthor = source.LastReplyAuthor;
        target.CreatedAt = source.CreatedAt;
    }
}
