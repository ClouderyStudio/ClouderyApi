using ClouderyApi.Data;
using ClouderyApi.Models.Zhuxs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Zhuxs;

[Route("zhuxs/[controller]")]
[ApiController]
[Authorize] // 写操作需登录；GET 保留匿名
public class TermsController(ClouderyApiContext context) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<Term>>> GetZhuxsTerm()
    {
        return await context.ZhuxsTerms.ToListAsync();
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
    public async Task<IActionResult> PutZhuxsTerm(string id, Term zhuxsTerm)
    {
        if (id != zhuxsTerm.Id) return BadRequest();

        context.Entry(zhuxsTerm).State = EntityState.Modified;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!ZhuxsTermExists(id)) return NotFound();

            throw;
        }

        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<Term>> PostZhuxsTerm(Term zhuxsTerm)
    {
        context.ZhuxsTerms.Add(zhuxsTerm);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (ZhuxsTermExists(zhuxsTerm.Id)) return Conflict();

            throw;
        }

        return CreatedAtAction("GetZhuxsTerm", new { id = zhuxsTerm.Id }, zhuxsTerm);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteZhuxsTerm(string id)
    {
        var zhuxsTerm = await context.ZhuxsTerms.FindAsync(id);
        if (zhuxsTerm == null) return NotFound();

        context.ZhuxsTerms.Remove(zhuxsTerm);
        await context.SaveChangesAsync();

        return NoContent();
    }

    private bool ZhuxsTermExists(string id)
    {
        return context.ZhuxsTerms.Any(e => e.Id == id);
    }
}