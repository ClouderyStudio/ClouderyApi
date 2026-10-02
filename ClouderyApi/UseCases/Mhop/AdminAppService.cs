using ClouderyApi.Data;
using ClouderyApi.Models;
using ClouderyApi.Models.Mhop;
using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Services.Mhop;
using ClouderyApi.UseCases.Mhop.Mapping;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.UseCases.Mhop;

/// <summary>
/// MHOP 管理后台用例编排：数据看板、帖子巡检、回复审核、AI 回复重新生成、用户与权限、AI 交互日志。
/// 控制器只做 HTTP 绑定与 MhopOk 包装；权限 / 角色 / 状态规则编排与映射在此完成。
/// </summary>
public sealed class AdminAppService
{
    private readonly MhopDbContext _db;
    private readonly MhopCurrentUserAccessor _current;
    private readonly MhopOnlineTracker _online;
    private readonly MhopPasswordHasher _hasher;
    private readonly MhopAiService _ai;
    private readonly MhopContentService _content;

    public AdminAppService(
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

    // ---------------- 数据看板 ----------------

    public async Task<AdminStatsOut> StatsAsync()
    {
        var since = DateTime.UtcNow.AddHours(-24);
        return new AdminStatsOut
        {
            Users = await _db.MhopUsers.CountAsync(),
            // 草稿（status=3）是作者私有内容，不计入后台统计
            Posts = await _db.MhopPosts.CountAsync(p => p.Status != ContentStatus.Draft),
            Replies = await _db.MhopReplies.CountAsync(r => r.Status != ContentStatus.Draft),
            Assessments = await _db.MhopAssessments.CountAsync(),
            AiLogs = await _db.MhopAiLogs.CountAsync(),
            PendingPosts = await _db.MhopPosts.CountAsync(p => p.Status == ContentStatus.Pending),
            CrisisPosts = await _db.MhopPosts.CountAsync(p => p.Crisis
                && p.Status != ContentStatus.Rejected && p.Status != ContentStatus.Draft),
            PendingReplies = await _db.MhopReplies.CountAsync(
                r => r.Status == ContentStatus.Pending && !r.IsAi),
            RejectedReplies = await _db.MhopReplies.CountAsync(r => r.Status == ContentStatus.Rejected),
            AiFlaggedPosts = await _db.MhopPosts.CountAsync(
                p => p.AiFlag != string.Empty && p.Status != ContentStatus.Draft),
            AiFlaggedReplies = await _db.MhopReplies.CountAsync(
                r => r.AiFlag != string.Empty && r.Status != ContentStatus.Draft),
            NewPosts24h = await _db.MhopPosts.CountAsync(p => p.CreatedAt >= since),
            NewUsers24h = await _db.MhopUsers.CountAsync(u => u.CreatedAt >= since),
            Online = _online.Count(),
        };
    }

    // ---------------- 帖子巡检 ----------------

    public async Task<IReadOnlyList<AdminPostListItemOut>> ListPostsAsync(int? status, string? flag)
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
        return posts.Select(p => MhopAdminMapper.ToPostListItem(
            p, users, replyCounts.GetValueOrDefault(p.Id, 0))).ToList();
    }

    public async Task<OkOut> ModeratePostAsync(int postId, ModerateIn body)
    {
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) throw new MhopApiException(404, "帖子不存在");

        var approved = body.Action switch
        {
            "approve" => true,
            "reject" => false,
            _ => throw new DomainRuleException("非法操作"),
        };
        if (approved) post.Publish(body.Note);
        else post.Reject(body.Note);
        await _db.SaveChangesAsync();

        // AI 自动回复只在「首次通过审核」时生成；隐藏后重新展示沿用已有回复，不重复调用大模型。
        // 需要强制刷新时用 POST posts/{id}/ai-reply/regenerate。
        if (approved) await _ai.EnsureForumReplyAsync(post.Id, post.Content, post.Crisis);

        return new OkOut();
    }

    /// <summary>强制重新生成某帖的 AI 自动回复：先清理旧回复，再调用大模型生成一条新的。</summary>
    public async Task<OkOut> RegenerateAiReplyAsync(int postId)
    {
        var post = await _db.MhopPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) throw new MhopApiException(404, "帖子不存在");
        if (post.Status != ContentStatus.Published)
            throw new DomainRuleException("仅公开中的帖子可以生成 AI 自动回复");

        await _ai.RegenerateForumReplyAsync(post.Id, post.Content, post.Crisis);
        return new OkOut();
    }

    /// <summary>管理员删除帖子：不限作者与状态，连同其全部回复、点赞、AI 日志与图片一并清理。</summary>
    public async Task<AdminDeletePostOut> DeletePostAsync(int postId, CancellationToken cancellationToken = default)
    {
        var post = await _db.MhopPosts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) throw new MhopApiException(404, "帖子不存在");

        var deletedReplies = await _content.DeletePostAsync(post, cancellationToken);
        return new AdminDeletePostOut { DeletedReplies = deletedReplies };
    }

    // ---------------- 回复审核 ----------------

    public async Task<IReadOnlyList<AdminReplyListItemOut>> ListRepliesAsync(int? status, string? flag)
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

        return replies.Select(r =>
        {
            var postContent = posts.TryGetValue(r.PostId, out var post) ? post.Content : string.Empty;
            return MhopAdminMapper.ToReplyListItem(r, users, postContent);
        }).ToList();
    }

    public async Task<OkOut> ModerateReplyAsync(int replyId, ModerateIn body)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");

        var approved = body.Action switch
        {
            "approve" => true,
            "reject" => false,
            _ => throw new DomainRuleException("非法操作"),
        };
        if (approved) reply.Publish(body.Note);
        else reply.Reject(body.Note);
        await _db.SaveChangesAsync();
        return new OkOut();
    }

    /// <summary>撤回 AI 回复：对所有用户即时隐藏正文，保留内容与原因以备审计，可恢复。</summary>
    public async Task<OkOut> RecallReplyAsync(int replyId, RecallIn body)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");
        if (!reply.IsAi) throw new DomainRuleException("仅支持撤回 AI 回复；人类回复请使用驳回");

        var reason = (body.Reason ?? string.Empty).Trim();
        if (reason.Length == 0) throw new DomainRuleException("请填写撤回原因");

        reply.Recall(reason);
        await _db.SaveChangesAsync();
        return new OkOut();
    }

    /// <summary>恢复被撤回的 AI 回复，重新公开展示。</summary>
    public async Task<OkOut> RestoreReplyAsync(int replyId)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");
        if (!reply.IsAi) throw new DomainRuleException("仅支持恢复 AI 回复");

        reply.Restore();
        await _db.SaveChangesAsync();
        return new OkOut();
    }

    /// <summary>管理员删除回复：不限作者与状态，连同其点赞、AI 日志与图片一并清理。</summary>
    public async Task<OkOut> DeleteReplyAsync(int replyId, CancellationToken cancellationToken = default)
    {
        var reply = await _db.MhopReplies.FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null) throw new MhopApiException(404, "回复不存在");

        await _content.DeleteReplyAsync(reply, cancellationToken);
        return new OkOut();
    }

    // ---------------- 用户管理 ----------------

    public async Task<IReadOnlyList<AdminUserListItemOut>> ListUsersAsync()
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

        return users.Select(u => MhopAdminMapper.ToUserListItem(
            u, postCounts.GetValueOrDefault(u.Id, 0), replyCounts.GetValueOrDefault(u.Id, 0))).ToList();
    }

    public async Task<OkOut> SetUserStatusAsync(int userId, StatusIn body)
    {
        if (!MhopUserStatus.IsValid(body.Status))
            throw new DomainRuleException("非法状态");

        var admin = await _current.RequirePermAsync(MhopAdminPermissions.Users);
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        GuardStaffTarget(admin, user);
        if (body.Status == MhopUserStatus.Disabled)
            user.Disable(admin.Id, user.IsSuperAdmin && await CountSuperAdminsAsync() <= 1);
        else
            user.Enable();

        await _db.SaveChangesAsync();
        return new OkOut();
    }

    /// <summary>设置用户标识。badge 为空字符串表示清除标识。</summary>
    public async Task<AdminBadgeResultOut> SetUserBadgeAsync(int userId, BadgeIn body)
    {
        var admin = await _current.RequirePermAsync(MhopAdminPermissions.Users);
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        GuardStaffTarget(admin, user);

        var badge = user.SetBadge(body.Badge);
        await _db.SaveChangesAsync();
        return new AdminBadgeResultOut { Badge = badge };
    }

    /// <summary>
    /// 角色管理（仅超级管理员）：
    /// promote 普通用户→普通管理员（可同时带模块权限）；demote 普通管理员→普通用户；
    /// promote_super 指定超级管理员；demote_super 超管降为普通管理员（保留其模块授权记录）。
    /// </summary>
    public async Task<AdminRoleResultOut> SetUserRoleAsync(int userId, RoleIn body)
    {
        var admin = await _current.RequireSuperAsync();
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");

        switch (body.Action)
        {
            case "promote":
                user.Promote(PermissionSet.From(body.Permissions));
                await _db.SaveChangesAsync();
                return new AdminRoleResultOut
                {
                    Role = MhopUserRole.Admin,
                    Permissions = MhopAdminPermissions.Parse(user.Permissions),
                };

            case "demote":
                user.Demote(admin.Id);
                await _db.SaveChangesAsync();
                return new AdminRoleResultOut
                {
                    Role = MhopUserRole.User,
                    Permissions = new List<string>(),
                };

            case "promote_super":
                // 保留其 permissions 列，便于日后取消超管时恢复原来的模块授权
                user.PromoteSuper();
                await _db.SaveChangesAsync();
                return new AdminRoleResultOut { Role = MhopUserRole.SuperAdmin };

            case "demote_super":
                var restoredPerms = user.DemoteSuper(admin.Id, await CountSuperAdminsAsync() <= 1);
                await _db.SaveChangesAsync();
                return new AdminRoleResultOut
                {
                    Role = MhopUserRole.Admin,
                    Permissions = restoredPerms.Codes.ToList(),
                };

            default:
                throw new DomainRuleException("非法操作");
        }
    }

    /// <summary>管理员重置用户密码。</summary>
    public async Task<OkOut> ResetUserPasswordAsync(int userId, ResetPasswordIn body)
    {
        var admin = await _current.RequirePermAsync(MhopAdminPermissions.Users);
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        GuardStaffTarget(admin, user);

        var newPassword = (body.Password ?? string.Empty).Trim();
        if (newPassword.Length < 6) throw new DomainRuleException("密码至少 6 位");

        user.PasswordHash = _hasher.Hash(newPassword);
        await _db.SaveChangesAsync();
        return new OkOut();
    }

    /// <summary>管理员删除用户：连同其名下帖子、回复、点赞、AI 日志与图片一并清理，不可恢复。</summary>
    public async Task<AdminDeleteUserOut> DeleteUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var admin = await _current.RequirePermAsync(MhopAdminPermissions.Users);
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        if (user.Id == admin.Id) throw new DomainRuleException("不能删除当前登录的账号");
        if (user.IsSuperAdmin)
            throw new DomainRuleException("超级管理员账号不可删除");
        GuardStaffTarget(admin, user);

        var (posts, replies) = await _content.DeleteUserAsync(user, cancellationToken);
        return new AdminDeleteUserOut { DeletedPosts = posts, DeletedReplies = replies };
    }

    // ---------------- 模块权限分配（超级管理员） ----------------

    public async Task<AdminPermissionsOut> GetPermissionsAsync(int userId)
    {
        var user = await _db.MhopUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");
        return new AdminPermissionsOut
        {
            UserId = user.Id,
            Role = user.Role,
            Permissions = MhopAdminPermissions.Parse(user.Permissions),
            AllPermissions = MhopAdminPermissions.All
                .Select(MhopAdminMapper.ToPermissionOption).ToList(),
        };
    }

    public async Task<AdminSetPermissionsOut> SetPermissionsAsync(int userId, PermissionsIn body)
    {
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) throw new MhopApiException(404, "用户不存在");

        user.SetPermissions(PermissionSet.From(body.Permissions));
        await _db.SaveChangesAsync();
        return new AdminSetPermissionsOut { Permissions = MhopAdminPermissions.Parse(user.Permissions) };
    }

    // ---------------- AI 交互日志 ----------------

    public async Task<IReadOnlyList<AdminAiLogItemOut>> ListAiLogsAsync()
    {
        var logs = await _db.MhopAiLogs.OrderByDescending(l => l.CreatedAt).Take(200).ToListAsync();

        // 关联 replies：forum 日志需带出回复的撤回状态，便于在本页直接撤回/恢复
        var replyIds = logs.Where(l => l.ReplyId.HasValue).Select(l => l.ReplyId!.Value).Distinct().ToList();
        var replies = replyIds.Count == 0
            ? new Dictionary<int, MhopReply>()
            : await _db.MhopReplies.AsNoTracking()
                .Where(r => replyIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id);

        return logs.Select(log =>
        {
            var reply = log.ReplyId.HasValue && replies.TryGetValue(log.ReplyId.Value, out var found) ? found : null;
            return MhopAdminMapper.ToAiLogItem(log, reply);
        }).ToList();
    }

    // ---------------- 内部辅助 ----------------

    private async Task<Dictionary<int, MhopUser>> LoadUsersAsync(IEnumerable<int?> userIds)
    {
        var ids = userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, MhopUser>();
        return await _db.MhopUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);
    }

    /// <summary>对管理员/超管目标的敏感操作（停用/改密/标识/删除）仅超管可执行；操作自己另有校验。</summary>
    private static void GuardStaffTarget(MhopUser operatorUser, MhopUser target)
    {
        if (target.IsStaff && !operatorUser.IsSuperAdmin)
            throw new MhopApiException(403, "仅超级管理员可操作管理员账号");
    }

    private Task<int> CountSuperAdminsAsync()
        => _db.MhopUsers.CountAsync(u => u.Role == MhopUserRole.SuperAdmin && u.Status == MhopUserStatus.Active);
}
