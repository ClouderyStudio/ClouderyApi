using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Mhop;

/// <summary>漂流瓶匿名对话中的单条文字消息。发送人身份仅存 Id，前台永远不暴露。</summary>
[Table("mhop_bottle_messages")]
public class MhopBottleMessage
{
    [Key]
    public int Id { get; set; }

    public int BottleId { get; set; }

    /// <summary>发送者用户 Id（等于瓶子的扔瓶人或捞瓶人）。</summary>
    public int SenderUserId { get; set; }

    /// <summary>消息文字（1-1000 字，长度由 DTO 校验）。</summary>
    [Required]
    [MaxLength(1000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>1=正常；2=违规被审核隐藏（前台不展示）。</summary>
    public int Status { get; set; } = 1;

    public bool Crisis { get; set; }

    /// <summary>AI 初筛风险标记："" 无 / suspect 疑似 / violation 违规。</summary>
    [Required]
    [MaxLength(16)]
    public string AiFlag { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public MhopBottle? Bottle { get; set; }
}
