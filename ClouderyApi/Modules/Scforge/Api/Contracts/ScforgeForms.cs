using Microsoft.AspNetCore.Http;

namespace ClouderyApi.Modules.Scforge.Api.Contracts;

/*
 * SCForge 写入侧输入模型。
 *
 * 一律只绑定输入 DTO：主键、时间戳、作者、计数与审核状态都由服务端生成，
 * 客户端多传的字段不会落到实体上（防 over-posting）。
 * 属性全部可空，避免 [ApiController] 为非空引用类型隐式加上 Required。
 */

/// <summary>发布一个新资源（multipart/form-data，含插件包与可选图片）。</summary>
public class ScforgeAddonCreateForm
{
    /// <summary>资源包（必填）：.dll（插件）或 .netmod（模组，会下发到客户端）。</summary>
    public IFormFile? Package { get; set; }

    /// <summary>资源类型：plugin（插件，仅服务端）或 mod（模组，会下发到客户端）。创建后不可更改。</summary>
    public string? Kind { get; set; }

    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Readme { get; set; }
    public string? Category { get; set; }
    public string? GameVersion { get; set; }

    /// <summary>标签键；同名多值可在表单里重复出现。</summary>
    public List<string>? Tags { get; set; }

    public string? SourceUrl { get; set; }
    public string? IssuesUrl { get; set; }
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }
    public string? DonationUrl { get; set; }
    public string? DiscordUrl { get; set; }

    /// <summary>资源图标（可选）：JPG / PNG / WebP / GIF。</summary>
    public IFormFile? Icon { get; set; }

    /// <summary>截图（可选，最多 6 张）。</summary>
    public List<IFormFile>? Gallery { get; set; }

    // ---- 首个版本 ----
    public string? Version { get; set; }
    public string? Channel { get; set; }
    public string? Changelog { get; set; }
    public List<string>? GameVersions { get; set; }
    public List<string>? Dependencies { get; set; }
}

/// <summary>
/// 作者编辑资源资料（multipart/form-data）。
/// slug 与名称不可改（前者是 URL 契约，后者改名会让老链接的搜索结果错位）；
/// 编辑会**重新进入待审核**，审核通过前不对公众可见。
/// </summary>
public class ScforgeAddonEditForm
{
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Readme { get; set; }
    public string? Category { get; set; }
    public string? GameVersion { get; set; }
    public List<string>? Tags { get; set; }
    public string? SourceUrl { get; set; }
    public string? IssuesUrl { get; set; }
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }
    public string? DonationUrl { get; set; }
    public string? DiscordUrl { get; set; }

    /// <summary>传入即替换图标。</summary>
    public IFormFile? Icon { get; set; }

    /// <summary>传入即整体替换截图列表（最多 6 张）。</summary>
    public List<IFormFile>? Gallery { get; set; }

    /// <summary>清空图标。</summary>
    public bool? ClearIcon { get; set; }

    /// <summary>清空截图。</summary>
    public bool? ClearGallery { get; set; }
}

/// <summary>为已有资源追加一个版本（multipart/form-data）。</summary>
public class ScforgeVersionCreateForm
{
    public IFormFile? Package { get; set; }

    public string? Version { get; set; }
    public string? Channel { get; set; }
    public string? Changelog { get; set; }
    public List<string>? GameVersions { get; set; }
    public List<string>? Dependencies { get; set; }

    /// <summary>缺省时沿用资源的主游戏版本。</summary>
    public string? GameVersion { get; set; }
}

/// <summary>编辑一个版本的元数据（JSON）；编辑会重新进入待审核。</summary>
public class ScforgeVersionEditIn
{
    public string? Channel { get; set; }
    public string? Changelog { get; set; }
    public string? GameVersion { get; set; }
    public List<string>? GameVersions { get; set; }
    public List<string>? Dependencies { get; set; }
}

/// <summary>替换某个版本的资源包文件（multipart/form-data）；替换会重新进入待审核。</summary>
public class ScforgeVersionFileForm
{
    public IFormFile? Package { get; set; }
}

/// <summary>添加一个受支持的游戏版本（仅超级管理员）。</summary>
public class ScforgeGameVersionIn
{
    /// <summary>版本号，形如 x26.07.01。</summary>
    public string? Version { get; set; }

    /// <summary>是否仍在内测（未正式发布）。</summary>
    public bool? Beta { get; set; }
}

/// <summary>审核一个资源或版本：通过 / 驳回，驳回必须给出理由。</summary>
public class ScforgeReviewIn
{
    public bool Approve { get; set; }

    /// <summary>审核意见；驳回时必填，通过时可留空或写备注。</summary>
    public string? Note { get; set; }
}

/// <summary>指定或调整一名管理员（仅超级管理员可调用）。</summary>
public class ScforgeAdminIn
{
    /// <summary>用户名或邮箱（Identity 域 users 表里查找）。</summary>
    public string? Username { get; set; }

    /// <summary>super / admin。</summary>
    public string? Role { get; set; }

    /// <summary>普通管理员的权限码；超管忽略（恒为全量）。</summary>
    public List<string>? Permissions { get; set; }
}

/// <summary>发表评论或回复。</summary>
public class ScforgeCommentCreateIn
{
    public string? Body { get; set; }

    /// <summary>父评论 Id：为空即顶层评论。</summary>
    public string? ParentId { get; set; }
}

/// <summary>编辑自己评论的正文。</summary>
public class ScforgeCommentUpdateIn
{
    public string? Body { get; set; }
}
