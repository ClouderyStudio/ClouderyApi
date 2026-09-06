using System.Text.Json;
using ClouderyApi.Data;
using ClouderyApi.Models.Cloudery;
using ClouderyApi.Controllers.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Cloudery;

/// <summary>
/// 内部测试试卷（心理学项目）——公开可读（下发不含答案），判分走服务端，写操作仅管理员。
/// </summary>
[Route("exam/[controller]")]
[ApiController]
public class ExamPapersController(ClouderyApiContext context) : ControllerBase
{
    private static ExamPaperView ToView(ExamPaper p) => new()
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

    /// <summary>公开读：列表（不含 Answer / Note）</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExamPaperView>>> GetExamPapers()
        => (await context.ExamPapers.OrderBy(p => p.Id).ToListAsync()).Select(ToView).ToList();

    /// <summary>公开读：单份（不含 Answer / Note）</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ExamPaperView>> GetExamPaper(string id)
    {
        var paper = await context.ExamPapers.FindAsync(id);
        if (paper == null) return NotFound(new { success = false, message = "未找到该试卷" });
        return ToView(paper);
    }

    /// <summary>管理员读取全量（含答案/解析），供后台编辑；公开读不含答案</summary>
    [HttpGet("{id}/full")]
    [AdminOnly]
    public async Task<ActionResult<ExamPaper>> GetExamPaperFull(string id)
    {
        var paper = await context.ExamPapers.FindAsync(id);
        if (paper == null) return NotFound(new { success = false, message = "未找到该试卷" });
        return paper;
    }

    /// <summary>服务端判分：接收作答，返回逐题对错 + 标准答案/解析（供交卷核对）</summary>
    [HttpPost("{id}/grade")]
    public async Task<ActionResult<ExamGradeResult>> Grade(string id, [FromBody] GradeRequest request)
    {
        var paper = await context.ExamPapers.FindAsync(id);
        if (paper == null) return NotFound(new { success = false, message = "未找到该试卷" });

        var results = new List<ExamGradeItem>();
        int scorable = 0, essay = 0, correctCount = 0;
        double totalPoints = 0, earned = 0;

        for (int s = 0; s < paper.Sections.Count; s++)
        {
            var section = paper.Sections[s];
            for (int q = 0; q < section.Questions.Count; q++)
            {
                var question = section.Questions[q];
                var key = $"{s}-{q}";
                var type = question.Type ?? (question.Options != null ? "single" : "judge");
                var points = section.PointsPerQuestion ?? 1.0;
                totalPoints += points;

                bool answered = request.Answers.TryGetValue(key, out var raw) && raw.ValueKind != JsonValueKind.Null;
                string[]? userMulti = null;
                string? userSingle = null;
                if (answered)
                {
                    if (raw.ValueKind == JsonValueKind.Array)
                        userMulti = raw.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                    else if (raw.ValueKind == JsonValueKind.String)
                        userSingle = raw.GetString();
                    else
                        answered = false;
                }

                bool correct = false;
                if (type == "essay")
                {
                    essay++;
                }
                else
                {
                    scorable++;
                    if (type == "multiple")
                    {
                        var std = string.Concat(question.Answer.OrderBy(c => c));
                        var userArr = userMulti ?? (userSingle != null ? new[] { userSingle } : Array.Empty<string>());
                        var user = string.Concat(userArr.OrderBy(x => x));
                        correct = answered && user == std;
                    }
                    else
                    {
                        correct = answered && userSingle != null && userSingle.Trim() == question.Answer.Trim();
                    }
                }

                if (correct) { correctCount++; earned += points; }
                results.Add(new ExamGradeItem
                {
                    Key = key,
                    Type = type,
                    Correct = correct,
                    Answered = answered,
                    Points = points,
                    Earned = correct ? points : 0,
                    StandardAnswer = question.Answer,
                    Note = question.Note,
                });
            }
        }

        var accuracy = scorable == 0 ? 0 : (int)Math.Round((double)correctCount / scorable * 100);

        return new ExamGradeResult
        {
            TotalCount = paper.Sections.Sum(x => x.Questions.Count),
            ScorableCount = scorable,
            EssayCount = essay,
            CorrectCount = correctCount,
            TotalPoints = totalPoints,
            Earned = earned,
            Accuracy = accuracy,
            Results = results,
        };
    }

    // ---- 写操作（管理员） ----
    [HttpPost]
    [AdminOnly]
    public async Task<ActionResult<ExamPaper>> PostExamPaper([FromBody] ExamPaper paper)
    {
        if (string.IsNullOrWhiteSpace(paper.Id))
            paper.Id = Guid.NewGuid().ToString("N");
        if (await context.ExamPapers.AnyAsync(p => p.Id == paper.Id))
            return Conflict(new { success = false, message = "试卷ID已存在" });
        paper.UpdatedAt = DateTime.UtcNow;
        context.ExamPapers.Add(paper);
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { success = false, message = "保存失败：ID 可能冲突" }); }
        return CreatedAtAction("GetExamPaper", new { id = paper.Id }, paper);
    }

    [HttpPut("{id}")]
    [AdminOnly]
    public async Task<IActionResult> PutExamPaper(string id, [FromBody] ExamPaper paper)
    {
        var existing = await context.ExamPapers.FindAsync(id);
        if (existing == null) return NotFound(new { success = false, message = "未找到该试卷" });
        existing.Name = paper.Name;
        existing.Sections = paper.Sections;
        existing.UpdatedAt = DateTime.UtcNow;
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { success = false, message = "并发冲突" }); }
        return NoContent();
    }

    [HttpDelete("{id}")]
    [AdminOnly]
    public async Task<IActionResult> DeleteExamPaper(string id)
    {
        var paper = await context.ExamPapers.FindAsync(id);
        if (paper == null) return NotFound(new { success = false, message = "未找到该试卷" });
        context.ExamPapers.Remove(paper);
        await context.SaveChangesAsync();
        return NoContent();
    }
}

/// <summary>判分请求：answers 以 "s-q" 为键，值为 string（单选/判断/简答）或 string[]（多选）</summary>
public class GradeRequest
{
    public Dictionary<string, JsonElement> Answers { get; set; } = new();
}

public class ExamGradeItem
{
    public required string Key { get; set; }
    public string? Type { get; set; }
    public bool Correct { get; set; }
    public bool Answered { get; set; }
    public double Points { get; set; }
    public double Earned { get; set; }
    public string? StandardAnswer { get; set; }
    public string? Note { get; set; }
}

public class ExamGradeResult
{
    public int TotalCount { get; set; }
    public int ScorableCount { get; set; }
    public int EssayCount { get; set; }
    public int CorrectCount { get; set; }
    public double TotalPoints { get; set; }
    public double Earned { get; set; }
    public int Accuracy { get; set; }
    public List<ExamGradeItem>? Results { get; set; }
}
