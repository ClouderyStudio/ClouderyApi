using ClouderyApi.Models.Mhop;
using ClouderyApi.Models.Mhop.DTOs;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 漂流瓶实体 → DTO 映射。
/// 铁律：前台映射只输出「是不是我」，绝不输出对方用户 Id / 用户名；真实身份仅出现在 Admin 映射。
/// </summary>
public static class MhopBottleMapper
{
    public const int BottlePreviewLength = 80;
    public const int MessagePreviewLength = 60;

    public static BottleMessageOut ToMessageOut(MhopBottleMessage m, int viewerId) => new()
    {
        Id = m.Id,
        Mine = m.SenderUserId == viewerId,
        Content = m.Content,
        Status = m.Status,
        Crisis = m.Crisis,
        CreatedAt = m.CreatedAt,
    };

    /// <summary>
    /// 会话详情。隐藏消息（审核 status=2）不下发；afterId 仅返回其之后的消息（增量轮询）。
    /// </summary>
    public static BottleDetailOut ToDetailOut(
        MhopBottle b, int viewerId, int unread, int afterId = 0) => new()
    {
        Id = b.Id,
        Content = b.Content,
        Status = b.Status,
        Crisis = b.Crisis,
        Role = MhopBottleService.RoleOf(b, viewerId),
        Unread = unread,
        EndReason = b.EndReason,
        EndedByMe = b.EndedByUserId is int ender && ender == viewerId,
        CreatedAt = b.CreatedAt,
        PickedAt = b.PickedAt,
        LastMessageAt = b.LastMessageAt,
        Messages = b.Messages
            .Where(m => m.Status == 1 && m.Id > afterId)
            .OrderBy(m => m.Id)
            .Select(m => ToMessageOut(m, viewerId))
            .ToList(),
    };

    public static BottleSummaryOut ToSummaryOut(MhopBottle b, int viewerId)
    {
        var visible = b.Messages.Where(m => m.Status == 1).ToList();
        var last = visible.OrderByDescending(m => m.Id).FirstOrDefault();
        return new BottleSummaryOut
        {
            Id = b.Id,
            Preview = Truncate(b.Content, BottlePreviewLength),
            Status = b.Status,
            Crisis = b.Crisis,
            Role = MhopBottleService.RoleOf(b, viewerId),
            Unread = MhopBottleService.CountUnread(b, viewerId),
            MessageCount = visible.Count,
            // 没有消息时用瓶身做预览
            LastMessage = Truncate(last?.Content ?? b.Content, MessagePreviewLength),
            CreatedAt = b.CreatedAt,
            LastMessageAt = b.LastMessageAt,
            EndReason = b.EndReason,
            AiFlag = b.AiFlag,
            AiReviewNote = b.AiReviewNote,
        };
    }

    public static AdminBottleOut ToAdminOut(MhopBottle b, IReadOnlyDictionary<int, string> names, int? messageCount = null) => new()
    {
        Id = b.Id,
        Content = b.Content,
        Status = b.Status,
        Crisis = b.Crisis,
        AiFlag = b.AiFlag,
        AiReviewNote = b.AiReviewNote,
        AiReviewedAt = b.AiReviewedAt,
        ReviewNote = b.ReviewNote,
        ReportedCount = b.ReportedCount,
        LastReportedAt = b.LastReportedAt,
        ReportReason = b.ReportReason,
        ThrowerId = b.UserId,
        ThrowerName = names.GetValueOrDefault(b.UserId, $"用户{b.UserId}"),
        PickerId = b.PickerUserId,
        PickerName = b.PickerUserId is int pid ? names.GetValueOrDefault(pid, $"用户{pid}") : string.Empty,
        MessageCount = messageCount
            ?? b.Messages?.Count(m => m.Status == 1)
            ?? 0,
        EndReason = b.EndReason,
        CreatedAt = b.CreatedAt,
        PickedAt = b.PickedAt,
        LastMessageAt = b.LastMessageAt,
    };

    public static AdminBottleMessageOut ToAdminMessageOut(MhopBottleMessage m, MhopBottle b, IReadOnlyDictionary<int, string> names) => new()
    {
        Id = m.Id,
        SenderId = m.SenderUserId,
        SenderName = names.GetValueOrDefault(m.SenderUserId, $"用户{m.SenderUserId}"),
        SenderRole = b.UserId == m.SenderUserId ? "thrower" : "picker",
        Content = m.Content,
        Status = m.Status,
        Crisis = m.Crisis,
        AiFlag = m.AiFlag,
        CreatedAt = m.CreatedAt,
    };

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) ? string.Empty
        : value.Length <= max ? value
        : value[..max] + "…";
}
