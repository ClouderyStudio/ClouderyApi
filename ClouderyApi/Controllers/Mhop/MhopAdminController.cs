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
            posts = await _db.MhopPosts.CountAsync(p => p.Status != ContentStatus.Draft),
            replies = await _db.MhopReplies.CountAsync(r => r.Status != ContentStatus.Draft),
            assessments = await _db.MhopAssessments.CountAsync(),
            ai_logs = await _db.MhopAiLogs.CountAsync(),
            pending_posts = await _db.MhopPosts.CountAsync(p => p.Status == ContentStatus.Pending),
            crisis_posts = await _db.MhopPosts.CountAsync(p => p.Crisis
                && p.Status != ContentStatus.Rejected && p.Status != ContentStatus.Draft),
            pending_replies = await _db.MhopReplies.CountAsync(
                r => r.Status == ContentStatus.Pending && !r.IsAi),
            rejected_replies = await _db.MhopReplies.CountAsync(r => r.Status == ContentStatus.Rejected),
            ai_flagged_posts = await _db.MhopPosts.CountAsync(
                p => p.AiFlag != string.Empty && p.Status != ContentStatus.Draft),
            ai_flagged_replies = await _db.MhopReplies.CountAsync(
                r => r.AiFlag != string.Empty && r.Status != ContentStatus.Draft),
            new_posts_24h = await _db.MhopPosts.CountAsync(p => p.CreatedAt >= since),
            new_users_24h = await _db.MhopUsers.CountAsync(u => u.CreatedAt >= since),
            online = _online.Count(),
        });
    }

    [HttpGet("posts")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> ListPosts([FromQuery] int? status = null, [FromQuery] string? flag = null)
    {
        // 草稿不对后台展示（作者主动取消审核后的私有内容）
        var query = _db.MhopPosts.Where(p => p.Status != ContentStatus.Draft);
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        // flag：按 AI 初筛结论过滤（suspect / violation / unavailable）
        if (!string.IsNullOrWhiteSpace(flag)) query = query.Where(p => p.AiFlag == flag);
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
                ai_flag = p.AiFlag ?? string.Empty,
                ai_review_note = p.AiReviewNote ?? string.Empty,
                ai_reviewed_at = p.AiReviewedAt,
                reply_count = replyCounts.GetValueOrDefault(p.Id, 0),
                created_at = p.CreatedAt,
            };
        }).ToList());
    }

    [HttpPost("posts/{postId:int}/moderate")]
    [MhopPerm(MhopAdminPermissions.Review)]
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
        if (approved) post.Publish(body.Note);
        else post.Reject(body.Note);
        await _db.SaveChangesAsync();

        // AI 自动回复只在「首次通过审核」时生成；隐藏后重新展示沿用已有回复，不重复调用大模型。
        // 需要强制刷新时用 POST posts/{id}/ai-reply/regenerate。
        if (approved) await _ai.EnsureForumReplyAsync(post.Id, post.Content, post.Crisis);

        return MhopOk(new { ok = true });
    }

    /// <summary>强制重新生成某帖的 AI 自动回复：先清理旧回复，再调用大模型生成一条新的。</summary>
    [HttpPost("posts/{postId:int}/ai-reply/regenerate")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> RegenerateAiReply(int postId)
    {
        var post = await _db.MhopPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) throw new MhopApiException(404, "帖子不存在");
        if (post.Status != ContentStatus.Published)
            throw new MhopApiException(400, "仅公开中的帖子可以生成 AI 自动回复");

        await _ai.RegenerateForumReplyAsync(post.Id, post.Content, post.Crisis);
        return MhopOk(new { ok = true });
    }

    /// <summary>管理员删除帖子：不限作者与状态，连同其全部回复、点赞、AI 日志与图片一并清理。</summary>
    [HttpDelete("posts/{postId:int}")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> DeletePost(int postId)
    {
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) throw new MhopApiException(404, "帖子不存在");

        var deletedReplies = await _content.DeletePostAsync(post, HttpContext.RequestAborted);
        return MhopOk(new { ok = true, deleted_replies = deletedReplies });
    }

    [HttpGet("replies")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> ListReplies([FromQuery] int? status = null, [FromQuery] string? flag = null)
    {
        var query = _db.MhopReplies.Where(r => r.Status != ContentStatus.Draft);
        if (status.HasValue) query = query.Where(r => r.Status == status.Value);
        // flag：按 AI 初筛结论过滤（suspect / violation / unavailable）
        if (!string.IsNullOrWhiteSpace(flag)) query = query.Where(r => r.AiFlag == flag);
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
                ai_flag = r.AiFlag ?? string.Empty,
                ai_review_note = r.AiReviewNote ?? string.Empty,
                ai_reviewed_at = r.AiReviewedAt,
                recalled = r.Recalled,
                recall_reason = r.RecallReason ?? string.Empty,
                created_at = r.CreatedAt,
            };
        }).ToList());
    }

    [HttpPost("replies/{replyId:int}/moderate")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> ModerateReply(int replyId, [FromBody] ModerateIn body)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");

        var approved = body.Action switch
        {
            "approve" => true,
            "reject" => false,
            _ => throw new MhopApiException(400, "非法操作"),
        };
        if (approved) reply.Publish(body.Note);
        else reply.Reject(body.Note);
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    /// <summary>撤回 AI 回复：对所有用户即时隐藏正文，保留内容与原因以备审计，可恢复。</summary>
    [HttpPost("replies/{replyId:int}/recall")]
    [MhopPerm(MhopAdminPermissions.Review, MhopAdminPermissions.AiLogs)]
    public async Task<IActionResult> RecallReply(int replyId, [FromBody] RecallIn body)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");
        if (!reply.IsAi) throw new MhopApiException(400, "仅支持撤回 AI 回复；人类回复请使用驳回");

        var reason = (body.Reason ?? string.Empty).Trim();
        if (reason.Length == 0) throw new MhopApiException(400, "请填写撤回原因");

        reply.Recall(reason);
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    /// <summary>恢复被撤回的 AI 回复，重新公开展示。</summary>
    [HttpPost("replies/{replyId:int}/restore")]
    [MhopPerm(MhopAdminPermissions.Review, MhopAdminPermissions.AiLogs)]
    public async Task<IActionResult> RestoreReply(int replyId)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");
        if (!reply.IsAi) throw new MhopApiException(400, "仅支持恢复 AI 回复");

        reply.Restore();
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    /// <summary>管理员删除回复：不限作者与状态，连同其点赞、AI 日志与图片一并清理。</summary>
    [HttpDelete("replies/{replyId:int}")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> DeleteReply(int replyId)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");

        await _content.DeleteReplyAsync(reply, HttpContext.RequestAborted);
        return MhopOk(new { ok = true });
    }

    [HttpGet("users")]
    [MhopPerm(MhopAdminPermissions.Users)]
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
                // 超管隐式全权限，列表中用空数组 + is_super 表达，与独立后端版本保持一致
                permissions = MhopAdminPermissions.IsSuper(u.Role) ? new List<string>() : item.Permissions,
                is_super = MhopAdminPermissions.IsSuper(u.Role),
                item.CreatedAt,
                post_count = postCounts.GetValueOrDefault(u.Id),
                reply_count = replyCounts.GetValueOrDefault(u.Id),
            };
        }).ToList());
    }

    [HttpPost("users/{userId:int}/status")]
    [MhopPerm(MhopAdminPermissions.Users)]
    public async Task<IActionResult> SetUserStatus(int userId, [FromBody] StatusIn body)
    {
        if (!MhopUserStatus.IsValid(body.Status))
            throw new MhopApiException(400, "非法状态");

        var admin = await _current.RequirePermAsync(MhopAdminPermissions.Users);
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        GuardStaffTarget(admin, user);
        if (body.Status == MhopUserStatus.Disabled)
            user.Disable(admin.Id, user.IsSuperAdmin && await CountSuperAdminsAsync() <= 1);
        else
            user.Enable();

        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    /// <summary>设置用户标识。badge 为空字符串表示清除标识。</summary>
    [HttpPost("users/{userId:int}/badge")]
    [MhopPerm(MhopAdminPermissions.Users)]
    public async Task<IActionResult> SetUserBadge(int userId, [FromBody] BadgeIn body)
    {
        var admin = await _current.RequirePermAsync(MhopAdminPermissions.Users);
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        GuardStaffTarget(admin, user);

        var badge = user.SetBadge(body.Badge);
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true, badge });
    }

    /// <summary>
    /// 角色管理（仅超级管理员）：
    /// promote 普通用户→普通管理员（可同时带模块权限）；demote 普通管理员→普通用户；
    /// promote_super 指定超级管理员；demote_super 超管降为普通管理员（保留其模块授权记录）。
    /// </summary>
    [HttpPost("users/{userId:int}/role")]
    [MhopSuper]
    public async Task<IActionResult> SetUserRole(int userId, [FromBody] RoleIn body)
    {
        var admin = await _current.RequireSuperAsync();
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");

        switch (body.Action)
        {
            case "promote":
                user.Promote(PermissionSet.From(body.Permissions));
                await _db.SaveChangesAsync();
                return MhopOk(new
                {
                    ok = true,
                    role = MhopUserRole.Admin,
                    permissions = MhopAdminPermissions.Parse(user.Permissions),
                });

            case "demote":
                user.Demote(admin.Id);
                await _db.SaveChangesAsync();
                return MhopOk(new { ok = true, role = MhopUserRole.User, permissions = new List<string>() });

            case "promote_super":
                // 保留其 permissions 列，便于日后取消超管时恢复原来的模块授权
                user.PromoteSuper();
                await _db.SaveChangesAsync();
                return MhopOk(new { ok = true, role = MhopUserRole.SuperAdmin });

            case "demote_super":
                var restoredPerms = user.DemoteSuper(admin.Id, await CountSuperAdminsAsync() <= 1);
                await _db.SaveChangesAsync();
                return MhopOk(new { ok = true, role = MhopUserRole.Admin, permissions = restoredPerms.Codes });

            default:
                throw new MhopApiException(400, "非法操作");
        }
    }

    /// <summary>管理员重置用户密码。</summary>
    [HttpPost("users/{userId:int}/reset-password")]
    [MhopPerm(MhopAdminPermissions.Users)]
    public async Task<IActionResult> ResetUserPassword(int userId, [FromBody] ResetPasswordIn body)
    {
        var admin = await _current.RequirePermAsync(MhopAdminPermissions.Users);
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        GuardStaffTarget(admin, user);

        var newPassword = (body.Password ?? string.Empty).Trim();
        if (newPassword.Length < 6) throw new MhopApiException(400, "密码至少 6 位");

        user.PasswordHash = _hasher.Hash(newPassword);
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true });
    }

    /// <summary>管理员删除用户：连同其名下帖子、回复、点赞、AI 日志与图片一并清理，不可恢复。</summary>
    [HttpDelete("users/{userId:int}")]
    [MhopPerm(MhopAdminPermissions.Users)]
    public async Task<IActionResult> DeleteUser(int userId)
    {
        var admin = await _current.RequirePermAsync(MhopAdminPermissions.Users);
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        if (user.Id == admin.Id) throw new MhopApiException(400, "不能删除当前登录的账号");
        if (user.IsSuperAdmin)
            throw new MhopApiException(400, "超级管理员账号不可删除");
        GuardStaffTarget(admin, user);

        var (posts, replies) = await _content.DeleteUserAsync(user, HttpContext.RequestAborted);
        return MhopOk(new { ok = true, deleted_posts = posts, deleted_replies = replies });
    }

    // ---------------- 模块权限分配（超级管理员） ----------------

    [HttpGet("users/{userId:int}/permissions")]
    [MhopSuper]
    public async Task<IActionResult> GetPermissions(int userId)
    {
        var user = await _db.MhopUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        return MhopOk(new
        {
            user_id = user.Id,
            role = user.Role,
            permissions = MhopAdminPermissions.Parse(user.Permissions),
            all_permissions = MhopAdminPermissions.All
                .Select(code => new { code, name = MhopAdminPermissions.Labels[code] }),
        });
    }

    [HttpPut("users/{userId:int}/permissions")]
    [MhopSuper]
    public async Task<IActionResult> SetPermissions(int userId, [FromBody] PermissionsIn body)
    {
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");

        user.SetPermissions(PermissionSet.From(body.Permissions));
        await _db.SaveChangesAsync();
        return MhopOk(new { ok = true, permissions = MhopAdminPermissions.Parse(user.Permissions) });
    }

    [HttpGet("ai-logs")]
    [MhopPerm(MhopAdminPermissions.AiLogs)]
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

    /// <summary>对管理员/超管目标的敏感操作（停用/改密/标识/删除）仅超管可执行；操作自己另有校验。</summary>
    private static void GuardStaffTarget(MhopUser operatorUser, MhopUser target)
    {
        if (target.IsStaff && !operatorUser.IsSuperAdmin)
            throw new MhopApiException(403, "仅超级管理员可操作管理员账号");
    }

    private Task<int> CountSuperAdminsAsync()
        => _db.MhopUsers.CountAsync(u => u.Role == MhopUserRole.SuperAdmin && u.Status == MhopUserStatus.Active);
}
