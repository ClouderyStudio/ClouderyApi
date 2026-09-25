using System.Text.Encodings.Web;
using System.Text.Json;
using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>
/// AI 心理评估：标准量表计分 + AI 解读（对应 Python 后端 routers/assessment.py）。
/// 匿名用户服务端不做任何持久化（隐私优先）；登录用户仅在显式勾选 save_to_cloud 时写入云端。
/// </summary>
[ApiController]
[Route("mhop/assessments")]
public class MhopAssessmentController : MhopControllerBase
{
    private static readonly JsonSerializerOptions InputJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly MhopDbContext _db;
    private readonly MhopCurrentUserAccessor _current;
    private readonly MhopAiService _ai;

    public MhopAssessmentController(MhopDbContext db, MhopCurrentUserAccessor current, MhopAiService ai)
    {
        _db = db;
        _current = current;
        _ai = ai;
    }

    [HttpGet("scales")]
    public IActionResult GetScales() => MhopOk(MhopScales.All.Values.Select(scale => scale.ToResponse()));

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] AssessmentIn body)
    {
        var assessmentType = body.AssessmentType ?? string.Empty;
        var freeText = (body.FreeText ?? string.Empty).Trim();
        int? score = null;
        var level = string.Empty;
        var levelCode = string.Empty;
        var crisis = MhopModeration.DetectCrisis(freeText);

        if (MhopScales.All.TryGetValue(assessmentType, out var scale))
        {
            var answers = body.Answers ?? new Dictionary<string, int>();
            var values = new List<int>(scale.Questions.Count);
            for (var index = 0; index < scale.Questions.Count; index++)
            {
                if (!answers.TryGetValue(index.ToString(), out var value))
                    throw new MhopApiException(400, "请完成量表全部题目");
                values.Add(value);
            }
            if (values.Any(value => value is < 0 or > 3))
                throw new MhopApiException(400, "量表作答值非法");

            score = values.Sum();
            (level, levelCode) = MhopScales.ScoreBand(assessmentType, score.Value);
            if (scale.CrisisIndex is int crisisIndex && values[crisisIndex] > 0) crisis = true;
        }
        else if (assessmentType != "free")
        {
            throw new MhopApiException(400, "未知的评估类型");
        }
        else if (freeText.Length == 0)
        {
            throw new MhopApiException(400, "请先描述你最近的状态与感受");
        }

        var (result, _) = await _ai.AssessAsync(assessmentType, score, level, freeText, crisis);

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
                Score = score,
                Level = level,
                CreatedAt = DateTime.UtcNow,
            };
            _db.MhopAssessments.Add(record);
            await _db.SaveChangesAsync();
            saved = true;
            recordId = record.Id;
            createdAt = record.CreatedAt;
        }

        return MhopOk(new AssessmentOut
        {
            Id = recordId,
            AssessmentType = assessmentType,
            Score = score,
            Level = level,
            LevelCode = levelCode,
            Crisis = crisis,
            AiResult = result,
            SavedCloud = saved,
            CreatedAt = createdAt,
        });
    }

    [HttpGet("mine")]
    public async Task<IActionResult> MyAssessments()
    {
        var current = await _current.RequireAsync();
        var rows = await _db.MhopAssessments
            .Where(record => record.UserId == current.Id)
            .OrderByDescending(record => record.CreatedAt)
            .ToListAsync();

        return MhopOk(rows.Select(record => new AssessmentOut
        {
            Id = record.Id,
            AssessmentType = record.AssessmentType,
            Score = record.Score,
            Level = record.Level ?? string.Empty,
            Crisis = false,
            AiResult = record.AiResult,
            SavedCloud = true,
            CreatedAt = record.CreatedAt,
        }).ToList());
    }
}
