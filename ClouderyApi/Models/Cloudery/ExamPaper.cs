using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Cloudery;

/// <summary>
/// 内部测试试卷（心理学项目后台管理）——试卷以 JSON 存单列，便于整卷增删改。
/// </summary>
public class ExamOption
{
    public required string Label { get; set; }
    public required string Text { get; set; }
}

public class ExamQuestion
{
    public required string Text { get; set; }
    public List<ExamOption>? Options { get; set; }
    public required string Answer { get; set; }
    public string? Note { get; set; }
    /// <summary>judge / single / multiple / essay（缺省按有无 Options 推断）</summary>
    public string? Type { get; set; }
}

public class ExamSection
{
    public required string Title { get; set; }
    /// <summary>每题分值，缺省 1 分</summary>
    public double? PointsPerQuestion { get; set; }
    public required List<ExamQuestion> Questions { get; set; }
}

public class ExamPaper
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required List<ExamSection> Sections { get; set; }
    public DateTime UpdatedAt { get; set; }
}