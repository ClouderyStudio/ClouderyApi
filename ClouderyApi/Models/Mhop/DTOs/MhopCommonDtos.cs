using System.Text.Json.Serialization;

namespace ClouderyApi.Models.Mhop.DTOs;

public class HeartbeatIn
{
    /// <summary>匿名访客 ID（前端生成的随机 ID，不含个人信息）。</summary>
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
}

public class HotlineOut
{
    public string Name { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Tag { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;
}
