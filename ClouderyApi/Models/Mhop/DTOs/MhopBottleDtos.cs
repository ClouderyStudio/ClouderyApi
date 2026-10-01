using System.Text.Json.Serialization;

namespace ClouderyApi.Models.Mhop.DTOs;

// ---------------- 入参 ----------------

public class ThrowIn
{
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
}

public class SendIn
{
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
}

public class ReportIn
{
    [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
}

public class BottleAdminActionIn
{
    [JsonPropertyName("note")] public string Note { get; set; } = string.Empty;
}

// ---------------- 前台出参（严格匿名，不含任何对方身份字段） ----------------

/// <summary>会话中的一条消息；接收方只能知道是不是自己发的。</summary>
public class BottleMessageOut
{
    public int Id { get; set; }

    /// <summary>是否为当前请求者本人发送（用于气泡左右分列）。</summary>
    public bool Mine { get; set; }

    public string Content { get; set; } = string.Empty;

    public int Status { get; set; }

    public bool Crisis { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>漂流瓶会话详情（仅会话双方可取）。</summary>
public class BottleDetailOut
{
    public int Id { get; set; }

    /// <summary>瓶身原始文字（对捞瓶人即首条内容）。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>见 MhopBottleStatus：1 漂流中 / 2 对话中 / 3 已结束 / 4 已下架。</summary>
    public int Status { get; set; }

    public bool Crisis { get; set; }

    /// <summary>当前请求者在会话中的角色：thrower 扔瓶人 / picker 捞瓶人。</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>当前请求者的未读消息数（拉取详情后清零）。</summary>
    public int Unread { get; set; }

    /// <summary>结束原因：0 主动 / 1 超时；null 未结束。</summary>
    public int? EndReason { get; set; }

    /// <summary>是否由当前请求者主动结束（用于文案区分）。</summary>
    public bool EndedByMe { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? PickedAt { get; set; }

    public DateTime LastMessageAt { get; set; }

    public List<BottleMessageOut> Messages { get; set; } = [];
}

/// <summary>「我的瓶子」列表行。</summary>
public class BottleSummaryOut
{
    public int Id { get; set; }

    /// <summary>瓶身内容预览（截断）。</summary>
    public string Preview { get; set; } = string.Empty;

    public int Status { get; set; }

    public bool Crisis { get; set; }

    /// <summary>当前请求者的角色：thrower / picker。</summary>
    public string Role { get; set; } = string.Empty;

    public int Unread { get; set; }

    public int MessageCount { get; set; }

    /// <summary>最后一条消息预览（隐藏消息不计）。</summary>
    public string LastMessage { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime LastMessageAt { get; set; }

    public int? EndReason { get; set; }

    /// <summary>AI 初筛标记（仅「我的瓶子」返回，用于展示审核中 / 未通过）。</summary>
    public string AiFlag { get; set; } = string.Empty;

    /// <summary>AI 初筛理由（仅「我的瓶子」返回）。</summary>
    public string AiReviewNote { get; set; } = string.Empty;
}

public class BottleMineOut
{
    public List<BottleSummaryOut> Items { get; set; } = [];

    /// <summary>今日已投瓶数。</summary>
    public int ThrownToday { get; set; }

    /// <summary>今日已捞瓶数。</summary>
    public int PickedToday { get; set; }

    /// <summary>每日投瓶上限。</summary>
    public int ThrowLimit { get; set; }

    /// <summary>每日捞瓶上限。</summary>
    public int PickLimit { get; set; }

    /// <summary>当前海中可捞瓶数。</summary>
    public int SeaCount { get; set; }
}

// ---------------- 后台出参（含真实身份，仅 bottles 权限） ----------------

public class AdminBottleMessageOut
{
    public int Id { get; set; }

    /// <summary>所属瓶子 Id（消息审核队列里用于跳转查看上下文）。</summary>
    public int BottleId { get; set; }

    public int SenderId { get; set; }

    /// <summary>发送者真实用户名（后台追责用，前台接口绝不下发）。</summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>发送者角色：thrower / picker。</summary>
    public string SenderRole { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int Status { get; set; }

    public bool Crisis { get; set; }

    public string AiFlag { get; set; } = string.Empty;

    /// <summary>AI 初筛理由。</summary>
    public string AiReviewNote { get; set; } = string.Empty;

    /// <summary>AI 初筛完成时间；null 表示仍在审核中。</summary>
    public DateTime? AiReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>消息审核队列（跨瓶子）。</summary>
public class AdminBottleMessageListOut
{
    public int Total { get; set; }

    public int Page { get; set; }

    public int Size { get; set; }

    public List<AdminBottleMessageOut> Items { get; set; } = [];
}

public class AdminBottleOut
{
    public int Id { get; set; }

    public string Content { get; set; } = string.Empty;

    public int Status { get; set; }

    public bool Crisis { get; set; }

    public string AiFlag { get; set; } = string.Empty;

    /// <summary>AI 初筛给人工审核的理由。</summary>
    public string AiReviewNote { get; set; } = string.Empty;

    /// <summary>AI 初筛完成时间；null 表示仍在审核中。</summary>
    public DateTime? AiReviewedAt { get; set; }

    public string ReviewNote { get; set; } = string.Empty;

    public int ReportedCount { get; set; }

    public DateTime? LastReportedAt { get; set; }

    /// <summary>最近一次举报理由。</summary>
    public string ReportReason { get; set; } = string.Empty;

    public int ThrowerId { get; set; }

    public string ThrowerName { get; set; } = string.Empty;

    public int? PickerId { get; set; }

    public string PickerName { get; set; } = string.Empty;

    public int MessageCount { get; set; }

    public int? EndReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? PickedAt { get; set; }

    public DateTime LastMessageAt { get; set; }
}

public class AdminBottleDetailOut : AdminBottleOut
{
    public List<AdminBottleMessageOut> Messages { get; set; } = [];
}

public class AdminBottleListOut
{
    public int Total { get; set; }

    public int Page { get; set; }

    public int Size { get; set; }

    public List<AdminBottleOut> Items { get; set; } = [];
}

public class AdminBottleStatsOut
{
    /// <summary>命中危机词、漂流/对话中的瓶子数。</summary>
    public int Crisis { get; set; }

    /// <summary>AI 初筛标记疑似的瓶子与消息数（合并待处置口径）。</summary>
    public int Suspect { get; set; }

    /// <summary>被举报且未下架的瓶子数。</summary>
    public int Reported { get; set; }

    /// <summary>待审核（AI 未通过 / 未完成）的瓶子数。</summary>
    public int Pending { get; set; }

    /// <summary>AI 没给出结论（服务不可用 / 拒答）的待审瓶子数。</summary>
    public int AiUnavailable { get; set; }

    /// <summary>AI 标记疑似/违规且仍然可见的消息数。</summary>
    public int FlaggedMessages { get; set; }

    /// <summary>被隐藏的消息数。</summary>
    public int HiddenMessages { get; set; }
}
