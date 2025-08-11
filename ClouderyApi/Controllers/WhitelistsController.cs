using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClouderyApi.Data;
using ClouderyApi.Models;

namespace ClouderyApi.Controllers
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

        // GET: api/Whitelists
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Whitelist>>> GetWhitelist()
        {
            return await _context.Whitelists.ToListAsync();
        }

        // GET: api/Whitelists/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Whitelist>> GetWhitelist(string id)
        {
            var whitelist = await _context.Whitelists.FindAsync(id);

            if (whitelist == null)
            {
                return NotFound();
            }

            return whitelist;
        }

        // POST: api/Whitelists
        [HttpPost]
        public async Task<ActionResult<Whitelist>> PostWhitelist(Whitelist whitelist)
        {
            _context.Whitelists.Add(whitelist);
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

        // DELETE: api/Whitelists/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWhitelist(string id)
        {
            var whitelist = await _context.Whitelists.FindAsync(id);
            if (whitelist == null)
            {
                return NotFound();
            }

            _context.Whitelists.Remove(whitelist);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool WhitelistExists(string id)
        {
            return _context.Whitelists.Any(e => e.Id == id);
        }
    }
}
