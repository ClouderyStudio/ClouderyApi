using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Shared.Exceptions;
using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Api.Contracts;
using ClouderyApi.Modules.Mhop.Infrastructure;
using ClouderyApi.Modules.Mhop.Application.Mapping;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Mhop.Application;

/// <summary>
/// 论坛互助用例编排：板块 / 统计 / 帖子列表与详情 / 发帖回帖 / 点赞 / 作者自管理。
/// 控制器只做 HTTP 绑定与 MhopOk / MhopStatus 包装，映射集中在 MhopForumMapper。
/// </summary>
public sealed class ForumAppService
{
    private readonly MhopDbContext _db;
    private readonly MhopCurrentUserAccessor _current;
    private readonly MhopOnlineTracker _online;
    private readonly MhopUploadService _uploads;
    private readonly MhopContentService _content;
    private readonly MhopContentReviewService _review;

    public ForumAppService(
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

    public async Task<List<BoardOut>> BoardsAsync()
    {
        var counts = await _db.MhopPosts
            .Where(p => p.Status == ContentStatus.Published)
            .GroupBy(p => p.Board)
            .Select(g => new { Board = g.Key, Count = g.Count() })
            .ToListAsync();
        var countMap = counts.ToDictionary(x => x.Board, x => x.Count);

        // 全部使用小写键名，蛇形命名策略不会改写，保证与 Python 返回一致
        return MhopBoards.All.Select(b => new BoardOut
        {
            Slug = b.Slug,
            Name = b.Name,
            Color = b.Color,
            Desc = b.Desc,
            Count = countMap.GetValueOrDefault(b.Slug, 0),
        }).ToList();
    }

    public async Task<ForumStatsOut> PublicStatsAsync()
    {
        return new ForumStatsOut
        {
            Posts = await _db.MhopPosts.CountAsync(p => p.Status == ContentStatus.Published),
            Replies = await _db.MhopReplies.CountAsync(r => r.Status == ContentStatus.Published),
            Users = await _db.MhopUsers.CountAsync(),
            Online = _online.Count(),
        };
    }

    public async Task<PostListOut> ListPostsAsync(
        int page, int size, string keyword, string board, string sort)
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
                throw new DomainRuleException("板块不存在");
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

        var items = posts.Select(p => MhopForumMapper.ToPostOut(
            p,
            users,
            repliesByPost.GetValueOrDefault(p.Id, []),
            current?.Id,
            likeCounts,
            liked)).ToList();

        return new PostListOut { Total = total, Page = page, Size = size, Items = items };
    }

    public async Task<PostDetailOut> GetPostAsync(int postId, bool incView)
    {
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null || post.Status != ContentStatus.Published)
            throw new MhopApiException(404, "帖子不存在或正在审核中");

        if (incView)
        {
            post.AddView();
            await _db.SaveChangesAsync();
        }

        // 已撤回的 AI 回复保留占位（正文在 ToReplyOut 中屏蔽），AI 回复置顶
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
        MhopForumMapper.CopyPostFields(detail, MhopForumMapper.ToPostOut(post, users, replies, current?.Id, postLikes, postLiked));
        detail.Replies = replies.Select(r => MhopForumMapper.ToReplyOut(r, users, replyLikeCounts, replyLiked)).ToList();
        return detail;
    }

    // ---------------- 写入接口 ----------------

    public async Task<PostOut> CreatePostAsync(PostIn body)
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
        return MhopForumMapper.ToPostOut(post, users, [], current.Id, null, null);
    }

    public async Task<ReplyOut> CreateReplyAsync(int postId, ReplyIn body)
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
        return MhopForumMapper.ToReplyOut(reply, users, null, null);
    }

    public async Task<LikeToggleOut> ToggleLikeAsync(LikeIn body)
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
                throw new DomainRuleException("该回复暂不可点赞");
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
        return new LikeToggleOut { Liked = liked, LikeCount = count };
    }

    public async Task<LikeIdsOut> MyLikesAsync(string targetType)
    {
        var target = LikeTargetType.Parse(targetType);

        var current = await _current.GetOptionalAsync();
        if (current is null) return new LikeIdsOut();

        var ids = await _db.MhopLikes
            .Where(l => l.UserId == current.Id && l.TargetType == target.Value)
            .Select(l => l.TargetId)
            .ToListAsync();
        return new LikeIdsOut { Ids = ids };
    }

    // ---------------- 作者自管理（个人主页：查看 / 编辑 / 撤回审核 / 重新提交 / 删除） ----------------

    /// <summary>个人主页统计：按状态汇总我的帖子 / 回复数量。</summary>
    public async Task<MineSummaryOut> MySummaryAsync()
    {
        var current = await _current.RequireAsync();
        var postRows = await _db.MhopPosts.Where(p => p.UserId == current.Id)
            .GroupBy(p => p.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var replyRows = await _db.MhopReplies.Where(r => r.UserId == current.Id)
            .GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var postMap = postRows.ToDictionary(x => x.Status, x => x.Count);
        var replyMap = replyRows.ToDictionary(x => x.Status, x => x.Count);

        return new MineSummaryOut
        {
            Posts = new MineCountOut
            {
                Total = postMap.Values.Sum(),
                Pending = postMap.GetValueOrDefault(ContentStatus.Pending, 0),
                Published = postMap.GetValueOrDefault(ContentStatus.Published, 0),
                Rejected = postMap.GetValueOrDefault(ContentStatus.Rejected, 0),
                Draft = postMap.GetValueOrDefault(ContentStatus.Draft, 0),
            },
            Replies = new MineCountOut
            {
                Total = replyMap.Values.Sum(),
                Pending = replyMap.GetValueOrDefault(ContentStatus.Pending, 0),
                Published = replyMap.GetValueOrDefault(ContentStatus.Published, 0),
                Rejected = replyMap.GetValueOrDefault(ContentStatus.Rejected, 0),
                Draft = replyMap.GetValueOrDefault(ContentStatus.Draft, 0),
            },
        };
    }

    /// <summary>我的帖子（含审核中 / 已驳回 / 草稿），status 为空返回全部。</summary>
    public async Task<MyPostListOut> MyPostsAsync(int? status, int page, int size)
    {
        var current = await _current.RequireAsync();
        if (page < 1) page = 1;
        size = Math.Clamp(size, 1, 50);

        var query = _db.MhopPosts.Where(p => p.UserId == current.Id);
        if (status.HasValue)
        {
            if (status.Value is < ContentStatus.Pending or > ContentStatus.Draft) throw new DomainRuleException("非法状态");
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
            return new MyPostOut
            {
                Id = p.Id,
                Content = p.Content,
                Board = p.Board,
                Status = p.Status,
                Crisis = p.Crisis,
                IsAnonymous = p.IsAnonymous,
                Images = MhopForumMapper.ParseImages(p.Images),
                ReplyCount = postReplies.Count(r => r.Status == ContentStatus.Published && !r.Recalled),
                ViewCount = p.ViewCount,
                LikeCount = likeCounts.GetValueOrDefault(p.Id, 0),
                ReviewNote = p.ReviewNote ?? string.Empty,
                AiFlag = p.AiFlag ?? string.Empty,
                AiReviewNote = p.AiReviewNote ?? string.Empty,
                Editable = p.IsEditable,
                CanWithdraw = p.CanWithdraw,
                CanSubmit = p.CanSubmit,
                CreatedAt = p.CreatedAt,
            };
        }).ToList();

        return new MyPostListOut { Total = total, Page = page, Size = size, Items = items };
    }

    /// <summary>我的回复（含审核中 / 已驳回 / 草稿），status 为空返回全部。</summary>
    public async Task<MyReplyListOut> MyRepliesAsync(int? status, int page, int size)
    {
        var current = await _current.RequireAsync();
        if (page < 1) page = 1;
        size = Math.Clamp(size, 1, 50);

        var query = _db.MhopReplies.Where(r => r.UserId == current.Id);
        if (status.HasValue)
        {
            if (status.Value is < ContentStatus.Pending or > ContentStatus.Draft) throw new DomainRuleException("非法状态");
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
            return new MyReplyOut
            {
                Id = r.Id,
                PostId = r.PostId,
                PostExcerpt = postContent.Length > 80 ? postContent[..80] + "…" : postContent,
                // 帖子本身未公开时，回复审核通过也不会公开展示
                PostStatus = posts.TryGetValue(r.PostId, out var p2) ? p2.Status : (int?)null,
                Content = r.Content,
                Status = r.Status,
                Crisis = r.Crisis,
                IsAnonymous = r.IsAnonymous,
                Images = MhopForumMapper.ParseImages(r.Images),
                LikeCount = likeCounts.GetValueOrDefault(r.Id, 0),
                ReviewNote = r.ReviewNote ?? string.Empty,
                AiFlag = r.AiFlag ?? string.Empty,
                AiReviewNote = r.AiReviewNote ?? string.Empty,
                Editable = r.IsEditable,
                CanWithdraw = r.CanWithdraw,
                CanSubmit = r.CanSubmit,
                CreatedAt = r.CreatedAt,
            };
        }).ToList();

        return new MyReplyListOut { Total = total, Page = page, Size = size, Items = items };
    }

    /// <summary>编辑自己的帖子：仅待审核 / 草稿可改，已通过或已驳回只能删除。</summary>
    public async Task<PostUpdateOut> UpdatePostAsync(int postId, PostIn body, CancellationToken cancellationToken = default)
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
        await _uploads.DeleteAsync(removedImages, cancellationToken);

        return new PostUpdateOut { Status = post.Status, Crisis = post.Crisis };
    }

    /// <summary>取消审核：待审核 → 草稿，转为仅自己可见并可继续编辑。</summary>
    public async Task<ContentStatusOut> WithdrawPostAsync(int postId)
    {
        var current = await _current.RequireAsync();
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null || post.UserId != current.Id) throw new MhopApiException(404, "帖子不存在");
        post.Withdraw();
        await _db.SaveChangesAsync();
        return new ContentStatusOut { Status = post.Status };
    }

    /// <summary>重新提交审核：草稿 → 待审核。</summary>
    public async Task<ContentStatusOut> SubmitPostAsync(int postId)
    {
        var current = await _current.RequireAsync();
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null || post.UserId != current.Id) throw new MhopApiException(404, "帖子不存在");
        post.SubmitForReview();
        await _db.SaveChangesAsync();
        _review.QueuePostReview(post.Id);
        return new ContentStatusOut { Status = post.Status };
    }

    /// <summary>删除自己的帖子：任意状态均可，连同其回复、点赞、AI 日志与图片一并清理。</summary>
    public async Task<OkOut> DeletePostAsync(int postId, CancellationToken cancellationToken = default)
    {
        var current = await _current.RequireAsync();
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null || post.UserId != current.Id) throw new MhopApiException(404, "帖子不存在");

        await _content.DeletePostAsync(post, cancellationToken);
        return new OkOut();
    }

    /// <summary>编辑自己的回复：仅待审核 / 草稿可改。</summary>
    public async Task<ContentStatusOut> UpdateReplyAsync(int replyId, ReplyIn body, CancellationToken cancellationToken = default)
    {
        var current = await _current.RequireAsync();
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null || reply.UserId != current.Id) throw new MhopApiException(404, "回复不存在");

        var images = MhopImageRefs.Normalize(body.Images);
        // 编辑时被移除的图片此后不再被引用，落库成功后清理存储对象
        var removedImages = reply.ApplyAuthorEdit(body.Content, body.IsAnonymous, images, Screen(body.Content));

        await _db.SaveChangesAsync();
        if (reply.Status == ContentStatus.Pending) _review.QueueReplyReview(reply.Id);
        await _uploads.DeleteAsync(removedImages, cancellationToken);
        return new ContentStatusOut { Status = reply.Status };
    }

    /// <summary>取消审核：待审核回复 → 草稿。</summary>
    public async Task<ContentStatusOut> WithdrawReplyAsync(int replyId)
    {
        var current = await _current.RequireAsync();
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null || reply.UserId != current.Id) throw new MhopApiException(404, "回复不存在");
        reply.Withdraw();
        await _db.SaveChangesAsync();
        return new ContentStatusOut { Status = reply.Status };
    }

    /// <summary>重新提交审核：草稿回复 → 待审核。</summary>
    public async Task<ContentStatusOut> SubmitReplyAsync(int replyId)
    {
        var current = await _current.RequireAsync();
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null || reply.UserId != current.Id) throw new MhopApiException(404, "回复不存在");
        reply.SubmitForReview();
        await _db.SaveChangesAsync();
        _review.QueueReplyReview(reply.Id);
        return new ContentStatusOut { Status = reply.Status };
    }

    /// <summary>删除自己的回复：任意状态均可，连同其点赞、AI 日志与图片一并清理。</summary>
    public async Task<OkOut> DeleteReplyAsync(int replyId, CancellationToken cancellationToken = default)
    {
        var current = await _current.RequireAsync();
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null || reply.UserId != current.Id) throw new MhopApiException(404, "回复不存在");

        await _content.DeleteReplyAsync(reply, cancellationToken);
        return new OkOut();
    }

    // ---------------- 数据装载辅助（EF 查询） ----------------

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

    /// <summary>内容安全初筛：危机信号 + 敏感词（领域方法据此改变状态）。</summary>
    private static ContentScreening Screen(string? rawContent)
    {
        var content = (rawContent ?? string.Empty).Trim();
        return new ContentScreening(MhopModeration.DetectCrisis(content), MhopModeration.HitSensitive(content));
    }
}
