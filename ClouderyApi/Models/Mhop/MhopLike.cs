using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Mhop;

/// <summary>点赞（帖子与回复统一，target_type: post / reply），仅登录用户。对应 Python 后端的 likes 表。</summary>
[Table("mhop_likes")]
public class MhopLike
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [MaxLength(8)]
    public string TargetType { get; set; } = string.Empty;

    public int TargetId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
