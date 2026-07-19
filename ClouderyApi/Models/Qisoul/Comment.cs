using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Qisoul;

public class Comment
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid PostId { get; set; }  // 所属帖子

    [Required]
    public Guid UserId { get; set; }  // 评论者

    public Guid? ParentId { get; set; }  // 父评论ID（支持嵌套回复）

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    public int Likes { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(PostId))]
    public virtual Post? Post { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(ParentId))]
    public virtual Comment? Parent { get; set; }

    [InverseProperty(nameof(Parent))]
    public virtual ICollection<Comment> Replies { get; set; } = new List<Comment>();
}