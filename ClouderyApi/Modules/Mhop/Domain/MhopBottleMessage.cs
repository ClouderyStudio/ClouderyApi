using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using ClouderyApi.Modules.Mhop.Domain.Events;
using ClouderyApi.Shared.Domain;

namespace ClouderyApi.Modules.Mhop.Domain;

/// <summary>漂流瓶消息可见性。</summary>
public static class MhopBottleMessageStatus
{
    /// <summary>正常：前台可见。</summary>
    public const int Visible = 1;

    /// <summary>违规被审核隐藏，前台不展示（AI 自动隐藏或人工隐藏）。</summary>
    public const int Hidden = 2;
}

/// <summary>漂流瓶匿名对话中的单条文字消息。发送人身份仅存 Id，前台永远不暴露。</summary>
[Table("mhop_bottle_messages")]
public class MhopBottleMessage : IHasDomainEvents
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

    /// <summary>见 MhopBottleMessageStatus。</summary>
    public int Status { get; set; } = MhopBottleMessageStatus.Visible;

    public bool Crisis { get; set; }

    /// <summary>
    /// AI 初筛风险标记："" 通过/未审 / suspect 疑似 / violation 违规 / unavailable 未定论 /
    /// approved 人工复核后放行（AI 重跑不再翻案）。
    /// </summary>
    [Required]
    [MaxLength(16)]
    public string AiFlag { get; set; } = string.Empty;

    /// <summary>AI 初筛给人工审核的理由（通过时为空）。</summary>
    [Required]
    [MaxLength(255)]
    public string AiReviewNote { get; set; } = string.Empty;

    /// <summary>AI 初筛完成时间；null 表示尚未完成（仍在审核中）。</summary>
    public DateTime? AiReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public MhopBottle? Bottle { get; set; }

    // ---------------- 领域事件承载 ----------------
    // 用接口 + [NotMapped] 集合而非实体基类：避免 EF 把基类纳入类型层级、要求主键或引入判别列。

    private readonly List<IDomainEvent> _domainEvents = new();

    /// <summary>尚未派发的领域事件；[NotMapped] 保证 EF 不把它当列。</summary>
    [NotMapped]
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <inheritdoc />
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>供实体行为登记领域事件。</summary>
    private void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    // ---------------- 领域行为 ----------------

    /// <summary>新建一条消息（初始可见、未审核）。</summary>
    public static MhopBottleMessage Create(int bottleId, int senderUserId, string content, bool crisis, DateTime now)
    {
        var message = new MhopBottleMessage
        {
            BottleId = bottleId,
            SenderUserId = senderUserId,
            Content = content,
            Status = MhopBottleMessageStatus.Visible,
            Crisis = crisis,
            CreatedAt = now,
        };
        message.AddDomainEvent(new BottleMessageSent(message));
        return message;
    }

    /// <summary>隐藏：AI 自动隐藏或人工隐藏，前台不再下发。</summary>
    public void Hide() => Status = MhopBottleMessageStatus.Hidden;

    /// <summary>人工放行：恢复可见并打上 approved，AI 重跑不会再自动隐藏。</summary>
    public void Show(DateTime now)
    {
        Status = MhopBottleMessageStatus.Visible;
        AiFlag = MhopModerationOutcome.Approved;
        AiReviewedAt ??= now;
    }
}
