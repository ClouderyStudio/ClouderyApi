using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClouderyApi.Data;
using ClouderyApi.Models.Zhuxs;

namespace ClouderyApi.Controllers.Zhuxs
{
    [Route("zhuxs/[controller]")]
    [ApiController]
    public class TermsController : ControllerBase
    {
        private readonly ClouderyApiContext _context;

        public TermsController(ClouderyApiContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Term>>> GetZhuxsTerm()
        {
            return await _context.ZhuxsTerms.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Term>> GetZhuxsTerm(string id)
        {
            var zhuxsTerm = await _context.ZhuxsTerms.FindAsync(id);

            if (zhuxsTerm == null)
            {
                return NotFound();
            }

            return zhuxsTerm;
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutZhuxsTerm(string id, Term zhuxsTerm)
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

        [HttpPost]
        public async Task<ActionResult<Term>> PostZhuxsTerm(Term zhuxsTerm)
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
