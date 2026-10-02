using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Mhop;

/// <summary>匿名倾诉论坛主题帖。对应 Python 后端的 posts 表。</summary>
[Table("mhop_posts")]
public class MhopPost
{
    [Key]
    public int Id { get; set; }

    /// <summary>NULL = 纯匿名（历史数据）；匿名仅对前台脱敏，后台仍可追责。</summary>
    public int? UserId { get; set; }

    public bool IsAnonymous { get; set; } = true;

    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>板块 slug，见 MhopBoards。</summary>
    [Required]
    [MaxLength(16)]
    public string Board { get; set; } = "mood";

    /// <summary>JSON 数组：帖子附图 URL 列表。</summary>
    public string Images { get; set; } = string.Empty;

    /// <summary>内容状态，见 <see cref="ContentStatus"/>。</summary>
    public int Status { get; set; }

    /// <summary>内容含自伤/自杀信号。</summary>
    public bool Crisis { get; set; }

    public int ViewCount { get; set; }

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MhopReply> Replies { get; set; } = new List<MhopReply>();

    // ---------------- 领域行为 ----------------

    /// <summary>允许作者编辑正文的状态：待审核 / 草稿。</summary>
    public bool IsEditable => Status is ContentStatus.Pending or ContentStatus.Draft;

    /// <summary>允许作者取消审核的状态。</summary>
    public bool CanWithdraw => Status == ContentStatus.Pending;

    /// <summary>允许作者重新提交审核的状态。</summary>
    public bool CanSubmit => Status == ContentStatus.Draft;

    /// <summary>新建作者帖：进入待审核，由 AI 自动审核异步放行或转人工。</summary>
    public static MhopPost NewAuthorPost(
        int userId,
        bool isAnonymous,
        ContentText content,
        BoardSlug board,
        IReadOnlyCollection<string> imageUrls,
        bool crisis)
        => new()
        {
            // 匿名仅对前台脱敏；user_id 始终留存，供后台审核与追责
            UserId = userId,
            IsAnonymous = isAnonymous,
            Content = content.Value,
            Board = board.Value,
            Images = MhopImageRefs.Serialize(imageUrls),
            Status = ContentStatus.Pending,
            Crisis = crisis,
            CreatedAt = DateTime.UtcNow,
        };

    /// <summary>
    /// 作者编辑正文：仅待审核 / 草稿可改，写完清空上一轮审核结论。
    /// 返回本次编辑后不再被引用的图片地址，供调用方在其后清理存储对象。
    /// </summary>
    public IReadOnlyList<string> ApplyAuthorEdit(
        string? rawContent,
        string? rawBoard,
        bool isAnonymous,
        IReadOnlyCollection<string> imageUrls,
        ContentScreening screening)
    {
        // 校验顺序与历史行为一致：状态 → 正文 → 板块
        if (!IsEditable) throw new DomainRuleException("已通过审核的内容不可修改，仅可删除");
        var content = ContentText.ForPost(rawContent);
        var board = BoardSlug.Create(rawBoard);

        var removed = MhopImageRefs.Parse(Images).Except(imageUrls, StringComparer.Ordinal).ToList();
        Content = content.Value;
        Board = board.Value;
        IsAnonymous = isAnonymous;
        Images = MhopImageRefs.Serialize(imageUrls);
        Crisis = screening.Crisis;
        ClearReviewState();
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
        if (string.IsNullOrWhiteSpace(Content)) throw new DomainRuleException("内容不能为空");
        ClearReviewState();
        Status = ContentStatus.Pending;
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
    }

    /// <summary>命中敏感词的系统拦截：直接驳回并写入拦截理由。</summary>
    public void RejectBySensitiveWords(IReadOnlyCollection<string> words)
        => Reject($"系统拦截：命中敏感词 {string.Join(",", words)}");

    /// <summary>清空上一轮审核结论（编辑 / 重新提交后需要重新审核）。</summary>
    public void ClearReviewState()
    {
        ReviewNote = string.Empty;
        AiFlag = string.Empty;
        AiReviewNote = string.Empty;
        AiReviewedAt = null;
    }

    /// <summary>浏览计数 +1。</summary>
    public void AddView() => ViewCount += 1;
}
