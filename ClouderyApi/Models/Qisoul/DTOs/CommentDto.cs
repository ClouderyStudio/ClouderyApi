using System.ComponentModel.DataAnnotations;

namespace ClouderyApi.Models.Qisoul.DTOs;

public class CommentDto
{
    [Required]
    public Guid PostId { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }
}

public class CommentResponseDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public int Likes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? Username { get; set; }
    public string? UserAvatar { get; set; }
    public Guid? ParentId { get; set; }
    public List<CommentResponseDto>? Replies { get; set; }
    public int ReplyCount { get; set; }
}