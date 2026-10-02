using System.Text.Encodings.Web;
using System.Text.Json;
using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Services.Mhop;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.UseCases.Mhop;

/// <summary>
/// 心理评估用例编排：量表目录、作答计分 → AI 解读 → 可选云端保存 / 历史查询。
/// 控制器只负责 HTTP 绑定与响应包装。
/// </summary>
public sealed class AssessmentAppService
{
    private static readonly JsonSerializerOptions InputJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly MhopDbContext _db;
    private readonly MhopCurrentUserAccessor _current;
    private readonly MhopAiService _ai;

    public AssessmentAppService(MhopDbContext db, MhopCurrentUserAccessor current, MhopAiService ai)
    {
        _db = db;
        _current = current;
        _ai = ai;
    }

    /// <summary>量表目录（Python 后端 /api/assessments/scales 的返回结构）。</summary>
    public IReadOnlyList<object> Scales()
        => MhopScales.All.Values.Select(scale => scale.ToResponse()).ToList();

    /// <summary>提交一次评估：服务端不落库，仅当 save_to_cloud 且已登录时写入云端。</summary>
    public async Task<AssessmentOut> SubmitAsync(AssessmentIn body, CancellationToken cancellationToken = default)
    {
        var assessmentType = body.AssessmentType ?? string.Empty;
        var freeText = (body.FreeText ?? string.Empty).Trim();
        var crisis = MhopModeration.DetectCrisis(freeText);
        var scoring = AssessmentScoring.Evaluate(assessmentType, body.Answers, freeText, crisis);

        var (result, _) = await _ai.AssessAsync(assessmentType, scoring.Score, scoring.Level, freeText, scoring.Crisis);

        var saved = false;
        int? recordId = null;
        DateTime? createdAt = null;
        if (body.SaveToCloud)
        {
            var current = await _current.GetOptionalAsync()
                ?? throw new MhopApiException(401, "登录后才能保存到云端");
            var record = new MhopAssessment
            {
                UserId = current.Id,
                AssessmentType = assessmentType,
                InputData = JsonSerializer.Serialize(
                    new { answers = body.Answers, free_text = body.FreeText }, InputJsonOptions),
                AiResult = result,
                Score = scoring.Score,
                Level = scoring.Level,
                CreatedAt = DateTime.UtcNow,
            };
            _db.MhopAssessments.Add(record);
            await _db.SaveChangesAsync(cancellationToken);
            saved = true;
            recordId = record.Id;
            createdAt = record.CreatedAt;
        }

        return new AssessmentOut
        {
            Id = recordId,
            AssessmentType = assessmentType,
            Score = scoring.Score,
            Level = scoring.Level,
            LevelCode = scoring.LevelCode,
            Crisis = scoring.Crisis,
            AiResult = result,
            SavedCloud = saved,
            CreatedAt = createdAt,
        };
    }

    /// <summary>当前登录用户的云端评估记录（新到旧）。</summary>
    public async Task<IReadOnlyList<AssessmentOut>> MineAsync(CancellationToken cancellationToken = default)
    {
        var current = await _current.RequireAsync();
        var rows = await _db.MhopAssessments
            .Where(record => record.UserId == current.Id)
            .OrderByDescending(record => record.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(record => new AssessmentOut
        {
            Id = record.Id,
            AssessmentType = record.AssessmentType,
            Score = record.Score,
            Level = record.Level ?? string.Empty,
            Crisis = false,
            AiResult = record.AiResult,
            SavedCloud = true,
            CreatedAt = record.CreatedAt,
        }).ToList();
    }
}
