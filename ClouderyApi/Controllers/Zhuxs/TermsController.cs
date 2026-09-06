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
public class TermsController(ClouderyApiContext context) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<Term>>> GetZhuxsTerm()
    {
        return await context.ZhuxsTerms
            .OrderByDescending(x => x.RecordDate)
            .Take(1000)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<Term>> GetZhuxsTerm(string id)
    {
        var zhuxsTerm = await context.ZhuxsTerms.FindAsync(id);
        if (zhuxsTerm == null) return NotFound();
        return zhuxsTerm;
    }

    [HttpPut("{id}")]
    [AdminOnly]
    public async Task<IActionResult> PutZhuxsTerm(string id, [FromBody] TermDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        var zhuxsTerm = await context.ZhuxsTerms.FindAsync(id);
        if (zhuxsTerm == null) return NotFound();

        zhuxsTerm.RecordDate = dto.RecordDate;
        zhuxsTerm.Description = dto.Description;
        zhuxsTerm.Information = dto.Information;
        zhuxsTerm.Files = dto.Files;

        try { await context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            if (await context.ZhuxsTerms.AnyAsync(e => e.Id == id)) throw;
            return NotFound();
        }
        return NoContent();
    }

    [HttpPost]
    [AdminOnly]
    public async Task<ActionResult<Term>> PostZhuxsTerm([FromBody] TermDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        var zhuxsTerm = new Term
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordDate = dto.RecordDate,
            Description = dto.Description,
            Information = dto.Information,
            Files = dto.Files
        };

        context.ZhuxsTerms.Add(zhuxsTerm);
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            return Conflict(new { success = false, message = "记录冲突" });
        }
        return CreatedAtAction("GetZhuxsTerm", new { id = zhuxsTerm.Id }, zhuxsTerm);
    }

    [HttpDelete("{id}")]
    [AdminOnly]
    public async Task<IActionResult> DeleteZhuxsTerm(string id)
    {
        var zhuxsTerm = await context.ZhuxsTerms.FindAsync(id);
        if (zhuxsTerm == null) return NotFound();
        context.ZhuxsTerms.Remove(zhuxsTerm);
        await context.SaveChangesAsync();
        return NoContent();
    }
}
