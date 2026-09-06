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
public class CommentController : ControllerBase
{
    private readonly QisoulDbContext _context;
    private readonly ILogger<CommentController> _logger;

    public CommentController(QisoulDbContext context, ILogger<CommentController> logger)
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

    [HttpGet("post/{postId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCommentsByPost(Guid postId)
    {
        try
        {
            var comments = await _context.Comments
                .Include(c => c.User)
                .Include(c => c.Replies)
                    .ThenInclude(r => r.User)
                .Where(c => c.PostId == postId && c.ParentId == null)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new CommentResponseDto
                {
                    Id = c.Id,
                    Content = c.Content,
                    Likes = c.Likes,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    Username = c.User != null ? c.User.Username : "匿名用户",
                    UserAvatar = c.User != null ? c.User.Avatar : null,
                    ParentId = c.ParentId,
                    ReplyCount = c.Replies.Count,
                    Replies = c.Replies.OrderBy(r => r.CreatedAt).Select(r => new CommentResponseDto
                    {
                        Id = r.Id,
                        Content = r.Content,
                        Likes = r.Likes,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt,
                        Username = r.User != null ? r.User.Username : "匿名用户",
                        UserAvatar = r.User != null ? r.User.Avatar : null,
                        ParentId = r.ParentId,
                    }).ToList()
                })
                .ToListAsync();

            return Ok(new { success = true, data = comments });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取评论失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetComment(Guid id)
    {
        try
        {
            var comment = await _context.Comments
                .Include(c => c.User)
                .Include(c => c.Replies)
                    .ThenInclude(r => r.User)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (comment == null)
                return NotFound(new { success = false, message = "评论不存在" });

            return Ok(new
            {
                success = true,
                data = new CommentResponseDto
                {
                    Id = comment.Id,
                    Content = comment.Content,
                    Likes = comment.Likes,
                    CreatedAt = comment.CreatedAt,
                    UpdatedAt = comment.UpdatedAt,
                    Username = comment.User?.Username ?? "匿名用户",
                    UserAvatar = comment.User?.Avatar,
                    ParentId = comment.ParentId,
                    ReplyCount = comment.Replies.Count,
                    Replies = comment.Replies.OrderBy(r => r.CreatedAt).Select(r => new CommentResponseDto
                    {
                        Id = r.Id,
                        Content = r.Content,
                        Likes = r.Likes,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt,
                        Username = r.User?.Username ?? "匿名用户",
                        UserAvatar = r.User?.Avatar,
                        ParentId = r.ParentId,
                    }).ToList()
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取评论失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateComment([FromBody] CommentDto dto)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "参数校验失败" });

            var userId = GetUserId();

            var post = await _context.Posts.FindAsync(dto.PostId);
            if (post == null)
                return NotFound(new { success = false, message = "帖子不存在" });

            if (dto.ParentId.HasValue)
            {
                var parent = await _context.Comments.FindAsync(dto.ParentId.Value);
                if (parent == null)
                    return NotFound(new { success = false, message = "父评论不存在" });
                if (parent.PostId != dto.PostId)
                    return BadRequest(new { success = false, message = "父评论不属于该帖子" });
            }

            var comment = new Comment
            {
                Id = Guid.NewGuid(),
                PostId = dto.PostId,
                UserId = userId,
                ParentId = dto.ParentId,
                Content = dto.Content,
                CreatedAt = DateTime.UtcNow
            };

            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            post.Comments = await _context.Comments.CountAsync(c => c.PostId == dto.PostId);
            await _context.SaveChangesAsync();

            _logger.LogInformation("用户 {UserId} 评论了帖子 {PostId}", userId, dto.PostId);

            var user = await _context.Users.FindAsync(userId);

            return Ok(new
            {
                success = true,
                message = "评论成功",
                data = new CommentResponseDto
                {
                    Id = comment.Id,
                    Content = comment.Content,
                    Likes = comment.Likes,
                    CreatedAt = comment.CreatedAt,
                    Username = user?.Username ?? "匿名用户",
                    UserAvatar = user?.Avatar,
                    ParentId = comment.ParentId,
                }
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建评论失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpPost("{id}/like")]
    public async Task<IActionResult> LikeComment(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var comment = await _context.Comments.FindAsync(id);
            if (comment == null)
                return NotFound(new { success = false, message = "评论不存在" });

            var existing = await _context.UserLikes.FirstOrDefaultAsync(l =>
                l.UserId == userId && l.TargetType == "comment" && l.TargetId == id);

            if (existing == null)
            {
                _context.UserLikes.Add(new UserLike
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    TargetType = "comment",
                    TargetId = id,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                _context.UserLikes.Remove(existing);
            }
            await _context.SaveChangesAsync();

            comment.Likes = await _context.UserLikes.CountAsync(l => l.TargetType == "comment" && l.TargetId == id);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, liked = existing == null, likes = comment.Likes });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "点赞评论失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteComment(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var comment = await _context.Comments
                .Include(c => c.Replies)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (comment == null)
                return NotFound(new { success = false, message = "评论不存在" });

            if (comment.UserId != userId)
                return Forbid();

            if (comment.Replies.Any())
            {
                _context.Comments.RemoveRange(comment.Replies);
            }
            _context.Comments.Remove(comment);
            await _context.SaveChangesAsync();

            var post = await _context.Posts.FindAsync(comment.PostId);
            if (post != null)
            {
                post.Comments = await _context.Comments.CountAsync(c => c.PostId == comment.PostId);
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("用户 {UserId} 删除了评论 {CommentId}", userId, id);

            return Ok(new { success = true, message = "删除成功" });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除评论失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }
}
