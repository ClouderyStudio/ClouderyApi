using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Mhop;

/// <summary>主题帖回复（含 AI 自动回复）。对应 Python 后端的 replies 表。</summary>
[Table("mhop_replies")]
public class MhopReply
{
    [Key]
    public int Id { get; set; }

    public int PostId { get; set; }

    public int? UserId { get; set; }

    public bool IsAnonymous { get; set; } = true;

    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>JSON 数组：回复附图 URL 列表。</summary>
    public string Images { get; set; } = string.Empty;

    /// <summary>0=待系统审核 1=审核通过 2=驳回</summary>
    public int Status { get; set; }

    public bool IsAi { get; set; }

    public bool Crisis { get; set; }

    [MaxLength(255)]
    public string ReviewNote { get; set; } = string.Empty;

    /// <summary>管理员撤回（仅 AI 回复）：撤回后公开接口不返回正文，内容保留以备审计。</summary>
    public bool Recalled { get; set; }

    [MaxLength(255)]
    public string RecallReason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public MhopPost? Post { get; set; }
}
