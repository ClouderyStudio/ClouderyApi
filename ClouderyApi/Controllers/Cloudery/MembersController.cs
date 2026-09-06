using ClouderyApi.Data;
using ClouderyApi.Models.Cloudery;
using ClouderyApi.Models.Cloudery.DTOs;
using ClouderyApi.Controllers.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Cloudery;

[Route("cloudery/[controller]")]
[ApiController]
[Authorize]
public class MembersController(ClouderyApiContext context) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<Member>>> GetClouderyMember()
    {
        return await context.ClouderyMembers
            .OrderBy(x => x.Name)
            .Take(1000)
            .ToListAsync();
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<Member>> GetClouderyMember(string id)
    {
        var clouderyMember = await context.ClouderyMembers.FindAsync(id);
        if (clouderyMember == null) return NotFound();
        return clouderyMember;
    }

    [HttpPut("{id}")]
    [AdminOnly]
    public async Task<IActionResult> PutClouderyMember(string id, [FromBody] MemberDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        var clouderyMember = await context.ClouderyMembers.FindAsync(id);
        if (clouderyMember == null) return NotFound();

        clouderyMember.Name = dto.Name;
        clouderyMember.Position = dto.Position;
        clouderyMember.Description = dto.Description;
        clouderyMember.Socials = dto.Socials;

        try { await context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            if (await context.ClouderyMembers.AnyAsync(e => e.Id == id)) throw;
            return NotFound();
        }
        return NoContent();
    }

    [HttpPost]
    [AdminOnly]
    public async Task<ActionResult<Member>> PostClouderyMember([FromBody] MemberDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "参数校验失败" });

        var clouderyMember = new Member
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = dto.Name,
            Position = dto.Position,
            Description = dto.Description,
            Socials = dto.Socials
        };

        context.ClouderyMembers.Add(clouderyMember);
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            return Conflict(new { success = false, message = "记录冲突" });
        }
        return CreatedAtAction("GetClouderyMember", new { id = clouderyMember.Id }, clouderyMember);
    }

    [HttpDelete("{id}")]
    [AdminOnly]
    public async Task<IActionResult> DeleteClouderyMember(string id)
    {
        var clouderyMember = await context.ClouderyMembers.FindAsync(id);
        if (clouderyMember == null) return NotFound();
        context.ClouderyMembers.Remove(clouderyMember);
        await context.SaveChangesAsync();
        return NoContent();
    }
}
