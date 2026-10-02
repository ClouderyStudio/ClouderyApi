using ClouderyApi.Modules.Cloudery.Domain;
using ClouderyApi.Modules.Cloudery.Api.Contracts;
using ClouderyApi.Shared.Authorization;
using ClouderyApi.Modules.Cloudery.Application;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Cloudery.Api;

/// <summary>
/// 内部测试试卷（心理学项目）——公开可读（下发不含答案），判分走服务端，写操作仅管理员。
/// </summary>
[Route("exam/[controller]")]
[ApiController]
public class ExamPapersController(ExamPaperAppService papers) : ControllerBase
{
    /// <summary>公开读：列表（不含 Answer / Note）</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExamPaperView>>> GetExamPapers()
        => await papers.ListAsync();

    /// <summary>公开读：单份（不含 Answer / Note）</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ExamPaperView>> GetExamPaper(string id)
    {
        var paper = await papers.FindViewAsync(id);
        if (paper == null) return NotFound(new { success = false, message = "未找到该试卷" });
        return paper;
    }

    /// <summary>管理员读取全量（含答案/解析），供后台编辑；公开读不含答案</summary>
    [HttpGet("{id}/full")]
    [AdminOnly]
    public async Task<ActionResult<ExamPaperFullView>> GetExamPaperFull(string id)
    {
        var paper = await papers.FindFullAsync(id);
        if (paper == null) return NotFound(new { success = false, message = "未找到该试卷" });
        return paper;
    }

    /// <summary>服务端判分：接收作答，返回逐题对错 + 标准答案/解析（供交卷核对）</summary>
    [HttpPost("{id}/grade")]
    public async Task<ActionResult<ExamGradeResult>> Grade(string id, [FromBody] GradeRequest request)
    {
        var result = await papers.GradeAsync(id, request);
        if (result == null) return NotFound(new { success = false, message = "未找到该试卷" });
        return result;
    }

    // ---- 写操作（管理员） ----
    [HttpPost]
    [AdminOnly]
    public async Task<ActionResult<ExamPaperFullView>> PostExamPaper([FromBody] ExamPaperInput input)
    {
        var result = await papers.CreateAsync(input);
        switch (result.Outcome)
        {
            case ExamPaperWriteOutcome.DuplicateId:
                return Conflict(new { success = false, message = "试卷ID已存在" });
            case ExamPaperWriteOutcome.SaveFailed:
                return Conflict(new { success = false, message = "保存失败：ID 可能冲突" });
        }

        return CreatedAtAction("GetExamPaper", new { id = result.Paper!.Id }, result.Paper);
    }

    [HttpPut("{id}")]
    [AdminOnly]
    public async Task<IActionResult> PutExamPaper(string id, [FromBody] ExamPaperInput input)
    {
        return (await papers.UpdateAsync(id, input)) switch
        {
            ExamPaperWriteOutcome.NotFound => NotFound(new { success = false, message = "未找到该试卷" }),
            ExamPaperWriteOutcome.ConcurrencyConflict => Conflict(new { success = false, message = "并发冲突" }),
            _ => NoContent(),
        };
    }

    [HttpDelete("{id}")]
    [AdminOnly]
    public async Task<IActionResult> DeleteExamPaper(string id)
    {
        return await papers.DeleteAsync(id)
            ? NoContent()
            : NotFound(new { success = false, message = "未找到该试卷" });
    }
}
