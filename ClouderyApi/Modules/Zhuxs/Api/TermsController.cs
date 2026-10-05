using ClouderyApi.Modules.Zhuxs.Api.Contracts;
using ClouderyApi.Shared.Authorization;
using ClouderyApi.Modules.Zhuxs.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClouderyApi.Shared.Json;

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
        if (zhuxsTerm == null) return ApiError.Result(404, "记录不存在");
        return zhuxsTerm;
    }

    [HttpPut("{id}")]
    [AdminOnly]
    public async Task<IActionResult> PutZhuxsTerm(string id, [FromBody] TermDto dto)
    {
        return await terms.UpdateAsync(id, dto) ? NoContent() : ApiError.Result(404, "记录不存在");
    }

    [HttpPost]
    [AdminOnly]
    public async Task<ActionResult<TermOut>> PostZhuxsTerm([FromBody] TermDto dto)
    {
        TermOut created;
        try
        {
            created = await terms.CreateAsync(dto);
        }
        catch (ZhuxsWriteConflictException)
        {
            return ApiError.Result(409, "记录冲突");
        }

        return CreatedAtAction("GetZhuxsTerm", new { id = created.Id }, created);
    }

    [HttpDelete("{id}")]
    [AdminOnly]
    public async Task<IActionResult> DeleteZhuxsTerm(string id)
    {
        return await terms.DeleteAsync(id) ? NoContent() : ApiError.Result(404, "记录不存在");
    }
}
