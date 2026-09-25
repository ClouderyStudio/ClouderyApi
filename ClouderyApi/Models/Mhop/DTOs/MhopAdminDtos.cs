using System.Text.Json.Serialization;

namespace ClouderyApi.Models.Mhop.DTOs;

public class ModerateIn
{
    /// <summary>approve / reject(remove)</summary>
    [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;

    [JsonPropertyName("note")] public string Note { get; set; } = string.Empty;
}

public class RecallIn
{
    /// <summary>撤回原因（必填，留审计痕迹）。</summary>
    [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
}

public class StatusIn
{
    /// <summary>active / disabled</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
}

public class BadgeIn
{
    [JsonPropertyName("badge")] public string? Badge { get; set; }
}

public class RoleIn
{
    /// <summary>promote / demote</summary>
    [JsonPropertyName("action")] public string? Action { get; set; }
}

public class ResetPasswordIn
{
    [JsonPropertyName("password")] public string? Password { get; set; }
}
