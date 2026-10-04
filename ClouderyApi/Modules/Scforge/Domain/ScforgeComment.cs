using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>插件下的一条评论；<see cref="ParentId"/> 非空表示它是对某条评论的回复。</summary>
[Table("scforge_comments")]
public class ScforgeComment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PluginId { get; set; }

    public Guid? ParentId { get; set; }

    /// <summary>评论正文（Markdown 原文）。</summary>
    [Required]
    public string Body { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string AuthorId { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string AuthorName { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? AuthorAvatar { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public int Upvotes { get; set; }

    public int Downvotes { get; set; }

    /// <summary>净评分。</summary>
    public int Score => Upvotes - Downvotes;
}
