using ClouderyApi.Models.Cloudery;
using ClouderyApi.Models.Cloudery.DTOs;

namespace ClouderyApi.UseCases.Cloudery.Mapping;

/// <summary>
/// ExamPaper → 对客视图。公开视图（ExamPaperView）刻意不含 Answer / Note；
/// 管理端视图（ExamPaperFullView）字段顺序与 ExamPaper 实体一致。
/// </summary>
internal static class ExamPaperMapper
{
    /// <summary>公开视图：下发测试端，隐藏答案与解析。</summary>
    public static ExamPaperView ToView(ExamPaper p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Sections = p.Sections.Select(s => new ExamSectionView
        {
            Title = s.Title,
            PointsPerQuestion = s.PointsPerQuestion,
            Questions = s.Questions.Select(q => new ExamQuestionView
            {
                Text = q.Text,
                Options = q.Options,
                Type = q.Type,
            }).ToList(),
        }).ToList(),
    };

    /// <summary>管理端完整视图（含 Answer / Note），替代直接返回实体。</summary>
    public static ExamPaperFullView ToFullView(ExamPaper p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Sections = p.Sections.Select(s => new ExamSectionFullView
        {
            Title = s.Title,
            PointsPerQuestion = s.PointsPerQuestion,
            Questions = s.Questions.Select(q => new ExamQuestionFullView
            {
                Text = q.Text,
                Options = q.Options,
                Answer = q.Answer,
                Note = q.Note,
                Type = q.Type,
            }).ToList(),
        }).ToList(),
        UpdatedAt = p.UpdatedAt,
    };
}
