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
    public class ZhuxsFilesController : ControllerBase
    {
        private readonly ClouderyApiContext _context;

        public ZhuxsFilesController(ClouderyApiContext context)
        {
            _context = context;
        }

        // GET: api/ZhuxsLinks
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ZhuxsFile>>> GetZhuxsLink()
        {
            return await _context.ZhuxsFiles.ToListAsync();
        }

        // GET: api/ZhuxsLinks/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ZhuxsFile>> GetZhuxsLink(string id)
        {
            var zhuxsLink = await _context.ZhuxsFiles.FindAsync(id);

            if (zhuxsLink == null)
            {
                return NotFound();
            }

            return zhuxsLink;
        }

        // PUT: api/ZhuxsLinks/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutZhuxsLink(string id, ZhuxsFile zhuxsLink)
        {
            if (id != zhuxsLink.Id)
            {
                return BadRequest();
            }

            _context.Entry(zhuxsLink).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ZhuxsLinkExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/ZhuxsLinks
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<ZhuxsFile>> PostZhuxsLink(ZhuxsFile zhuxsLink)
        {
            _context.ZhuxsFiles.Add(zhuxsLink);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ZhuxsLinkExists(zhuxsLink.Id))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetZhuxsLink", new { id = zhuxsLink.Id }, zhuxsLink);
        }

        // DELETE: api/ZhuxsLinks/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteZhuxsLink(string id)
        {
            var zhuxsLink = await _context.ZhuxsFiles.FindAsync(id);
            if (zhuxsLink == null)
            {
                return NotFound();
            }

            _context.ZhuxsFiles.Remove(zhuxsLink);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ZhuxsLinkExists(string id)
        {
            return _context.ZhuxsFiles.Any(e => e.Id == id);
        }
    }
}
