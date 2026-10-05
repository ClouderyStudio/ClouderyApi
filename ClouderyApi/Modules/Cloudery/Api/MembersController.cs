using ClouderyApi.Modules.Cloudery.Api.Contracts;
using ClouderyApi.Shared.Authorization;
using ClouderyApi.Modules.Cloudery.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClouderyApi.Shared.Json;
using ClouderyApi.Shared.Http;

namespace ClouderyApi.Modules.Cloudery.Api;

[Route("cloudery/[controller]")]
[ApiController]
[Authorize]
public class MembersController(MembersAppService members) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<MemberOut>>> GetClouderyMember()
    {
        return await members.ListAsync();
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<MemberOut>> GetClouderyMember(string id)
    {
        var clouderyMember = await members.FindAsync(id);
        if (clouderyMember == null) return ApiError.Result(404, "记录不存在");
        return clouderyMember;
    }

    [HttpPut("{id}")]
    [AdminOnly]
    public async Task<IActionResult> PutClouderyMember(string id, [FromBody] MemberDto dto)
    {
        return await members.UpdateAsync(id, dto) ? NoContent() : ApiError.Result(404, "记录不存在");
    }

    [HttpPost]
    [AdminOnly]
    public async Task<ActionResult<MemberOut>> PostClouderyMember([FromBody] MemberDto dto)
    {
        MemberOut created;
        try
        {
            created = await members.CreateAsync(dto);
        }
        catch (ClouderyWriteConflictException)
        {
            return ApiError.Result(409, "记录冲突");
        }

        return Created(Url.LowercaseActionUrl("GetClouderyMember", new { id = created.Id }), created);
    }

    [HttpDelete("{id}")]
    [AdminOnly]
    public async Task<IActionResult> DeleteClouderyMember(string id)
    {
        return await members.DeleteAsync(id) ? NoContent() : ApiError.Result(404, "记录不存在");
    }
}
