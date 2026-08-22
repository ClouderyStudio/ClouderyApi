using System.ComponentModel.DataAnnotations;

namespace ClouderyApi.Models.Qisoul.DTOs
{
    public class PostDto
    {
        public Guid? Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(10000)]
        public string Content { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Icon { get; set; }
    }

    public class PostResponseDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public int Likes { get; set; }
        public int Comments { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? Username { get; set; }
        public string? UserAvatar { get; set; }
        public int CommentCount { get; set; }
    }
}
