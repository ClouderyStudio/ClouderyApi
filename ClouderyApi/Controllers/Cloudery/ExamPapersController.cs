using ClouderyApi.Data;
using ClouderyApi.Models.Cloudery;
using ClouderyApi.Models.Cloudery.DTOs;
using ClouderyApi.Controllers.Filters;
using ClouderyApi.UseCases.Cloudery.Domain;
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

        return ExamPaperGrader.Grade(paper, request);
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
