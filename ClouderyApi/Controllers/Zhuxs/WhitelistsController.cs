using ClouderyApi.Data;
using ClouderyApi.Models.Zhuxs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Zhuxs;

[Route("zhuxs/[controller]")]
[ApiController]
public class WhitelistsController(ClouderyApiContext context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Whitelist>>> GetWhitelist()
    {
        return await context.ZhuxsWhitelists.ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Whitelist>> GetWhitelist(string id)
    {
        var whitelist = await context.ZhuxsWhitelists.FindAsync(id);

        if (whitelist == null) return NotFound();

        return whitelist;
    }

    [HttpPost]
    public async Task<ActionResult<Whitelist>> PostWhitelist(Whitelist whitelist)
    {
        context.ZhuxsWhitelists.Add(whitelist);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (WhitelistExists(whitelist.Id)) return Conflict();

            throw;
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

    private bool WhitelistExists(string id)
    {
        return context.ZhuxsWhitelists.Any(e => e.Id == id);
    }
}