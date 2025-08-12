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
    public class ZhuxsApplicationsController : ControllerBase
    {
        private readonly ClouderyApiContext _context;

        public ZhuxsApplicationsController(ClouderyApiContext context)
        {
            _context = context;
        }

        // GET: api/ZhuxsApplications
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ZhuxsApplication>>> GetZhuxsApplication()
        {
            return await _context.ZhuxsApplications.ToListAsync();
        }

        // GET: api/ZhuxsApplications/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ZhuxsApplication>> GetZhuxsApplication(string id)
        {
            var zhuxsApplication = await _context.ZhuxsApplications.FindAsync(id);

            if (zhuxsApplication == null)
            {
                return NotFound();
            }

            return zhuxsApplication;
        }

        // PUT: api/ZhuxsApplications/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutZhuxsApplication(string id, ZhuxsApplication zhuxsApplication)
        {
            if (id != zhuxsApplication.Id)
            {
                return BadRequest();
            }

            _context.Entry(zhuxsApplication).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ZhuxsApplicationExists(id))
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

        // POST: api/ZhuxsApplications
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<ZhuxsApplication>> PostZhuxsApplication(ZhuxsApplication zhuxsApplication)
        {
            _context.ZhuxsApplications.Add(zhuxsApplication);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ZhuxsApplicationExists(zhuxsApplication.Id))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetZhuxsApplication", new { id = zhuxsApplication.Id }, zhuxsApplication);
        }

        // DELETE: api/ZhuxsApplications/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteZhuxsApplication(string id)
        {
            var zhuxsApplication = await _context.ZhuxsApplications.FindAsync(id);
            if (zhuxsApplication == null)
            {
                return NotFound();
            }

            _context.ZhuxsApplications.Remove(zhuxsApplication);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ZhuxsApplicationExists(string id)
        {
            return _context.ZhuxsApplications.Any(e => e.Id == id);
        }
    }
}
