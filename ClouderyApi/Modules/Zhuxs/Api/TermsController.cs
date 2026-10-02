using ClouderyApi.Modules.Zhuxs.Api.Contracts;
using ClouderyApi.Shared.Filters;
using ClouderyApi.Modules.Zhuxs.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Zhuxs.Api;

[Route("zhuxs/[controller]")]
[ApiController]
[Authorize]
public class TermsController(TermsAppService terms) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<TermOut>>> GetZhuxsTerm()
    {
        return await terms.ListAsync();
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<TermOut>> GetZhuxsTerm(string id)
    {
        var zhuxsTerm = await terms.FindAsync(id);
        if (zhuxsTerm == null) return NotFound();
        return zhuxsTerm;
    }

    [HttpPut("{id}")]
    [AdminOnly]
    public async Task<IActionResult> PutZhuxsTerm(string id, [FromBody] TermDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        return await terms.UpdateAsync(id, dto) ? NoContent() : NotFound();
    }

    [HttpPost]
    [AdminOnly]
    public async Task<ActionResult<TermOut>> PostZhuxsTerm([FromBody] TermDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        TermOut created;
        try
        {
            created = await terms.CreateAsync(dto);
        }
        catch (ZhuxsWriteConflictException)
        {
            return Conflict(new { success = false, message = "记录冲突" });
        }

        return CreatedAtAction("GetZhuxsTerm", new { id = created.Id }, created);
    }

    [HttpDelete("{id}")]
    [AdminOnly]
    public async Task<IActionResult> DeleteZhuxsTerm(string id)
    {
        return await terms.DeleteAsync(id) ? NoContent() : NotFound();
    }
}
