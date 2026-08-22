using System.ComponentModel.DataAnnotations;

namespace ClouderyApi.Models.Qisoul.DTOs
{
    public class MoodRecordDto
    {
        public Guid? Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string MoodType { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? MoodLabel { get; set; }

        [Range(1, 5)]
        public int Intensity { get; set; } = 3;

        [MaxLength(500)]
        public string? Note { get; set; }

        [MaxLength(2000)]
        public string? Diary { get; set; }

        [MaxLength(500)]
        public string? Tags { get; set; }

        public DateTime? RecordDate { get; set; }
    }

    public class MoodRecordResponseDto
    {
        public Guid Id { get; set; }
        public string MoodType { get; set; } = string.Empty;
        public string? MoodLabel { get; set; }
        public int Intensity { get; set; }
        public string? Note { get; set; }
        public string? Diary { get; set; }
        public string? Tags { get; set; }
        public DateTime RecordDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? Username { get; set; }
    }
}
