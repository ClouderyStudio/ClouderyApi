namespace ClouderyApi.Models.Qisoul.DTOs
{
    public class StatsResponseDto
    {
        public int TotalDays { get; set; }
        public int TotalRecords { get; set; }
        public string? TodayMood { get; set; }
        public int Streak { get; set; }
        public List<MoodTrendDto>? Trends { get; set; }
        public List<MoodDistributionDto>? Distribution { get; set; }
    }

    public class MoodTrendDto
    {
        public string Date { get; set; } = string.Empty;
        public double AvgIntensity { get; set; }
        public string? MoodType { get; set; }
    }

    public class MoodDistributionDto
    {
        public string MoodType { get; set; } = string.Empty;
        public string? MoodLabel { get; set; }
        public int Count { get; set; }
    }
}
