using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace ClouderyApi.Models.Qisoul
{
    public class User
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Email { get; set; }

        [MaxLength(500)]
        public string? Avatar { get; set; }

        [Required]
        [MaxLength(100)]
        public string CasdoorId { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastLoginAt { get; set; }

        // 导航属性
        public ICollection<MoodRecord> MoodRecords { get; set; } = new List<MoodRecord>();
        public ICollection<Post> Posts { get; set; } = new List<Post>();
        public ICollection<Sticky> Stickies { get; set; } = new List<Sticky>();
    }
}
