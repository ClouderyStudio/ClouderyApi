using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Mhop;

/// <summary>漂流瓶状态。</summary>
public static class MhopBottleStatus
{
    /// <summary>预留：待处置（当前流程为 AI 异步初筛，瓶子直接进入漂流，此状态暂不使用）。</summary>
    public const int Pending = 0;

    /// <summary>漂流中：等待陌生人捞起。</summary>
    public const int Drifting = 1;

    /// <summary>已捞起：双方匿名对话进行中。</summary>
    public const int Picked = 2;

    /// <summary>已结束：主动结束或 7 天超时，历史只读，不再回到海中。</summary>
    public const int Ended = 3;

    /// <summary>违规下架：审核处置，前台显示「内容违规」。</summary>
    public const int Removed = 4;
}

/// <summary>结束原因：0=任一方主动结束；1=7 天无消息系统超时。</summary>
public static class MhopBottleEndReason
{
    public const int Manual = 0;
    public const int Timeout = 1;
}

/// <summary>
/// 漂流瓶：用户扔出的匿名文字心事，被一名随机用户捞起后形成一对一双匿名对话。
/// 用户关联仅存 Id 并建索引（与 MhopPost 一致，不建外键，避免账号删除级联）。
/// </summary>
[Table("mhop_bottles")]
public class MhopBottle
{
    [Key]
    public int Id { get; set; }

    /// <summary>扔瓶人用户 Id。</summary>
    public int UserId { get; set; }

    /// <summary>瓶身文字（1-500 字，长度由 DTO 校验）。</summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>见 MhopBottleStatus 常量。</summary>
    public int Status { get; set; } = MhopBottleStatus.Drifting;

    /// <summary>内容含自伤/自杀信号，双方界面展示援助热线。</summary>
    public bool Crisis { get; set; }

    /// <summary>AI 初筛风险标记："" 通过/未审 / suspect 疑似 / violation 违规 / unavailable 服务不可用。</summary>
    [Required]
    [MaxLength(16)]
    public string AiFlag { get; set; } = string.Empty;

    /// <summary>AI 初筛给人工审核的理由（通过时为空）。</summary>
    [Required]
    [MaxLength(255)]
    public string AiReviewNote { get; set; } = string.Empty;

    /// <summary>AI 初筛完成时间；null 表示尚未完成（仍在审核中）。</summary>
    public DateTime? AiReviewedAt { get; set; }

    [Required]
    [MaxLength(255)]
    public string ReviewNote { get; set; } = string.Empty;

    /// <summary>捞瓶人用户 Id；NULL 表示仍在漂流。</summary>
    public int? PickerUserId { get; set; }

    public DateTime? PickedAt { get; set; }

    /// <summary>结束操作人（用于区分是哪一方主动结束）；超时为 NULL。</summary>
    public int? EndedByUserId { get; set; }

    /// <summary>见 MhopBottleEndReason；NULL 表示未结束。</summary>
    public int? EndReason { get; set; }

    public int ReportedCount { get; set; }

    public DateTime? LastReportedAt { get; set; }

    /// <summary>举报过该瓶的用户 Id JSON 数组（同一用户对同瓶仅计一次；不另建举报表）。</summary>
    [Required]
    public string ReportedBy { get; set; } = "[]";

    /// <summary>最近一次举报理由（供审核队列查看）。</summary>
    [Required]
    [MaxLength(500)]
    public string ReportReason { get; set; } = string.Empty;

    /// <summary>扔瓶人最后已读时间（未读按对方新消息计算）。</summary>
    public DateTime? ThrowerLastReadAt { get; set; }

    /// <summary>捞瓶人最后已读时间。</summary>
    public DateTime? PickerLastReadAt { get; set; }

    /// <summary>最后一条消息时间，用于 7 天超时判定；投瓶时初始化为 CreatedAt。</summary>
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MhopBottleMessage> Messages { get; set; } = new List<MhopBottleMessage>();
}
