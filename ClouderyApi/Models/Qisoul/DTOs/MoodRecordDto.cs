namespace ClouderyApi.Models.Qisoul.DTOs
{
    public class MoodRecordDto
    {
        public Guid? Id { get; set; }
        public string MoodType { get; set; } = string.Empty;
        public string? MoodLabel { get; set; }
        public int Intensity { get; set; } = 3;
        public string? Note { get; set; }
        public string? Diary { get; set; }
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
