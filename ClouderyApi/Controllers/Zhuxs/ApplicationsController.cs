using ClouderyApi.Data;
using ClouderyApi.Models.Zhuxs;
using ClouderyApi.Models.Zhuxs.DTOs;
using ClouderyApi.Controllers.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Zhuxs;

[Route("zhuxs/[controller]")]
[ApiController]
[Authorize]
public class ApplicationsController(ClouderyApiContext context) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<Application>>> GetZhuxsApplication()
    {
        return await context.ZhuxsApplications
            .OrderByDescending(a => a.SubmissionDate)
            .Take(1000)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<Application>> GetZhuxsApplication(string id)
    {
        var zhuxsApplication = await context.ZhuxsApplications.FindAsync(id);
        if (zhuxsApplication == null) return NotFound();
        return zhuxsApplication;
    }

    [HttpPut("{id}")]
    [AdminOnly]
    public async Task<IActionResult> PutZhuxsApplication(string id, [FromBody] ApplicationDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        var zhuxsApplication = await context.ZhuxsApplications.FindAsync(id);
        if (zhuxsApplication == null) return NotFound();

        zhuxsApplication.Sharables = dto.Sharables;
        if (dto.Passed.HasValue)
            zhuxsApplication.Passed = dto.Passed.Value;

        try { await context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            if (await context.ZhuxsApplications.AnyAsync(e => e.Id == id)) throw;
            return NotFound();
        }
        return NoContent();
    }

    [HttpPost]
    [AdminOnly]
    public async Task<ActionResult<Application>> PostZhuxsApplication([FromBody] ApplicationDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        // 主键服务端生成；Passed 恒为 false，需管理员在 PUT 阶段审核通过（防 over-posting 绕过审核）
        var zhuxsApplication = new Application
        {
            Id = Guid.NewGuid().ToString("N"),
            Passed = false,
            SubmissionDate = DateTime.UtcNow,
            Sharables = dto.Sharables
        };

        context.ZhuxsApplications.Add(zhuxsApplication);
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            return Conflict(new { success = false, message = "记录冲突" });
        }
        return CreatedAtAction("GetZhuxsApplication", new { id = zhuxsApplication.Id }, zhuxsApplication);
    }

    [HttpDelete("{id}")]
    [AdminOnly]
    public async Task<IActionResult> DeleteZhuxsApplication(string id)
    {
        var zhuxsApplication = await context.ZhuxsApplications.FindAsync(id);
        if (zhuxsApplication == null) return NotFound();
        context.ZhuxsApplications.Remove(zhuxsApplication);
        await context.SaveChangesAsync();
        return NoContent();
    }
}
