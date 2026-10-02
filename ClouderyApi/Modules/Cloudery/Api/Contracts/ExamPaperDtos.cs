using ClouderyApi.Modules.Cloudery.Domain;

namespace ClouderyApi.Modules.Cloudery.Api.Contracts;

/// <summary>
/// 管理端完整试卷视图（含 Answer / Note）：替代写接口与 /exam/ExamPapers/{id}/full
/// 直接返回 ExamPaper 实体。属性声明顺序必须与 ExamPaper 实体的键顺序一致
/// （id → name → sections → updatedAt）。
/// </summary>
public class ExamPaperFullView
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required List<ExamSectionFullView> Sections { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ExamSectionFullView
{
    public required string Title { get; set; }
    public double? PointsPerQuestion { get; set; }
    public required List<ExamQuestionFullView> Questions { get; set; }
}

public class ExamQuestionFullView
{
    public required string Text { get; set; }
    public List<ExamOption>? Options { get; set; }
    public required string Answer { get; set; }
    public string? Note { get; set; }
    public string? Type { get; set; }
}
