namespace ClouderyApi.Modules.Scforge.Api.Contracts;

/*
 * SCForge 对外 DTO。
 *
 * 字段名与前端 src/api/types.ts 一一对应（ASP.NET Core 默认 camelCase 序列化），
 * 时间一律 UTC（mapper 用 AsUtc 标注，序列化后带 Z 后缀）。
 *
 * 审核字段（status / reviewNote / reviewedAt / reviewedBy）同时出现在列表项与详情里：
 * 「我的插件」与后台审核队列都需要不点进详情就能看到状态与驳回理由。
 */

/// <summary>资源作者快照。</summary>
public class ScforgeAuthorDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Avatar { get; set; }
}

/// <summary>列表项：浏览页、首页卡片、我的资源与后台列表共用。</summary>
public class ScforgeAddonSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>资源类型：plugin（插件）或 mod（模组，会下发到客户端）。</summary>
    public string Kind { get; set; } = "plugin";
    public string Summary { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string Category { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public string GameVersion { get; set; } = string.Empty;
    public ScforgeAuthorDto Author { get; set; } = new();
    public int Downloads { get; set; }
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
    public int Score { get; set; }
    public int CommentCount { get; set; }
    /// <summary>当前调用者可见的版本数（匿名/普通用户只看得到已通过审核的版本，作者与管理员看到全部）。</summary>
    public int VersionCount { get; set; }
    public string? LatestVersion { get; set; }
    public DateTimeOffset? LatestReleaseAt { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool Featured { get; set; }

    /// <summary>1 / -1 / 0；未登录恒为 0。</summary>
    public int MyVote { get; set; }

    /// <summary>pending / published / rejected。</summary>
    public string Status { get; set; } = "pending";

    /// <summary>审核意见（驳回理由或通过备注）。</summary>
    public string? ReviewNote { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewedBy { get; set; }

    /// <summary>已通过审核的版本数（公开可见的版本）。</summary>
    public int PublishedVersionCount { get; set; }
}

/// <summary>详情页：列表字段 + 正文、链接、截图文集与全部版本。</summary>
public sealed class ScforgeAddonDetailDto : ScforgeAddonSummaryDto
{
    public string Description { get; set; } = string.Empty;
    public string Readme { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string? IssuesUrl { get; set; }
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }
    public string? DonationUrl { get; set; }
    public string? DiscordUrl { get; set; }
    public List<string> Gallery { get; set; } = [];

    /// <summary>当前用户是否为作者本人：可编辑资料、发布/编辑版本、删除。</summary>
    public bool CanManage { get; set; }

    /// <summary>当前用户是否有审核权限（后台审核队列 / 通过 / 驳回）。</summary>
    public bool CanReview { get; set; }

    /// <summary>当前用户是否有内容管理权限（编辑任意资源、删除）。</summary>
    public bool CanManageContent { get; set; }

    public List<ScforgeVersionDto> Versions { get; set; } = [];
}

/// <summary>一个版本（含审核状态）。</summary>
public class ScforgeVersionDto
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Channel { get; set; } = "release";
    public string Changelog { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int Downloads { get; set; }
    public string GameVersion { get; set; } = string.Empty;
    public List<string> GameVersions { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];
    public DateTimeOffset PublishedAt { get; set; }

    /// <summary>下载入口（服务端会校验版本可见性并累加计数）。</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>pending / published / rejected。</summary>
    public string Status { get; set; } = "pending";

    public string? ReviewNote { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }

    /// <summary>所属资源（后台审核队列需要展示它属于谁）。</summary>
    public string AddonId { get; set; } = string.Empty;

    public string AddonName { get; set; } = string.Empty;

    public string AddonSlug { get; set; } = string.Empty;

    /// <summary>所属资源的类型：plugin 或 mod（审核队列据此跳到正确的板块）。</summary>
    public string AddonKind { get; set; } = "plugin";

    public ScforgeAuthorDto Author { get; set; } = new();
}

/// <summary>受支持的游戏版本（生存战争用日期式编号）。</summary>
public class ScforgeGameVersionDto
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>是否仍在内测。</summary>
    public bool Beta { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>声明兼容该版本的资源数（后台列表用于判断能否删除）。</summary>
    public int UsageCount { get; set; }
}

/// <summary>筛选面板里的一项（键 + 展示名 + 命中数量）。</summary>
public class ScforgeFacetDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>浏览页的筛选维度。</summary>
public class ScforgeFacetsDto
{
    /// <summary>插件 / 模组两个板块的计数。</summary>
    public List<ScforgeFacetDto> Kinds { get; set; } = [];

    public List<ScforgeFacetDto> Categories { get; set; } = [];
    public List<ScforgeFacetDto> Tags { get; set; } = [];
    public List<string> GameVersions { get; set; } = [];
}

/// <summary>分页搜索结果（分页字段直接摊平，前端无需再拆一层）。</summary>
public class ScforgeSearchResultDto
{
    public List<ScforgeAddonSummaryDto> Items { get; set; } = [];
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 24;
    public int Total { get; set; }
    public int TotalPages { get; set; }
    public ScforgeFacetsDto Facets { get; set; } = new();
}

/// <summary>一条评论（含它的回复）。</summary>
public class ScforgeCommentDto
{
    public string Id { get; set; } = string.Empty;
    public string AddonId { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string Body { get; set; } = string.Empty;
    public ScforgeAuthorDto Author { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool Edited { get; set; }
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
    public int Score { get; set; }
    public int MyVote { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public List<ScforgeCommentDto> Replies { get; set; } = [];
}

/// <summary>投票后的权威计数，供前端直接覆盖本地状态。</summary>
public class ScforgeVoteStateDto
{
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
    public int Score { get; set; }

    /// <summary>1 / -1 / 0。</summary>
    public int MyVote { get; set; }
}

/// <summary>「我的资源」页顶部的统计卡。</summary>
public class ScforgeMineSummaryDto
{
    public int Addons { get; set; }
    public int Downloads { get; set; }
    public int Upvotes { get; set; }
    public int Comments { get; set; }

    /// <summary>按审核状态分组的数量，用于作者视图上的提示。</summary>
    public int Pending { get; set; }

    public int Published { get; set; }

    public int Rejected { get; set; }
}

/* ------------------------------------------------------------------ */
/* 后台面板                                                            */
/* ------------------------------------------------------------------ */

/// <summary>当前调用者的后台身份：前端据此决定是否显示后台入口与哪些菜单。</summary>
public class ScforgeAdminMeDto
{
    public bool IsAdmin { get; set; }
    public bool IsSuperAdmin { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>有效权限码（超管为全量）。</summary>
    public List<string> Permissions { get; set; } = [];

    /// <summary>权限码目录：键、中文名与说明，前端无需硬编码。</summary>
    public List<ScforgePermissionDto> Catalog { get; set; } = [];
}

/// <summary>一个可授予的权限。</summary>
public class ScforgePermissionDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>后台首页的统计。</summary>
public class ScforgeAdminSummaryDto
{
    public int PendingAddons { get; set; }
    public int PendingVersions { get; set; }
    public int PublishedAddons { get; set; }
    public int RejectedAddons { get; set; }
    public int TotalAddons { get; set; }
    public int TotalDownloads { get; set; }
    public int Admins { get; set; }
}

/// <summary>后台的分页结果（审核队列与全量列表共用）。</summary>
public class ScforgeAdminAddonPageDto
{
    public List<ScforgeAddonSummaryDto> Items { get; set; } = [];
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int Total { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>后台的版本分页结果。</summary>
public class ScforgeAdminVersionPageDto
{
    public List<ScforgeVersionDto> Items { get; set; } = [];
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int Total { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>一名管理员（后台管理员列表）。</summary>
public class ScforgeAdminDto
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Avatar { get; set; }

    /// <summary>super / admin。</summary>
    public string Role { get; set; } = "admin";

    public bool IsSuperAdmin { get; set; }

    /// <summary>有效权限码（超管为全量）。</summary>
    public List<string> Permissions { get; set; } = [];

    public string? GrantedBy { get; set; }

    /// <summary>是否由配置白名单引导而来（这类账号没有库记录）。</summary>
    public bool FromConfig { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>按用户名搜索到的候选用户（用于指定管理员）。</summary>
public class ScforgeUserCandidateDto
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Avatar { get; set; }

    /// <summary>已经是管理员时的现有角色，便于前端提示。</summary>
    public string? CurrentRole { get; set; }
}
