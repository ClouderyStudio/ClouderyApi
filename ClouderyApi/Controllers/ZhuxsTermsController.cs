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
    public class ZhuxsTermsController : ControllerBase
    {
        private readonly ClouderyApiContext _context;

        public ZhuxsTermsController(ClouderyApiContext context)
        {
            _context = context;
        }

        // GET: api/ZhuxsTerms
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ZhuxsTerm>>> GetZhuxsTerm()
        {
            return await _context.ZhuxsTerms.ToListAsync();
        }

        // GET: api/ZhuxsTerms/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ZhuxsTerm>> GetZhuxsTerm(string id)
        {
            var zhuxsTerm = await _context.ZhuxsTerms.FindAsync(id);

            if (zhuxsTerm == null)
            {
                return NotFound();
            }

            return zhuxsTerm;
        }

        // PUT: api/ZhuxsTerms/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutZhuxsTerm(string id, ZhuxsTerm zhuxsTerm)
        {
            if (id != zhuxsTerm.Id)
            {
                return BadRequest();
            }

            _context.Entry(zhuxsTerm).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ZhuxsTermExists(id))
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

        // POST: api/ZhuxsTerms
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<ZhuxsTerm>> PostZhuxsTerm(ZhuxsTerm zhuxsTerm)
        {
            _context.ZhuxsTerms.Add(zhuxsTerm);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ZhuxsTermExists(zhuxsTerm.Id))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetZhuxsTerm", new { id = zhuxsTerm.Id }, zhuxsTerm);
        }

        // DELETE: api/ZhuxsTerms/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteZhuxsTerm(string id)
        {
            var zhuxsTerm = await _context.ZhuxsTerms.FindAsync(id);
            if (zhuxsTerm == null)
            {
                return NotFound();
            }

            _context.ZhuxsTerms.Remove(zhuxsTerm);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ZhuxsTermExists(string id)
        {
            return _context.ZhuxsTerms.Any(e => e.Id == id);
        }
    }
}
