using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClouderyApi.Data;
using ClouderyApi.Models.Zhuxs;

namespace ClouderyApi.Controllers.Zhuxs
{
    [Route("zhuxs/[controller]")]
    [ApiController]
    public class WhitelistsController : ControllerBase
    {
        private readonly ClouderyApiContext _context;

        public WhitelistsController(ClouderyApiContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Whitelist>>> GetWhitelist()
        {
            return await _context.ZhuxsWhitelists.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Whitelist>> GetWhitelist(string id)
        {
            var whitelist = await _context.ZhuxsWhitelists.FindAsync(id);

            if (whitelist == null)
            {
                return NotFound();
            }

            return whitelist;
        }

        [HttpPost]
        public async Task<ActionResult<Whitelist>> PostWhitelist(Whitelist whitelist)
        {
            _context.ZhuxsWhitelists.Add(whitelist);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (WhitelistExists(whitelist.Id))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetWhitelist", new { id = whitelist.Id }, whitelist);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWhitelist(string id)
        {
            var whitelist = await _context.ZhuxsWhitelists.FindAsync(id);
            if (whitelist == null)
            {
                return NotFound();
            }

            _context.ZhuxsWhitelists.Remove(whitelist);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool WhitelistExists(string id)
        {
            return _context.ZhuxsWhitelists.Any(e => e.Id == id);
        }
    }
}
