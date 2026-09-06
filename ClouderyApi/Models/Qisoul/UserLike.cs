using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Qisoul;

/// <summary>
/// 点赞去重表：同一用户对同一目标（帖子/评论/便签）仅保留一条记录，
/// 由 (UserId, TargetType, TargetId) 唯一索引约束，点赞接口据此实现幂等切换。
/// </summary>
public class UserLike
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    /// <summary>目标类型：post / comment / sticky。</summary>
    [Required]
    [MaxLength(20)]
    public string TargetType { get; set; } = string.Empty;

    [Required]
    public Guid TargetId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }
}
