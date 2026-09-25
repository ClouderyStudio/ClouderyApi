using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>
/// 管理后台：数据看板、帖子巡检、回复审核、AI 回复撤回/恢复、用户管理、AI 日志。
/// 对应 Python 后端 routers/admin.py，全路由要求管理员权限。
/// </summary>
[ApiController]
[Route("mhop/admin")]
[MhopAdmin]
public class MhopAdminController : MhopControllerBase
{
    private readonly MhopDbContext _db;
    private readonly MhopCurrentUserAccessor _current;
    private readonly MhopOnlineTracker _online;
    private readonly MhopPasswordHasher _hasher;
    private readonly MhopAiService _ai;
    private readonly MhopContentService _content;

    public MhopAdminController(
        MhopDbContext db,
        MhopCurrentUserAccessor current,
        MhopOnlineTracker online,
        MhopPasswordHasher hasher,
        MhopAiService ai,
        MhopContentService content)
    {
        _db = db;
        _current = current;
        _online = online;
        _hasher = hasher;
        _ai = ai;
        _content = content;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var since = DateTime.UtcNow.AddHours(-24);
        return MhopOk(new
        {
            users = await _db.MhopUsers.CountAsync(),
            // 草稿（status=3）是作者私有内容，不计入后台统计
            posts = await _db.MhopPosts.CountAsync(p => p.Status != 3),
            replies = await _db.MhopReplies.CountAsync(r => r.Status != 3),
            assessments = await _db.MhopAssessments.CountAsync(),
            ai_logs = await _db.MhopAiLogs.CountAsync(),
            pending_posts = await _db.MhopPosts.CountAsync(p => p.Status == 0),
            crisis_posts = await _db.MhopPosts.CountAsync(p => p.Crisis && p.Status != 2 && p.Status != 3),
            pending_replies = await _db.MhopReplies.CountAsync(r => r.Status == 0 && !r.IsAi),
            rejected_replies = await _db.MhopReplies.CountAsync(r => r.Status == 2),
            new_posts_24h = await _db.MhopPosts.CountAsync(p => p.CreatedAt >= since),
            new_users_24h = await _db.MhopUsers.CountAsync(u => u.CreatedAt >= since),
            online = _online.Count(),
        });
    }

    [HttpGet("posts")]
    public async Task<IActionResult> ListPosts([FromQuery] int? status = null)
    {
        // 草稿不对后台展示（作者主动取消审核后的私有内容）
        var query = _db.MhopPosts.Where(p => p.Status != 3);
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        var posts = await query.OrderByDescending(p => p.CreatedAt).Take(200).ToListAsync();

        var postIds = posts.Select(p => p.Id).ToList();
        var replyCounts = postIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.MhopReplies
                .Where(r => postIds.Contains(r.PostId))
                .GroupBy(r => r.PostId)
                .Select(g => new { PostId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PostId, x => x.Count);
        var users = await LoadUsersAsync(posts.Select(p => p.UserId));

        // 后台视角：匿名帖也返回真实作者与手机号（历史匿名帖 user_id 已丢失则为 null）
        return MhopOk(posts.Select(p =>
        {
            var (author, phone) = RealAuthor(p.UserId, users);
            return new
            {
                id = p.Id,
                content = p.Content,
                board = p.Board,
                status = p.Status,
                crisis = p.Crisis,
                is_anonymous = p.IsAnonymous,
                author,
                author_phone = phone,
                review_note = p.ReviewNote ?? string.Empty,
                reply_count = replyCounts.GetValueOrDefault(p.Id, 0),
                created_at = p.CreatedAt,
            };
        }).ToList());
    }

    [HttpPost("posts/{postId:int}/moderate")]
    public async Task<IActionResult> ModeratePost(int postId, [FromBody] ModerateIn body)
    {
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) throw new MhopApiException(404, "帖子不存在");

        var approved = body.Action switch
        {
            "approve" => true,
            "reject" => false,
            _ => throw new MhopApiException(400, "非法操作"),
        };
        post.Status = approved ? 1 : 2;
        post.ReviewNote = Truncate(body.Note ?? string.Empty, 255);
        await _db.SaveChangesAsync();

        // AI 自动回复只在「首次通过审核」时生成；隐藏后重新展示沿用已有回复，不重复调用大模型。
        // 需要强制刷新时用 POST posts/{id}/ai-reply/regenerate。
        if (approved) await _ai.EnsureForumReplyAsync(post.Id, post.Content, post.Crisis);

        return MhopOk(new { ok = true });
    }

    /// <summary>强制重新生成某帖的 AI 自动回复：先清理旧回复，再调用大模型生成一条新的。</summary>
    [HttpPost("posts/{postId:int}/ai-reply/regenerate")]
    public async Task<IActionResult> RegenerateAiReply(int postId)
    {
        var post = await _db.MhopPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) throw new MhopApiException(404, "帖子不存在");
        if (post.Status != 1) throw new MhopApiException(400, "仅公开中的帖子可以生成 AI 自动回复");

        await _ai.RegenerateForumReplyAsync(post.Id, post.Content, post.Crisis);
        return MhopOk(new { ok = true });
    }

    /// <summary>管理员删除帖子：不限作者与状态，连同其全部回复、点赞、AI 日志与图片一并清理。</summary>
    [HttpDelete("posts/{postId:int}")]
    public async Task<IActionResult> DeletePost(int postId)
    {
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) throw new MhopApiException(404, "帖子不存在");

        var deletedReplies = await _content.DeletePostAsync(post, HttpContext.RequestAborted);
        return MhopOk(new { ok = true, deleted_replies = deletedReplies });
    }

    [HttpGet("replies")]
    public async Task<IActionResult> ListReplies([FromQuery] int? status = null)
    {
        var query = _db.MhopReplies.Where(r => r.Status != 3);
        if (status.HasValue) query = query.Where(r => r.Status == status.Value);
        var replies = await query.OrderByDescending(r => r.CreatedAt).Take(300).ToListAsync();

        var postIds = replies.Select(r => r.PostId).Distinct().ToList();
        var posts = postIds.Count == 0
            ? new Dictionary<int, MhopPost>()
            : await _db.MhopPosts.AsNoTracking()
                .Where(p => postIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);
        var users = await LoadUsersAsync(replies.Select(r => r.UserId));

        return MhopOk(replies.Select(r =>
        {
            var (author, phone) = RealAuthor(r.UserId, users);
            var postContent = posts.TryGetValue(r.PostId, out var post) ? post.Content : string.Empty;
            return new
            {
                id = r.Id,
                post_id = r.PostId,
                post_excerpt = postContent.Length > 80 ? postContent[..80] + "…" : postContent,
                content = r.Content,
                status = r.Status,
                is_ai = r.IsAi,
                crisis = r.Crisis,
                is_anonymous = r.IsAnonymous,
                author,
                author_phone = phone,
                review_note = r.ReviewNote ?? string.Empty,
                recalled = r.Recalled,
                recall_reason = r.RecallReason ?? string.Empty,
                created_at = r.CreatedAt,
            };
        }).ToList());
    }

    [HttpPost("replies/{replyId:int}/moderate")]
    public async Task<IActionResult> ModerateReply(int replyId, [FromBody] ModerateIn body)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");

        reply.Status = body.Action switch
        {
            "approve" => 1,
            "reject" => 2,
            _ => throw new MhopApiException(400, "非法操作"),
        };
        reply.ReviewNote = Truncate(body.Note ?? string.Empty, 255);
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    /// <summary>撤回 AI 回复：对所有用户即时隐藏正文，保留内容与原因以备审计，可恢复。</summary>
    [HttpPost("replies/{replyId:int}/recall")]
    public async Task<IActionResult> RecallReply(int replyId, [FromBody] RecallIn body)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");
        if (!reply.IsAi) throw new MhopApiException(400, "仅支持撤回 AI 回复；人类回复请使用驳回");

        var reason = (body.Reason ?? string.Empty).Trim();
        if (reason.Length == 0) throw new MhopApiException(400, "请填写撤回原因");

        reply.Recalled = true;
        reply.RecallReason = Truncate(reason, 255);
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    /// <summary>恢复被撤回的 AI 回复，重新公开展示。</summary>
    [HttpPost("replies/{replyId:int}/restore")]
    public async Task<IActionResult> RestoreReply(int replyId)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");
        if (!reply.IsAi) throw new MhopApiException(400, "仅支持恢复 AI 回复");

        reply.Recalled = false;
        reply.RecallReason = string.Empty;
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    /// <summary>管理员删除回复：不限作者与状态，连同其点赞、AI 日志与图片一并清理。</summary>
    [HttpDelete("replies/{replyId:int}")]
    public async Task<IActionResult> DeleteReply(int replyId)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");

        await _content.DeleteReplyAsync(reply, HttpContext.RequestAborted);
        return MhopOk(new { ok = true });
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        var users = await _db.MhopUsers.AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        // 附带内容数量：后台可在删除用户前明确提示将连带删除多少内容
        var postCounts = (await _db.MhopPosts.Where(p => p.UserId != null)
                .Select(p => p.UserId!.Value).ToListAsync())
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        var replyCounts = (await _db.MhopReplies.Where(r => r.UserId != null)
                .Select(r => r.UserId!.Value).ToListAsync())
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

        return MhopOk(users.Select(u =>
        {
            var item = UserOut.FromEntity(u);
            return new
            {
                item.Id,
                item.Username,
                item.Email,
                item.Phone,
                item.Role,
                item.Status,
                item.Avatar,
                item.Badge,
                item.CreatedAt,
                post_count = postCounts.GetValueOrDefault(u.Id),
                reply_count = replyCounts.GetValueOrDefault(u.Id),
            };
        }).ToList());
    }

    [HttpPost("users/{userId:int}/status")]
    public async Task<IActionResult> SetUserStatus(int userId, [FromBody] StatusIn body)
    {
        if (body.Status is not ("active" or "disabled"))
            throw new MhopApiException(400, "非法状态");

        var admin = await _current.RequireAdminAsync();
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        if (user.Id == admin.Id && body.Status == "disabled")
            throw new MhopApiException(400, "不能停用当前登录的管理员");

        user.Status = body.Status;
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    /// <summary>设置用户标识。badge 为空字符串表示清除标识。</summary>
    [HttpPost("users/{userId:int}/badge")]
    public async Task<IActionResult> SetUserBadge(int userId, [FromBody] BadgeIn body)
    {
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");

        var badge = Truncate((body.Badge ?? string.Empty).Trim(), 64);
        user.Badge = badge;
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true, badge });
    }

    /// <summary>设置/取消管理员角色。action: promote / demote</summary>
    [HttpPost("users/{userId:int}/role")]
    public async Task<IActionResult> SetUserRole(int userId, [FromBody] RoleIn body)
    {
        var admin = await _current.RequireAdminAsync();
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");

        switch (body.Action)
        {
            case "promote":
                if (user.Role == "admin") throw new MhopApiException(400, "该用户已是管理员");
                user.Role = "admin";
                break;
            case "demote":
                if (user.Role != "admin") throw new MhopApiException(400, "该用户不是管理员");
                if (user.Id == admin.Id) throw new MhopApiException(400, "不能取消自己的管理员权限");
                var adminCount = await _db.MhopUsers.CountAsync(u => u.Role == "admin");
                if (adminCount <= 1) throw new MhopApiException(400, "系统至少需要保留一个管理员");
                user.Role = "user";
                break;
            default:
                throw new MhopApiException(400, "非法操作");
        }

        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true, role = user.Role });
    }

    /// <summary>管理员重置用户密码。</summary>
    [HttpPost("users/{userId:int}/reset-password")]
    public async Task<IActionResult> ResetUserPassword(int userId, [FromBody] ResetPasswordIn body)
    {
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");

        var newPassword = (body.Password ?? string.Empty).Trim();
        if (newPassword.Length < 6) throw new MhopApiException(400, "密码至少 6 位");

        user.PasswordHash = _hasher.Hash(newPassword);
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    /// <summary>管理员删除用户：连同其名下帖子、回复、点赞、AI 日志与图片一并清理，不可恢复。</summary>
    [HttpDelete("users/{userId:int}")]
    public async Task<IActionResult> DeleteUser(int userId)
    {
        var admin = await _current.RequireAdminAsync();
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        if (user.Id == admin.Id) throw new MhopApiException(400, "不能删除当前登录的账号");
        if (user.Role == "admin" && await _db.MhopUsers.CountAsync(u => u.Role == "admin") <= 1)
            throw new MhopApiException(400, "系统至少需要保留一个管理员");

        var (posts, replies) = await _content.DeleteUserAsync(user, HttpContext.RequestAborted);
        return MhopOk(new { ok = true, deleted_posts = posts, deleted_replies = replies });
    }

    [HttpGet("ai-logs")]
    public async Task<IActionResult> ListAiLogs()
    {
        var logs = await _db.MhopAiLogs.OrderByDescending(l => l.CreatedAt).Take(200).ToListAsync();

        // 关联 replies：forum 日志需带出回复的撤回状态，便于在本页直接撤回/恢复
        var replyIds = logs.Where(l => l.ReplyId.HasValue).Select(l => l.ReplyId!.Value).Distinct().ToList();
        var replies = replyIds.Count == 0
            ? new Dictionary<int, MhopReply>()
            : await _db.MhopReplies.AsNoTracking()
                .Where(r => replyIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id);

        return MhopOk(logs.Select(log =>
        {
            var reply = log.ReplyId.HasValue && replies.TryGetValue(log.ReplyId.Value, out var found) ? found : null;
            return new
            {
                id = log.Id,
                module = log.Module,
                engine = log.Engine,
                prompt = Truncate(log.Prompt ?? string.Empty, 300),
                response = Truncate(log.Response ?? string.Empty, 600),
                created_at = log.CreatedAt,
                reply_id = reply?.Id,
                post_id = reply?.PostId,
                reply_status = reply?.Status,
                recalled = reply?.Recalled ?? false,
                recall_reason = reply?.RecallReason ?? string.Empty,
            };
        }).ToList());
    }

    private async Task<Dictionary<int, MhopUser>> LoadUsersAsync(IEnumerable<int?> userIds)
    {
        var ids = userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, MhopUser>();
        return await _db.MhopUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);
    }

    private static (string? Author, string? Phone) RealAuthor(int? userId, IReadOnlyDictionary<int, MhopUser> users)
    {
        if (!userId.HasValue) return (null, null);
        if (!users.TryGetValue(userId.Value, out var user)) return ("未知用户", null);
        return (user.Username, string.IsNullOrEmpty(user.Phone) ? null : user.Phone);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
