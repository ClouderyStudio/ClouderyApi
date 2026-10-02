using System.Text.Json;

namespace ClouderyApi.Modules.Cloudery.Api.Contracts;

/// <summary>站点上传的一条结果（同步用）。不放 DataAnnotations：Cloudery 模块的 400 一律是 { success, message } 裸对象。</summary>
public class ExamResultIn
{
    /// <summary>站点本机记录键，缺失时按 testId + savedAt 生成，保证同步幂等</summary>
    public string? ClientKey { get; set; }

    public string TestId { get; set; } = string.Empty;

    public string? TestTitle { get; set; }

    /// <summary>结果在本机存档的时间</summary>
    public DateTime? SavedAt { get; set; }

    /// <summary>结果正文（站点存档对象的原样 JSON）</summary>
    public JsonElement? Payload { get; set; }
}

public class ExamResultSyncIn
{
    public List<ExamResultIn>? Records { get; set; }
}

public class ExamResultOut
{
    public string Id { get; set; } = string.Empty;

    public string? ClientKey { get; set; }

    public string TestId { get; set; } = string.Empty;

    public string? TestTitle { get; set; }

    public DateTime? SavedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public JsonElement? Payload { get; set; }
}

/// <summary>同步结果：上传条数 + 该用户在云端的全部记录（客户端据此补全本机存档）</summary>
public class ExamResultSyncOut
{
    public bool Success { get; set; } = true;

    public int Uploaded { get; set; }

    public int Total { get; set; }

    public List<ExamResultOut> Results { get; set; } = new();
}
