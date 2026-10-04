using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>插件的一个已发布版本（含插件包文件信息）。</summary>
[Table("scforge_versions")]
public class ScforgeVersion
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PluginId { get; set; }

    [Required]
    [MaxLength(ScforgeCatalog.MaxVersionLength)]
    public string Version { get; set; } = string.Empty;

    /// <summary>release / beta / alpha，见 <see cref="ScforgeCatalog.Channels"/>。</summary>
    [Required]
    [MaxLength(16)]
    public string Channel { get; set; } = "release";

    /// <summary>更新日志（Markdown 原文）。</summary>
    public string Changelog { get; set; } = string.Empty;

    /// <summary>上传时的原始文件名。</summary>
    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>对象存储键（相对上传根目录），绝不直接下发给前端。</summary>
    [Required]
    [MaxLength(512)]
    public string StorageKey { get; set; } = string.Empty;

    public long FileSize { get; set; }

    /// <summary>插件包 SHA-256，用于内容寻址与去重。</summary>
    [MaxLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>主游戏版本。</summary>
    [Required]
    [MaxLength(16)]
    public string GameVersion { get; set; } = string.Empty;

    /// <summary>该版本声明兼容的全部游戏版本，以 JSON 列存储。</summary>
    public List<string> GameVersions { get; set; } = [];

    /// <summary>前置依赖名称，以 JSON 列存储。</summary>
    public List<string> Dependencies { get; set; } = [];

    public int Downloads { get; set; }

    /// <summary>见 <see cref="ScforgeContentStatus"/>：新版本与替换过文件的版本都要重新审核。</summary>
    public int Status { get; set; } = ScforgeContentStatus.Pending;

    [MaxLength(500)]
    public string? ReviewNote { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(80)]
    public string? ReviewedBy { get; set; }

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
}
