namespace ClouderyApi.Models.Qisoul.DTOs
{
    public class StickyDto
    {
        public Guid? Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Color { get; set; }
    }

    public class StickyResponseDto
    {
        public Guid Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Color { get; set; }
        public int Likes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? Username { get; set; }
        public string? UserAvatar { get; set; }
    }
}
