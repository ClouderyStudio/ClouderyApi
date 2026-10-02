using System.Text.Json.Serialization;

namespace ClouderyApi.Models.Mhop.DTOs;

public class ModerateIn
{
    /// <summary>approve / reject(remove)</summary>
    [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;

    [JsonPropertyName("note")] public string Note { get; set; } = string.Empty;
}

public class RecallIn
{
    /// <summary>撤回原因（必填，留审计痕迹）。</summary>
    [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
}

public class StatusIn
{
    /// <summary>active / disabled</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
}

public class BadgeIn
{
    [JsonPropertyName("badge")] public string? Badge { get; set; }
}

public class RoleIn
{
    /// <summary>promote / demote / promote_super / demote_super</summary>
    [JsonPropertyName("action")] public string? Action { get; set; }

    /// <summary>提升为管理员时一并授予的模块权限码（仅 superadmin 调用时生效）。</summary>
    [JsonPropertyName("permissions")] public List<string>? Permissions { get; set; }
}

public class PermissionsIn
{
    /// <summary>模块权限码数组，如 ["dashboard","review"]</summary>
    [JsonPropertyName("permissions")] public List<string>? Permissions { get; set; }
}

public class ResetPasswordIn
{
    [JsonPropertyName("password")] public string? Password { get; set; }
}

// ---- Stage 2 应用层输出 DTO：管理后台用例（AdminAppService）----
// 属性声明顺序即 JSON 字段顺序；字段名由 MhopJson 的蛇形命名策略生成，
// 仅 new_posts_24h / new_users_24h 因命名策略不会插入字母与数字间的下划线而显式声明。

/// <summary>数据看板统计。</summary>
public class AdminStatsOut
{
    public int Users { get; set; }

    public int Posts { get; set; }

    public int Replies { get; set; }

    public int Assessments { get; set; }

    public int AiLogs { get; set; }

    public int PendingPosts { get; set; }

    public int CrisisPosts { get; set; }

    public int PendingReplies { get; set; }

    public int RejectedReplies { get; set; }

    public int AiFlaggedPosts { get; set; }

    public int AiFlaggedReplies { get; set; }

    [JsonPropertyName("new_posts_24h")] public int NewPosts24h { get; set; }

    [JsonPropertyName("new_users_24h")] public int NewUsers24h { get; set; }

    public int Online { get; set; }
}

/// <summary>后台帖子巡检列表项（后台视角返回真实作者与手机号）。</summary>
public class AdminPostListItemOut
{
    public int Id { get; set; }

    public string Content { get; set; } = string.Empty;

    public string Board { get; set; } = string.Empty;

    public int Status { get; set; }

    public bool Crisis { get; set; }

    public bool IsAnonymous { get; set; }

    public string? Author { get; set; }

    public string? AuthorPhone { get; set; }

    public string ReviewNote { get; set; } = string.Empty;

    public string AiFlag { get; set; } = string.Empty;

    public string AiReviewNote { get; set; } = string.Empty;

    public DateTime? AiReviewedAt { get; set; }

    public int ReplyCount { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>后台回复审核列表项（含父帖摘要）。</summary>
public class AdminReplyListItemOut
{
    public int Id { get; set; }

    public int PostId { get; set; }

    public string PostExcerpt { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int Status { get; set; }

    public bool IsAi { get; set; }

    public bool Crisis { get; set; }

    public bool IsAnonymous { get; set; }

    public string? Author { get; set; }

    public string? AuthorPhone { get; set; }

    public string ReviewNote { get; set; } = string.Empty;

    public string AiFlag { get; set; } = string.Empty;

    public string AiReviewNote { get; set; } = string.Empty;

    public DateTime? AiReviewedAt { get; set; }

    public bool Recalled { get; set; }

    public string RecallReason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

/// <summary>后台用户列表项（附带内容数量，便于删除前评估影响）。</summary>
public class AdminUserListItemOut
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Avatar { get; set; } = string.Empty;

    public string Badge { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = [];

    public bool IsSuper { get; set; }

    public DateTime CreatedAt { get; set; }

    public int PostCount { get; set; }

    public int ReplyCount { get; set; }
}

/// <summary>模块权限目录项。</summary>
public class AdminPermissionOptionOut
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

/// <summary>某用户的角色与模块权限。</summary>
public class AdminPermissionsOut
{
    public int UserId { get; set; }

    public string Role { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = [];

    public List<AdminPermissionOptionOut> AllPermissions { get; set; } = [];
}

/// <summary>分配模块权限结果。</summary>
public class AdminSetPermissionsOut
{
    public bool Ok { get; set; } = true;

    public List<string> Permissions { get; set; } = [];
}

/// <summary>角色变更结果：promote_super 分支不返回 permissions（保持与原响应字段一致）。</summary>
public class AdminRoleResultOut
{
    public bool Ok { get; set; } = true;

    public string Role { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Permissions { get; set; }
}

/// <summary>设置用户标识结果。</summary>
public class AdminBadgeResultOut
{
    public bool Ok { get; set; } = true;

    public string Badge { get; set; } = string.Empty;
}

/// <summary>管理员删除帖子结果。</summary>
public class AdminDeletePostOut
{
    public bool Ok { get; set; } = true;

    public int DeletedReplies { get; set; }
}

/// <summary>管理员删除用户结果。</summary>
public class AdminDeleteUserOut
{
    public bool Ok { get; set; } = true;

    public int DeletedPosts { get; set; }

    public int DeletedReplies { get; set; }
}

/// <summary>AI 交互日志列表项（forum 日志带出关联回复状态）。</summary>
public class AdminAiLogItemOut
{
    public int Id { get; set; }

    public string Module { get; set; } = string.Empty;

    public string Engine { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public string Response { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public int? ReplyId { get; set; }

    public int? PostId { get; set; }

    public int? ReplyStatus { get; set; }

    public bool Recalled { get; set; }

    public string RecallReason { get; set; } = string.Empty;
}

