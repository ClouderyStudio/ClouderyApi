using ClouderyApi.Modules.Zhuxs.Api.Contracts;
using ClouderyApi.Shared.Filters;
using ClouderyApi.Modules.Zhuxs.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Zhuxs.Api;

[Route("zhuxs/[controller]")]
[ApiController]
[Authorize]
public class ApplicationsController(ApplicationsAppService applications) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<ApplicationOut>>> GetZhuxsApplication()
    {
        return await applications.ListAsync();
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApplicationOut>> GetZhuxsApplication(string id)
    {
        var zhuxsApplication = await applications.FindAsync(id);
        if (zhuxsApplication == null) return NotFound();
        return zhuxsApplication;
    }

    [HttpPut("{id}")]
    [AdminOnly]
    public async Task<IActionResult> PutZhuxsApplication(string id, [FromBody] ApplicationDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        return await applications.UpdateAsync(id, dto) ? NoContent() : NotFound();
    }

    [HttpPost]
    [AdminOnly]
    public async Task<ActionResult<ApplicationOut>> PostZhuxsApplication([FromBody] ApplicationDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        ApplicationOut created;
        try
        {
            created = await applications.CreateAsync(dto);
        }
        catch (ZhuxsWriteConflictException)
        {
            return Conflict(new { success = false, message = "记录冲突" });
        }

        return CreatedAtAction("GetZhuxsApplication", new { id = created.Id }, created);
    }

    [HttpDelete("{id}")]
    [AdminOnly]
    public async Task<IActionResult> DeleteZhuxsApplication(string id)
    {
        return await applications.DeleteAsync(id) ? NoContent() : NotFound();
    }
}
