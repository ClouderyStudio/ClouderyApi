using ClouderyApi.Data;
using ClouderyApi.Models.Zhuxs;
using ClouderyApi.Models.Zhuxs.DTOs;
using ClouderyApi.Controllers.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Zhuxs;

/// <summary>
/// 白名单（邀请码）管理：邀请码属敏感数据，读取与写入均仅限管理员。
/// </summary>
[Route("zhuxs/[controller]")]
[ApiController]
[Authorize]
[AdminOnly]
public class WhitelistsController(ClouderyApiContext context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Whitelist>>> GetWhitelist()
    {
        // 加排序与上限，避免无界返回全表
        return await context.ZhuxsWhitelists
            .OrderBy(w => w.Code)
            .Take(1000)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Whitelist>> GetWhitelist(string id)
    {
        var whitelist = await context.ZhuxsWhitelists.FindAsync(id);
        if (whitelist == null) return NotFound();
        return whitelist;
    }

    [HttpPost]
    public async Task<ActionResult<Whitelist>> PostWhitelist([FromBody] WhitelistDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        // 主键由服务端生成，客户端不可指定（防 over-posting）
        var whitelist = new Whitelist { Id = Guid.NewGuid().ToString("N"), Code = dto.Code };
        context.ZhuxsWhitelists.Add(whitelist);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { success = false, message = "记录冲突" });
        }
        return CreatedAtAction("GetWhitelist", new { id = whitelist.Id }, whitelist);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteWhitelist(string id)
    {
        var whitelist = await context.ZhuxsWhitelists.FindAsync(id);
        if (whitelist == null) return NotFound();
        context.ZhuxsWhitelists.Remove(whitelist);
        await context.SaveChangesAsync();
        return NoContent();
    }
}
