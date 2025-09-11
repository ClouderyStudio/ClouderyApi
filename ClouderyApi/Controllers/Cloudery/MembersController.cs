using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClouderyApi.Data;
using ClouderyApi.Models.Cloudery;

namespace ClouderyApi.Controllers.Cloudery
{
    [Route("cloudery/[controller]")]
    [ApiController]
    public class MembersController : ControllerBase
    {
        private readonly ClouderyApiContext _context;

        public MembersController(ClouderyApiContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Member>>> GetClouderyMember()
        {
            return await _context.ClouderyMembers.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Member>> GetClouderyMember(string id)
        {
            var clouderyMember = await _context.ClouderyMembers.FindAsync(id);

            if (clouderyMember == null)
            {
                return NotFound();
            }

            return clouderyMember;
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutClouderyMember(string id, Member clouderyMember)
        {
            if (id != clouderyMember.Id)
            {
                return BadRequest();
            }

            _context.Entry(clouderyMember).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ClouderyMemberExsits(id))
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
        public async Task<ActionResult<Member>> PostClouderyMember(Member clouderyMember)
        {
            _context.ClouderyMembers.Add(clouderyMember);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ClouderyMemberExsits(clouderyMember.Id))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetClouderyMember", new { id = clouderyMember.Id }, clouderyMember);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteClouderyMember(string id)
        {
            var clouderyMember = await _context.ClouderyMembers.FindAsync(id);
            if (clouderyMember == null)
            {
                return NotFound();
            }

            _context.ClouderyMembers.Remove(clouderyMember);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ClouderyMemberExsits(string id)
        {
            return _context.ClouderyMembers.Any(e => e.Id == id);
        }
    }
}
