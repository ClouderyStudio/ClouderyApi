namespace ClouderyApi.Models
{
    public enum FileType
    {
        World,
        Mod,
        Datapacks,
        Pack
    }
    public class ZhuxsFile
    {
        public required string Id { get; set; }
        public required string Term { get; set; }
        public required FileType Type { get; set; }
        public required string Url { get; set; }
    }
}
