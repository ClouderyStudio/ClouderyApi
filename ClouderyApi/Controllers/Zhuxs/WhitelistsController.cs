using ClouderyApi.Models.Zhuxs.DTOs;
using ClouderyApi.Shared.Filters;
using ClouderyApi.UseCases.Zhuxs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Controllers.Zhuxs;

/// <summary>
/// 白名单（邀请码）管理：邀请码属敏感数据，读取与写入均仅限管理员。
/// </summary>
[Route("zhuxs/[controller]")]
[ApiController]
[Authorize]
[AdminOnly]
public class WhitelistsController(WhitelistsAppService whitelists) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WhitelistOut>>> GetWhitelist()
    {
        // 加排序与上限，避免无界返回全表
        return await whitelists.ListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WhitelistOut>> GetWhitelist(string id)
    {
        var whitelist = await whitelists.FindAsync(id);
        if (whitelist == null) return NotFound();
        return whitelist;
    }

    [HttpPost]
    public async Task<ActionResult<WhitelistOut>> PostWhitelist([FromBody] WhitelistDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        WhitelistOut created;
        try
        {
            created = await whitelists.CreateAsync(dto);
        }
        catch (ZhuxsWriteConflictException)
        {
            return Conflict(new { success = false, message = "记录冲突" });
        }

        return CreatedAtAction("GetWhitelist", new { id = created.Id }, created);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteWhitelist(string id)
    {
        return await whitelists.DeleteAsync(id) ? NoContent() : NotFound();
    }
}
