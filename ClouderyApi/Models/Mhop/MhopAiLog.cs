using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Mhop;

/// <summary>AI 调用审计日志。对应 Python 后端的 ai_logs 表。</summary>
[Table("mhop_ai_logs")]
public class MhopAiLog
{
    [Key]
    public int Id { get; set; }

    public int? UserId { get; set; }

    [MaxLength(64)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>forum / assessment</summary>
    [Required]
    [MaxLength(32)]
    public string Module { get; set; } = string.Empty;

    /// <summary>forum 模块关联生成的 AI 回复，供后台日志页直接撤回/恢复；历史数据为 NULL。</summary>
    public int? ReplyId { get; set; }

    public string Prompt { get; set; } = string.Empty;

    public string Response { get; set; } = string.Empty;

    /// <summary>llm / local</summary>
    [MaxLength(32)]
    public string Engine { get; set; } = "local";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
