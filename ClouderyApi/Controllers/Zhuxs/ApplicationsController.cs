using ClouderyApi.Data;
using ClouderyApi.Models.Zhuxs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Zhuxs;

[Route("zhuxs/[controller]")]
[ApiController]
[Authorize] // 写操作需登录；GET 保留匿名
public class ApplicationsController(ClouderyApiContext context) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<Application>>> GetZhuxsApplication()
    {
        return await context.ZhuxsApplications.ToListAsync();
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
    public async Task<IActionResult> PutZhuxsApplication(string id, Application zhuxsApplication)
    {
        if (id != zhuxsApplication.Id) return BadRequest();

        context.Entry(zhuxsApplication).State = EntityState.Modified;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!ZhuxsApplicationExists(id)) return NotFound();

            throw;
        }

        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<Application>> PostZhuxsApplication(Application zhuxsApplication)
    {
        context.ZhuxsApplications.Add(zhuxsApplication);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (ZhuxsApplicationExists(zhuxsApplication.Id)) return Conflict();

            throw;
        }

        return CreatedAtAction("GetZhuxsApplication", new { id = zhuxsApplication.Id }, zhuxsApplication);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteZhuxsApplication(string id)
    {
        var zhuxsApplication = await context.ZhuxsApplications.FindAsync(id);
        if (zhuxsApplication == null) return NotFound();

        context.ZhuxsApplications.Remove(zhuxsApplication);
        await context.SaveChangesAsync();

        return NoContent();
    }

    private bool ZhuxsApplicationExists(string id)
    {
        return context.ZhuxsApplications.Any(e => e.Id == id);
    }
}