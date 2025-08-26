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
    public class ApplicationsController : ControllerBase
    {
        private readonly ClouderyApiContext _context;

        public ApplicationsController(ClouderyApiContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Application>>> GetZhuxsApplication()
        {
            return await _context.ZhuxsApplications.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Application>> GetZhuxsApplication(string id)
        {
            var zhuxsApplication = await _context.ZhuxsApplications.FindAsync(id);

            if (zhuxsApplication == null)
            {
                return NotFound();
            }

            return zhuxsApplication;
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutZhuxsApplication(string id, Application zhuxsApplication)
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

        [HttpPost]
        public async Task<ActionResult<Application>> PostZhuxsApplication(Application zhuxsApplication)
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
