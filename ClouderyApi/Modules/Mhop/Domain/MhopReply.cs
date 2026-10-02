using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using ClouderyApi.Modules.Mhop.Domain.Events;
using ClouderyApi.Shared.Domain;
using ClouderyApi.Shared.Exceptions;

namespace ClouderyApi.Modules.Mhop.Domain;

/// <summary>主题帖回复（含 AI 自动回复）。对应 Python 后端的 replies 表。</summary>
[Table("mhop_replies")]
public class MhopReply : IHasDomainEvents
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

    /// <summary>内容状态，见 <see cref="ContentStatus"/>。</summary>
    public int Status { get; set; }

    public bool IsAi { get; set; }

    public bool Crisis { get; set; }

    [MaxLength(255)]
    public string ReviewNote { get; set; } = string.Empty;

    /// <summary>AI 初筛标记："" 通过/未审，suspect 疑似，violation 违规，unavailable 服务不可用。</summary>
    [MaxLength(16)]
    public string AiFlag { get; set; } = string.Empty;

    /// <summary>AI 初筛给人工审核的理由（通过时为空）。</summary>
    [MaxLength(255)]
    public string AiReviewNote { get; set; } = string.Empty;

    /// <summary>AI 初筛完成时间；null 表示尚未完成（仍在审核中）。</summary>
    public DateTime? AiReviewedAt { get; set; }

    /// <summary>管理员撤回（仅 AI 回复）：撤回后公开接口不返回正文，内容保留以备审计。</summary>
    public bool Recalled { get; set; }

    [MaxLength(255)]
    public string RecallReason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public MhopPost? Post { get; set; }

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

    /// <summary>撤销尚未派发的指定类型事件（例如驳回后不再送审）。</summary>
    private void CancelDomainEvent<TEvent>() where TEvent : IDomainEvent
        => _domainEvents.RemoveAll(e => e is TEvent);

    // ---------------- 领域行为 ----------------

    /// <summary>允许作者编辑正文的状态：待审核 / 草稿。</summary>
    public bool IsEditable => Status is ContentStatus.Pending or ContentStatus.Draft;

    /// <summary>允许作者取消审核的状态。</summary>
    public bool CanWithdraw => Status == ContentStatus.Pending;

    /// <summary>允许作者重新提交审核的状态。</summary>
    public bool CanSubmit => Status == ContentStatus.Draft;

    /// <summary>新建人类回复：进入待审核（命中敏感词时由调用方立即驳回）。</summary>
    public static MhopReply NewAuthorReply(
        int postId,
        int userId,
        bool isAnonymous,
        ContentText content,
        IReadOnlyCollection<string> imageUrls,
        bool crisis)
    {
        var reply = new MhopReply
        {
            PostId = postId,
            UserId = userId,
            IsAnonymous = isAnonymous,
            Content = content.Value,
            Images = MhopImageRefs.Serialize(imageUrls),
            Status = ContentStatus.Pending,
            Crisis = crisis,
            CreatedAt = DateTime.UtcNow,
        };
        reply.AddDomainEvent(new ReplySubmittedForReview(reply));
        return reply;
    }

    /// <summary>
    /// 作者编辑回复：仅待审核 / 草稿可改，写完清空 AI 初筛结论。
    /// 待审核的回复会重新做敏感词复核（命中即驳回，未命中则重置人工理由）；草稿保持原状。
    /// 返回本次编辑后不再被引用的图片地址，供调用方清理存储对象。
    /// </summary>
    public IReadOnlyList<string> ApplyAuthorEdit(
        string? rawContent,
        bool isAnonymous,
        IReadOnlyCollection<string> imageUrls,
        ContentScreening screening)
    {
        // 校验顺序与历史行为一致：状态 → 正文
        if (!IsEditable) throw new DomainRuleException("已通过审核的内容不可修改，仅可删除");
        var content = ContentText.ForReply(rawContent);

        var removed = MhopImageRefs.Parse(Images).Except(imageUrls, StringComparer.Ordinal).ToList();
        Content = content.Value;
        IsAnonymous = isAnonymous;
        Images = MhopImageRefs.Serialize(imageUrls);
        Crisis = screening.Crisis;
        ClearAiReviewState();

        if (Status == ContentStatus.Pending)
        {
            if (screening.HasSensitiveWords) RejectBySensitiveWords(screening.SensitiveWords);
            else ReviewNote = string.Empty;
        }
        // 敏感词复核之后：仍待审核才登记（被驳回时 Status 已变，不排队）
        if (Status == ContentStatus.Pending) AddDomainEvent(new ReplySubmittedForReview(this));

        return removed;
    }

    /// <summary>取消审核：待审核 → 草稿（作者自留，仅本人可见）。</summary>
    public void Withdraw()
    {
        if (!CanWithdraw) throw new DomainRuleException("只有审核中的内容可以取消审核");
        Status = ContentStatus.Draft;
    }

    /// <summary>重新提交审核：草稿 → 待审核，并清空上一轮审核结论。</summary>
    public void SubmitForReview()
    {
        if (!CanSubmit) throw new DomainRuleException("只有草稿可以重新提交审核");
        if (string.IsNullOrWhiteSpace(Content)) throw new DomainRuleException("回复内容不能为空");
        ClearReviewState();
        Status = ContentStatus.Pending;
        AddDomainEvent(new ReplySubmittedForReview(this));
    }

    /// <summary>人工放行（AI 自动放行走审核服务的条件更新，不经过实体）。</summary>
    public void Publish(string? note = null)
    {
        Status = ContentStatus.Published;
        ReviewNote = ContentRules.TruncateReviewNote(note);
    }

    /// <summary>人工驳回并记录理由。</summary>
    public void Reject(string? note = null)
    {
        Status = ContentStatus.Rejected;
        ReviewNote = ContentRules.TruncateReviewNote(note);
        // 已驳回 ⇒ 不能再排队送审：撤销尚未派发的提交事件（与历史 if (Status == Pending) 守卫等价）
        CancelDomainEvent<ReplySubmittedForReview>();
    }

    /// <summary>命中敏感词的系统拦截：直接驳回并写入拦截理由。</summary>
    public void RejectBySensitiveWords(IReadOnlyCollection<string> words)
        => Reject($"系统拦截：命中敏感词 {string.Join(",", words)}");

    /// <summary>清空上一轮审核结论（编辑 / 重新提交后需要重新审核）。</summary>
    public void ClearReviewState()
    {
        ReviewNote = string.Empty;
        ClearAiReviewState();
    }

    /// <summary>只清 AI 初筛结论，保留人工审核理由。</summary>
    public void ClearAiReviewState()
    {
        AiFlag = string.Empty;
        AiReviewNote = string.Empty;
        AiReviewedAt = null;
    }

    /// <summary>管理员撤回（仅 AI 回复）：正文立即对所有用户隐藏，内容与原因保留备审。</summary>
    public void Recall(string reason)
    {
        Recalled = true;
        RecallReason = ContentRules.TruncateReviewNote(reason.Trim());
    }

    /// <summary>恢复被撤回的 AI 回复，重新公开展示。</summary>
    public void Restore()
    {
        Recalled = false;
        RecallReason = string.Empty;
    }
}
