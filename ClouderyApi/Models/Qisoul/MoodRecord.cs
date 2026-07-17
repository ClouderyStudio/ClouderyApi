using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Qisoul
{
    public class MoodRecord
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [Required]
        [MaxLength(20)]
        public string MoodType { get; set; } = string.Empty; // happy, calm, sad, etc.

        [MaxLength(50)]
        public string? MoodLabel { get; set; } // 开心、平静、难过等

        public int Intensity { get; set; } = 3; // 1-5

        [MaxLength(500)]
        public string? Note { get; set; }

        [MaxLength(2000)]
        public string? Diary { get; set; } // 情绪日记

        [MaxLength(500)]
        public string? Tags { get; set; } // JSON 数组或逗号分隔

        public DateTime RecordDate { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(UserId))]
        public virtual User? User { get; set; }
    }
}
