using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Qisoul
{
    public class Post
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(10000)]
        public string Content { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty; // 心理调节、生活习惯等

        [MaxLength(50)]
        public string? Icon { get; set; }

        public int Likes { get; set; } = 0;

        public int Comments { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual User? User { get; set; }
    }
}
