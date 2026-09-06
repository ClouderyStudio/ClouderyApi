using ClouderyApi.Data;
using ClouderyApi.Models.Qisoul;
using ClouderyApi.Models.Qisoul.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ClouderyApi.Controllers.Qisoul;

[ApiController]
[Route("qisoul/[controller]")]
[Authorize]
public class StickyController : ControllerBase
{
    private readonly QisoulDbContext _context;
    private readonly ILogger<StickyController> _logger;

    public StickyController(QisoulDbContext context, ILogger<StickyController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException("用户未登录或标识无效");
        return userId;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetStickies([FromQuery] int limit = 20)
    {
        try
        {
            limit = Math.Clamp(limit, 1, 200);
            var stickies = await _context.Stickies
                .Include(s => s.User)
                .OrderByDescending(s => s.CreatedAt)
                .Take(limit)
                .Select(s => new StickyResponseDto
                {
                    Id = s.Id,
                    Content = s.Content,
                    Icon = s.Icon,
                    Color = s.Color,
                    Likes = s.Likes,
                    CreatedAt = s.CreatedAt,
                    Username = s.User != null ? s.User.Username : "匿名用户",
                    UserAvatar = s.User != null ? s.User.Avatar : null
                })
                .ToListAsync();

            return Ok(new { success = true, data = stickies });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取便签列表失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateSticky([FromBody] StickyDto dto)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "参数校验失败" });

            var userId = GetUserId();

            var sticky = new Sticky
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Content = dto.Content,
                Icon = dto.Icon ?? "📌",
                Color = dto.Color ?? "rgba(236, 227, 219, 0.45)",
                CreatedAt = DateTime.UtcNow
            };

            _context.Stickies.Add(sticky);
            await _context.SaveChangesAsync();

            _logger.LogInformation("用户 {UserId} 创建了便签 {StickyId}", userId, sticky.Id);

            return Ok(new
            {
                success = true,
                message = "便签发布成功",
                data = new StickyResponseDto
                {
                    Id = sticky.Id,
                    Content = sticky.Content,
                    Icon = sticky.Icon,
                    Color = sticky.Color,
                    Likes = sticky.Likes,
                    CreatedAt = sticky.CreatedAt
                }
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建便签失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpPost("{id}/like")]
    public async Task<IActionResult> LikeSticky(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var sticky = await _context.Stickies.FindAsync(id);
            if (sticky == null)
                return NotFound(new { success = false, message = "便签不存在" });

            var existing = await _context.UserLikes.FirstOrDefaultAsync(l =>
                l.UserId == userId && l.TargetType == "sticky" && l.TargetId == id);

            if (existing == null)
            {
                _context.UserLikes.Add(new UserLike
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    TargetType = "sticky",
                    TargetId = id,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                _context.UserLikes.Remove(existing);
            }
            await _context.SaveChangesAsync();

            sticky.Likes = await _context.UserLikes.CountAsync(l => l.TargetType == "sticky" && l.TargetId == id);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, liked = existing == null, likes = sticky.Likes });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "点赞便签失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSticky(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var sticky = await _context.Stickies
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

            if (sticky == null)
                return NotFound(new { success = false, message = "便签不存在或无权限" });

            _context.Stickies.Remove(sticky);
            await _context.SaveChangesAsync();

            _logger.LogInformation("用户 {UserId} 删除了便签 {StickyId}", userId, id);

            return Ok(new { success = true, message = "删除成功" });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除便签失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }
}
