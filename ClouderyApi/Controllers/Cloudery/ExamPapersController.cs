using ClouderyApi.Data;
using ClouderyApi.Models.Cloudery;
using ClouderyApi.Controllers.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Cloudery;

/// <summary>
/// 内部测试试卷（心理学项目）——公开可读，写操作仅管理员。
/// </summary>
[Route("exam/[controller]")]
[ApiController]
public class ExamPapersController(ClouderyApiContext context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExamPaper>>> GetExamPapers()
    {
        return await context.ExamPapers.OrderBy(p => p.Id).ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ExamPaper>> GetExamPaper(string id)
    {
        var paper = await context.ExamPapers.FindAsync(id);
        if (paper == null) return NotFound(new { success = false, message = "未找到该试卷" });
        return paper;
    }

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