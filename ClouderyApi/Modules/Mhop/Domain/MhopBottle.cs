using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

using ClouderyApi.Modules.Mhop.Domain.Events;
using ClouderyApi.Shared.Domain;
using ClouderyApi.Shared.Exceptions;

namespace ClouderyApi.Modules.Mhop.Domain;

/// <summary>漂流瓶状态。</summary>
public static class MhopBottleStatus
{
    /// <summary>待审核：刚扔出，等待 AI 初筛 / 人工放行，不参与随机捞取。</summary>
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
public class MhopBottle : IHasDomainEvents
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

    /// <summary>
    /// AI 初筛风险标记："" 通过/未审 / suspect 疑似 / violation 违规 / unavailable 未定论 /
    /// approved 人工复核后放行。
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

    /// <summary>扔出一个瓶子：初始待审核（先送 AI 初筛，通过后自动入海）。</summary>
    public static MhopBottle Throw(int userId, string content, bool crisis, DateTime now)
    {
        var bottle = new MhopBottle
        {
            UserId = userId,
            Content = content,
            Status = MhopBottleStatus.Pending,
            Crisis = crisis,
            LastMessageAt = now,
            CreatedAt = now,
            ThrowerLastReadAt = now, // 瓶身是自己写的，无未读
        };
        bottle.AddDomainEvent(new BottleThrown(bottle));
        return bottle;
    }

    public bool IsParty(int userId) => UserId == userId || PickerUserId == userId;

    /// <summary>当前视角：扔瓶人 thrower / 捞瓶人 picker。</summary>
    public string RoleOf(int userId) => UserId == userId ? "thrower" : "picker";

    /// <summary>未读数：对方发出的、可见的、晚于本方最后已读时间的消息数。</summary>
    public int UnreadCountFor(int userId)
    {
        var readAt = UserId == userId ? ThrowerLastReadAt : PickerLastReadAt;
        return Messages.Count(m =>
            m.Status == MhopBottleMessageStatus.Visible && m.SenderUserId != userId &&
            (readAt is null || m.CreatedAt > readAt));
    }

    /// <summary>
    /// 被捞起（捞起即视为已读瓶身）。并发「一瓶不被两人捞走」由调用方的
    /// 「Id + Status=Drifting 条件更新」保证，实体方法只负责状态落地。
    /// 领域不变量：扔瓶人永远不能捞起自己扔出的瓶子（双账号等绕过 SQL 过滤时兜底）。
    /// </summary>
    public void Pick(int pickerId, DateTime now)
    {
        if (pickerId == UserId)
            throw new DomainRuleException("不能捞起自己扔出的瓶子");

        Status = MhopBottleStatus.Picked;
        PickerUserId = pickerId;
        PickedAt = now;
        PickerLastReadAt = now;
    }

    /// <summary>任一方主动结束会话。</summary>
    public void End(int byUserId)
    {
        Status = MhopBottleStatus.Ended;
        EndReason = MhopBottleEndReason.Manual;
        EndedByUserId = byUserId;
    }

    /// <summary>有新消息，推进 7 天超时判定基准。</summary>
    public void TouchLastMessage(DateTime now) => LastMessageAt = now;

    /// <summary>举报：同一用户对同一瓶只计一次；返回是否新增了举报。</summary>
    public bool TryReport(int userId, string reason, DateTime now)
    {
        var reporterIds = ParseReportedBy(ReportedBy);
        if (!reporterIds.Add(userId)) return false;

        ReportedBy = JsonSerializer.Serialize(reporterIds);
        ReportedCount = reporterIds.Count;
        LastReportedAt = now;
        ReportReason = reason;
        return true;
    }

    /// <summary>违规下架（审核处置）。</summary>
    public void MarkRemoved() => Status = MhopBottleStatus.Removed;

    /// <summary>
    /// 人工恢复：未捞过 → 回到海中；已建立对话且未结束 → 回到对话中；已结束 → 保持结束态。
    /// 恢复后清空 AI 风险标记，避免重跑 AI 再次拦下。
    /// </summary>
    public void Restore()
    {
        Status = PickerUserId is null
            ? MhopBottleStatus.Drifting
            : EndReason is null ? MhopBottleStatus.Picked : Status;
        if (Status != MhopBottleStatus.Removed) ClearAiFlag();
    }

    /// <summary>人工放行待审核的瓶子：放入海中，并留下人工结论。</summary>
    public void Approve(DateTime now)
    {
        if (Status != MhopBottleStatus.Pending)
            throw new DomainRuleException("该瓶子不在待审核状态");

        Status = MhopBottleStatus.Drifting;
        AiFlag = MhopModerationOutcome.Approved;
        AiReviewedAt = now;
    }

    /// <summary>清空 AI 风险标记（不动人工处置理由 ReviewNote）。</summary>
    public void ClearAiFlag()
    {
        AiFlag = MhopModerationOutcome.None;
        AiReviewNote = string.Empty;
    }

    /// <summary>记录人工处置理由；空白忽略。</summary>
    public void SetReviewNote(string? note)
    {
        if (!string.IsNullOrWhiteSpace(note)) ReviewNote = note.Trim();
    }

    private static HashSet<int> ParseReportedBy(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json ?? "[]")?.ToHashSet() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
