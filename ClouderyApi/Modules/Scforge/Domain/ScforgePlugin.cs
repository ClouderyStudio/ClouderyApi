using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// 一个资源（聚合根）：插件或模组，用 <see cref="Kind"/> 区分。作者信息为写入时快照，避免跨域联表依赖 Identity 库。
/// </summary>
/// <remarks>
/// 命名说明：内部实体名与表名沿用 Plugin / <c>scforge_plugins</c>（避免数据库迁移），
/// 对外 API 契约（路由 <c>/scforge/addons</c>、DTO 类名与 JSON 字段）一律称 addon。
/// </remarks>
[Table("scforge_plugins")]
public class ScforgePlugin
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>URL 标识，全局唯一。</summary>
    [Required]
    [MaxLength(ScforgeCatalog.MaxSlugLength)]
    public string Slug { get; set; } = string.Empty;

    /// <summary>资源类型：plugin（插件，仅服务端）或 mod（模组，会下发到客户端）。</summary>
    public string Kind { get; set; } = ScforgeCatalog.PluginKind;

    [Required]
    [MaxLength(ScforgeCatalog.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(ScforgeCatalog.MaxSummaryLength)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>详细描述（长文本）。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>README（Markdown 原文，前端按白名单语法渲染）。</summary>
    public string Readme { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string Category { get; set; } = "misc";

    /// <summary>标签键列表，以 JSON 列存储。</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// 标签的管线化副本（形如 <c>|survival|pvp|</c>）。
    ///
    /// Tags 存成 JSON 列后无法在 SQL 里做成员判断，按标签筛选会退化成全表读进内存；
    /// 这里同步维护一份可用于 LIKE 的副本，代价是一次写入、换来可下推的过滤条件。
    /// 取值来自受控目录（见 ScforgeCatalog.Tags），因此不存在通配符注入。
    /// </summary>
    [MaxLength(256)]
    public string TagsText { get; set; } = string.Empty;

    /// <summary>兼容游戏版本的管线化副本（形如 <c>|2.4|2.3|</c>），由所有版本聚合而来。</summary>
    [MaxLength(128)]
    public string GameVersionsText { get; set; } = string.Empty;

    /// <summary>插件的主游戏版本。</summary>
    [Required]
    [MaxLength(16)]
    public string GameVersion { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? IconUrl { get; set; }

    /// <summary>截图 URL 列表，以 JSON 列存储。</summary>
    public List<string> Gallery { get; set; } = [];

    [MaxLength(512)]
    public string? SourceUrl { get; set; }

    [MaxLength(512)]
    public string? IssuesUrl { get; set; }

    [MaxLength(80)]
    public string? License { get; set; }

    [MaxLength(512)]
    public string? LicenseUrl { get; set; }

    [MaxLength(512)]
    public string? DonationUrl { get; set; }

    [MaxLength(512)]
    public string? DiscordUrl { get; set; }

    /// <summary>作者的本地用户 Id（Identity 域 Users.Id 的字符串形式）。</summary>
    [Required]
    [MaxLength(64)]
    public string AuthorId { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string AuthorName { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? AuthorAvatar { get; set; }

    /// <summary>累计下载次数（版本下载之和的口径由服务端维护）。</summary>
    public int Downloads { get; set; }

    /// <summary>赞同票数：由 scforge_votes 权威重算，列表排序直接读本列。</summary>
    public int Upvotes { get; set; }

    /// <summary>反对票数：同上。</summary>
    public int Downvotes { get; set; }

    /// <summary>评论条数（含回复）。</summary>
    public int CommentCount { get; set; }

    /// <summary>见 <see cref="ScforgeContentStatus"/>：先审后发，默认待审核。</summary>
    public int Status { get; set; } = ScforgeContentStatus.Pending;

    /// <summary>审核意见（驳回理由或通过备注），面向作者展示。</summary>
    [MaxLength(500)]
    public string? ReviewNote { get; set; }

    public DateTime? ReviewedAt { get; set; }

    /// <summary>审核人用户名快照。</summary>
    [MaxLength(80)]
    public string? ReviewedBy { get; set; }

    /// <summary>
    /// 访问模式：public / password / whitelist，见 <see cref="ScforgeAccessMode"/>。
    /// 存量数据靠 EF 的默认值回填成 public，行为与引入隐私前完全一致。
    /// </summary>
    [Required]
    [MaxLength(16)]
    public string AccessMode { get; set; } = ScforgeAccessMode.Public;

    /// <summary>
    /// 口令哈希（仅 password 模式）。**只存 PBKDF2 派生值**，明文永不落库、
    /// 任何接口都取不回来。切走 password 模式时应用层会把它置空。
    /// </summary>
    [MaxLength(160)]
    public string? AccessPasswordHash { get; set; }

    /// <summary>作者设置的访问说明（口令模式下可写「问某某要口令」），仅授权者可见。</summary>
    [MaxLength(200)]
    public string? AccessHint { get; set; }

    /// <summary>平台精选标记。</summary>
    public bool Featured { get; set; }

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ScforgeVersion> Versions { get; set; } = [];

    /// <summary>白名单模式的授权名单（仅该模式下有值）。</summary>
    public List<ScforgeAccessGrant> AccessGrants { get; set; } = [];

    /// <summary>净评分：赞同减反对。</summary>
    public int Score => Upvotes - Downvotes;

    /// <summary>是否已通过审核（对公众可见）。</summary>
    public bool IsPublished => Status == ScforgeContentStatus.Published;

    /// <summary>
    /// 是否会出现在公开目录（搜索 / 精选 / 最近 / 分类计数）。
    /// 隐私插件一律不出现 —— 挡住了「无意间被人翻到」，代价是没有曝光量。
    /// </summary>
    public bool IsPubliclyListed => AccessMode == ScforgeAccessMode.Public;

    /// <summary>是否需要口令解锁（决定详情页要不要渲染解锁门）。</summary>
    public bool RequiresPassword => AccessMode == ScforgeAccessMode.Password;
}
