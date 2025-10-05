using ClouderyApi.Data;
using ClouderyApi.Models.Cloudery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Cloudery;

[Route("cloudery/[controller]")]
[ApiController]
public class MembersController(ClouderyApiContext context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Member>>> GetClouderyMember()
    {
        return await context.ClouderyMembers.ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Member>> GetClouderyMember(string id)
    {
        var clouderyMember = await context.ClouderyMembers.FindAsync(id);

        if (clouderyMember == null) return NotFound();

        return clouderyMember;
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutClouderyMember(string id, Member clouderyMember)
    {
        if (id != clouderyMember.Id) return BadRequest();

        context.Entry(clouderyMember).State = EntityState.Modified;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!ClouderyMemberExsits(id)) return NotFound();

            throw;
        }

        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<Member>> PostClouderyMember(Member clouderyMember)
    {
        context.ClouderyMembers.Add(clouderyMember);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (ClouderyMemberExsits(clouderyMember.Id)) return Conflict();

            throw;
        }

        return CreatedAtAction("GetClouderyMember", new { id = clouderyMember.Id }, clouderyMember);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteClouderyMember(string id)
    {
        var clouderyMember = await context.ClouderyMembers.FindAsync(id);
        if (clouderyMember == null) return NotFound();

        context.ClouderyMembers.Remove(clouderyMember);
        await context.SaveChangesAsync();

        return NoContent();
    }

    private bool ClouderyMemberExsits(string id)
    {
        return context.ClouderyMembers.Any(e => e.Id == id);
    }
}