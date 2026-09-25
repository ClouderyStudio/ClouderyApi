using System.Text.Json;
using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>
/// 论坛互助：板块、发帖、人类回复先审后发、点赞。
/// AI 自动回复不在发帖/编辑时生成，统一由管理员审核通过后触发（见 MhopAdminController.ModeratePost）。
/// 对应 Python 后端 routers/forum.py。
/// </summary>
[ApiController]
[Route("mhop/forum")]
public class MhopForumController : MhopControllerBase
{
    private readonly MhopDbContext _db;
    private readonly MhopCurrentUserAccessor _current;
    private readonly MhopOnlineTracker _online;

    public MhopForumController(
        MhopDbContext db,
        MhopCurrentUserAccessor current,
        MhopOnlineTracker online)
    {
        _db = db;
        _current = current;
        _online = online;
    }

    // ---------------- 读取接口 ----------------

    [HttpGet("boards")]
    public async Task<IActionResult> Boards()
    {
        var counts = await _db.MhopPosts
            .Where(p => p.Status == 1)
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
        posts = await _db.MhopPosts.CountAsync(p => p.Status == 1),
        replies = await _db.MhopReplies.CountAsync(r => r.Status == 1),
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

        var query = _db.MhopPosts.Where(p => p.Status == 1);
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
                    .Where(r => r.PostId == p.Id && r.Status == 1 && !r.Recalled)
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
        var likeCounts = await LikeCountMapAsync("post", postIds);
        var liked = await LikedSetAsync(current?.Id, "post", postIds);

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
        if (post is null || post.Status != 1)
            throw new MhopApiException(404, "帖子不存在或正在审核中");

        if (IsTruthy(incView))
        {
            post.ViewCount += 1;
            await _db.SaveChangesAsync();
        }

        // 已撤回的 AI 回复保留占位（正文在 _reply_out 中屏蔽），AI 回复置顶
        var replies = await _db.MhopReplies
            .Where(r => r.PostId == postId && r.Status == 1)
            .OrderBy(r => r.IsAi ? 0 : 1)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync();

        var current = await _current.GetOptionalAsync();
        var users = await LoadAuthorMapAsync([post], replies);
        var replyIds = replies.Select(r => r.Id).ToList();
        var replyLikeCounts = await LikeCountMapAsync("reply", replyIds);
        var replyLiked = await LikedSetAsync(current?.Id, "reply", replyIds);
        var postLikes = await LikeCountMapAsync("post", [post.Id]);
        var postLiked = await LikedSetAsync(current?.Id, "post", [post.Id]);

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
        var content = (body.Content ?? string.Empty).Trim();
        if (content.Length == 0) throw new MhopApiException(400, "内容不能为空");
        if (content.Length > 2000) throw new MhopApiException(400, "内容不能超过 2000 字");
        if (string.IsNullOrWhiteSpace(body.Board) || !MhopBoards.Slugs.Contains(body.Board))
            throw new MhopApiException(400, "请选择板块");

        var crisis = MhopModeration.DetectCrisis(content);
        var images = NormalizeImages(body.Images);
        var post = new MhopPost
        {
            // 匿名仅对前台脱敏；user_id 始终留存，供后台审核与追责
            UserId = current.Id,
            IsAnonymous = body.IsAnonymous,
            Content = content,
            Board = body.Board,
            Images = images.Count > 0 ? JsonSerializer.Serialize(images) : string.Empty,
            Status = 0, // 待审核，管理员通过后才公开展示
            Crisis = crisis,
            CreatedAt = DateTime.UtcNow,
        };
        _db.MhopPosts.Add(post);
        await _db.SaveChangesAsync();

        // 这里不生成 AI 自动回复：待审核期间正文可能被反复修改，
        // 统一等管理员审核通过后再生成，避免堆积多条与最终正文脱节的回复
        var users = new Dictionary<int, MhopUser> { [current.Id] = current };
        return MhopStatus(201, ToPostOut(post, users, [], current.Id, null, null));
    }

    [HttpPost("posts/{postId:int}/replies")]
    public async Task<IActionResult> CreateReply(int postId, [FromBody] ReplyIn body)
    {
        var current = await _current.RequirePhoneVerifiedAsync();
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        // 草稿仅作者可见，禁止他人（含作者本人）对其回复
        if (post is null || post.Status is StatusRejected or StatusDraft)
            throw new MhopApiException(404, "帖子不存在或已被移除");

        var content = (body.Content ?? string.Empty).Trim();
        if (content.Length == 0) throw new MhopApiException(400, "回复内容不能为空");
        if (content.Length > 1000) throw new MhopApiException(400, "回复内容不能超过 1000 字");

        var crisis = MhopModeration.DetectCrisis(content);
        var sensitiveWords = MhopModeration.HitSensitive(content);
        var images = NormalizeImages(body.Images);
        var reply = new MhopReply
        {
            PostId = postId,
            UserId = current.Id,
            IsAnonymous = body.IsAnonymous,
            Content = content,
            Images = images.Count > 0 ? JsonSerializer.Serialize(images) : string.Empty,
            Crisis = crisis,
            Status = sensitiveWords.Count > 0 ? 2 : 0, // 命中违规词直接拦截，否则待审核
            ReviewNote = sensitiveWords.Count > 0
                ? Truncate($"系统拦截：命中敏感词 {string.Join(",", sensitiveWords)}", 255)
                : string.Empty,
            CreatedAt = DateTime.UtcNow,
        };
        _db.MhopReplies.Add(reply);
        await _db.SaveChangesAsync();

        var users = new Dictionary<int, MhopUser> { [current.Id] = current };
        return MhopStatus(201, ToReplyOut(reply, users, null, null));
    }

    [HttpPost("likes/toggle")]
    public async Task<IActionResult> ToggleLike([FromBody] LikeIn body)
    {
        var current = await _current.RequireAsync();
        if (body.TargetType is not ("post" or "reply"))
            throw new MhopApiException(400, "非法点赞对象");

        if (body.TargetType == "post")
        {
            var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == body.TargetId);
            if (post is null || post.Status is StatusRejected or StatusDraft)
                throw new MhopApiException(404, "内容不存在或已被移除");
        }
        else
        {
            var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == body.TargetId);
            if (reply is null || reply.Status == 2)
                throw new MhopApiException(404, "内容不存在或已被移除");
            if (reply.Status != 1 || reply.Recalled)
                throw new MhopApiException(400, "该回复暂不可点赞");
        }

        var existing = await _db.MhopLikes.FirstOrDefaultAsync(l =>
            l.UserId == current.Id && l.TargetType == body.TargetType && l.TargetId == body.TargetId);

        var liked = existing is null;
        if (existing is not null) _db.MhopLikes.Remove(existing);
        else _db.MhopLikes.Add(new MhopLike
        {
            UserId = current.Id,
            TargetType = body.TargetType,
            TargetId = body.TargetId,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var count = await _db.MhopLikes.CountAsync(l =>
            l.TargetType == body.TargetType && l.TargetId == body.TargetId);
        return MhopOk(new { liked, like_count = count });
    }

    [HttpGet("likes/mine")]
    public async Task<IActionResult> MyLikes([FromQuery(Name = "target_type")] string targetType)
    {
        if (targetType is not ("post" or "reply"))
            throw new MhopApiException(400, "非法点赞对象");

        var current = await _current.GetOptionalAsync();
        if (current is null) return MhopOk(new { ids = Array.Empty<int>() });

        var ids = await _db.MhopLikes
            .Where(l => l.UserId == current.Id && l.TargetType == targetType)
            .Select(l => l.TargetId)
            .ToListAsync();
        return MhopOk(new { ids });
    }

    // ---------------- 作者自管理（个人主页：查看 / 编辑 / 撤回审核 / 重新提交 / 删除） ----------------

    /// <summary>内容状态：0=待审核 1=已通过 2=已驳回 3=草稿（作者取消审核后自留，仅本人可见）。</summary>
    private const int StatusPending = 0;
    private const int StatusPublished = 1;
    private const int StatusRejected = 2;
    private const int StatusDraft = 3;

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
                pending = postMap.GetValueOrDefault(StatusPending, 0),
                published = postMap.GetValueOrDefault(StatusPublished, 0),
                rejected = postMap.GetValueOrDefault(StatusRejected, 0),
                draft = postMap.GetValueOrDefault(StatusDraft, 0),
            },
            replies = new
            {
                total = replyMap.Values.Sum(),
                pending = replyMap.GetValueOrDefault(StatusPending, 0),
                published = replyMap.GetValueOrDefault(StatusPublished, 0),
                rejected = replyMap.GetValueOrDefault(StatusRejected, 0),
                draft = replyMap.GetValueOrDefault(StatusDraft, 0),
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
            if (status.Value is < StatusPending or > StatusDraft) throw new MhopApiException(400, "非法状态");
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
        var likeCounts = await LikeCountMapAsync("post", postIds);

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
                reply_count = postReplies.Count(r => r.Status == StatusPublished && !r.Recalled),
                view_count = p.ViewCount,
                like_count = likeCounts.GetValueOrDefault(p.Id, 0),
                review_note = p.ReviewNote ?? string.Empty,
                editable = p.Status is StatusPending or StatusDraft,
                can_withdraw = p.Status == StatusPending,
                can_submit = p.Status == StatusDraft,
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
            if (status.Value is < StatusPending or > StatusDraft) throw new MhopApiException(400, "非法状态");
            query = query.Where(r => r.Status == status.Value);
        }

        var total = await query.CountAsync();
        var replies = await query.OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * size).Take(size).ToListAsync();

        var postIds = replies.Select(r => r.PostId).Distinct().ToList();
        var posts = postIds.Count == 0
            ? new Dictionary<int, MhopPost>()
            : await _db.MhopPosts.AsNoTracking().Where(p => postIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var likeCounts = await LikeCountMapAsync("reply", replies.Select(r => r.Id).ToList());

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
                editable = r.Status is StatusPending or StatusDraft,
                can_withdraw = r.Status == StatusPending,
                can_submit = r.Status == StatusDraft,
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
        if (post.Status is not (StatusPending or StatusDraft))
            throw new MhopApiException(400, "已通过审核的内容不可修改，仅可删除");

        var content = (body.Content ?? string.Empty).Trim();
        if (content.Length == 0) throw new MhopApiException(400, "内容不能为空");
        if (content.Length > 2000) throw new MhopApiException(400, "内容不能超过 2000 字");
        if (string.IsNullOrWhiteSpace(body.Board) || !MhopBoards.Slugs.Contains(body.Board))
            throw new MhopApiException(400, "请选择板块");

        post.Content = content;
        post.Board = body.Board;
        post.IsAnonymous = body.IsAnonymous;
        var images = NormalizeImages(body.Images);
        post.Images = images.Count > 0 ? JsonSerializer.Serialize(images) : string.Empty;
        post.Crisis = MhopModeration.DetectCrisis(content);
        post.ReviewNote = string.Empty;

        await _db.SaveChangesAsync();
        // AI 自动回复只随审核通过产生：待审核 / 草稿阶段无论编辑多少次都不会生成回复

        return MhopOk(new { ok = true, status = post.Status, crisis = post.Crisis });
    }

    /// <summary>取消审核：待审核 → 草稿，转为仅自己可见并可继续编辑。</summary>
    [HttpPost("posts/{postId:int}/withdraw")]
    public async Task<IActionResult> WithdrawPost(int postId)
    {
        var current = await _current.RequireAsync();
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null || post.UserId != current.Id) throw new MhopApiException(404, "帖子不存在");
        if (post.Status != StatusPending) throw new MhopApiException(400, "只有审核中的内容可以取消审核");

        post.Status = StatusDraft;
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
        if (post.Status != StatusDraft) throw new MhopApiException(400, "只有草稿可以重新提交审核");
        if (string.IsNullOrWhiteSpace(post.Content)) throw new MhopApiException(400, "内容不能为空");

        post.Status = StatusPending;
        post.ReviewNote = string.Empty;
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true, status = post.Status });
    }

    /// <summary>删除自己的帖子：任意状态均可，连同其回复与点赞一并清理。</summary>
    [HttpDelete("posts/{postId:int}")]
    public async Task<IActionResult> DeletePost(int postId)
    {
        var current = await _current.RequireAsync();
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null || post.UserId != current.Id) throw new MhopApiException(404, "帖子不存在");

        var replies = await _db.MhopReplies.Where(r => r.PostId == post.Id).ToListAsync();
        var replyIds = replies.Select(r => r.Id).ToList();
        var likes = await _db.MhopLikes.Where(l =>
            (l.TargetType == "post" && l.TargetId == post.Id)
            || (l.TargetType == "reply" && replyIds.Contains(l.TargetId))).ToListAsync();

        if (likes.Count > 0) _db.MhopLikes.RemoveRange(likes);
        if (replies.Count > 0) _db.MhopReplies.RemoveRange(replies);
        _db.MhopPosts.Remove(post);
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    /// <summary>编辑自己的回复：仅待审核 / 草稿可改。</summary>
    [HttpPut("replies/{replyId:int}")]
    public async Task<IActionResult> UpdateReply(int replyId, [FromBody] ReplyIn body)
    {
        var current = await _current.RequireAsync();
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null || reply.UserId != current.Id) throw new MhopApiException(404, "回复不存在");
        if (reply.Status is not (StatusPending or StatusDraft))
            throw new MhopApiException(400, "已通过审核的内容不可修改，仅可删除");

        var content = (body.Content ?? string.Empty).Trim();
        if (content.Length == 0) throw new MhopApiException(400, "回复内容不能为空");
        if (content.Length > 1000) throw new MhopApiException(400, "回复内容不能超过 1000 字");

        reply.Content = content;
        reply.IsAnonymous = body.IsAnonymous;
        var images = NormalizeImages(body.Images);
        reply.Images = images.Count > 0 ? JsonSerializer.Serialize(images) : string.Empty;
        reply.Crisis = MhopModeration.DetectCrisis(content);

        if (reply.Status == StatusPending)
        {
            var sensitiveWords = MhopModeration.HitSensitive(content);
            reply.Status = sensitiveWords.Count > 0 ? StatusRejected : StatusPending;
            reply.ReviewNote = sensitiveWords.Count > 0
                ? Truncate($"系统拦截：命中敏感词 {string.Join(",", sensitiveWords)}", 255)
                : string.Empty;
        }

        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true, status = reply.Status });
    }

    /// <summary>取消审核：待审核回复 → 草稿。</summary>
    [HttpPost("replies/{replyId:int}/withdraw")]
    public async Task<IActionResult> WithdrawReply(int replyId)
    {
        var current = await _current.RequireAsync();
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null || reply.UserId != current.Id) throw new MhopApiException(404, "回复不存在");
        if (reply.Status != StatusPending) throw new MhopApiException(400, "只有审核中的内容可以取消审核");

        reply.Status = StatusDraft;
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
        if (reply.Status != StatusDraft) throw new MhopApiException(400, "只有草稿可以重新提交审核");
        if (string.IsNullOrWhiteSpace(reply.Content)) throw new MhopApiException(400, "回复内容不能为空");

        reply.Status = StatusPending;
        reply.ReviewNote = string.Empty;
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true, status = reply.Status });
    }

    /// <summary>删除自己的回复：任意状态均可，连同其点赞一并清理。</summary>
    [HttpDelete("replies/{replyId:int}")]
    public async Task<IActionResult> DeleteReply(int replyId)
    {
        var current = await _current.RequireAsync();
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null || reply.UserId != current.Id) throw new MhopApiException(404, "回复不存在");

        _db.MhopLikes.RemoveRange(await _db.MhopLikes
            .Where(l => l.TargetType == "reply" && l.TargetId == reply.Id).ToListAsync());
        _db.MhopReplies.Remove(reply);
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    // ---------------- 映射辅助 ----------------

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    private static List<string> NormalizeImages(IEnumerable<string>? images)
        => images is null
            ? []
            : images.Where(u => !string.IsNullOrWhiteSpace(u)).Take(9).ToList();

    private static List<string> ParseImages(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(raw);
            return parsed is null ? [] : parsed.Where(u => !string.IsNullOrEmpty(u)).Take(9).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

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

    private async Task<Dictionary<int, int>> LikeCountMapAsync(string targetType, List<int> ids)
    {
        if (ids.Count == 0) return new Dictionary<int, int>();
        var rows = await _db.MhopLikes
            .Where(l => l.TargetType == targetType && ids.Contains(l.TargetId))
            .GroupBy(l => l.TargetId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(x => x.Id, x => x.Count);
    }

    private async Task<HashSet<int>> LikedSetAsync(int? userId, string targetType, List<int> ids)
    {
        if (!userId.HasValue || ids.Count == 0) return new HashSet<int>();
        var list = await _db.MhopLikes
            .Where(l => l.UserId == userId.Value && l.TargetType == targetType && ids.Contains(l.TargetId))
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
        var visible = replies.Where(r => r.Status == 1 && !r.Recalled).ToList();
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
