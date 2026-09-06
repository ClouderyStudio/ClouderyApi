using ClouderyApi.Data;
using ClouderyApi.Models.Qisoul;
using ClouderyApi.Models.Qisoul.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace ClouderyApi.Controllers.Qisoul;

[ApiController]
[Route("qisoul/[controller]")]
[Authorize]
public class PostController : ControllerBase
{
    private readonly QisoulDbContext _context;
    private readonly ILogger<PostController> _logger;

    public PostController(QisoulDbContext context, ILogger<PostController> logger)
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
    public async Task<IActionResult> GetPosts(
        [FromQuery] string? category = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            if (page < 1) page = 1;
            pageSize = Math.Clamp(pageSize, 1, 50);

            var query = _context.Posts
                .Include(p => p.User)
                .OrderByDescending(p => p.CreatedAt)
                .AsQueryable();

            if (!string.IsNullOrEmpty(category) && category != "all")
                query = query.Where(p => p.Category == category);

            var total = await query.CountAsync();
            var posts = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new PostResponseDto
                {
                    Id = p.Id,
                    Title = p.Title,
                    Content = p.Content,
                    Category = p.Category,
                    Icon = p.Icon,
                    Likes = p.Likes,
                    Comments = p.Comments,
                    CreatedAt = p.CreatedAt,
                    Username = p.User != null ? p.User.Username : "匿名用户",
                    UserAvatar = p.User != null ? p.User.Avatar : null
                })
                .ToListAsync();

            return Ok(new
            {
                success = true,
                data = posts,
                pagination = new { page, pageSize, total, totalPages = (int)Math.Ceiling((double)total / pageSize) }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取帖子列表失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpGet("categories")]
    [AllowAnonymous]
    public IActionResult GetCategories()
    {
        var categories = new[]
        {
            new { value = "all", label = "全部", icon = "✦" },
            new { value = "心理调节", label = "心理调节", icon = "🧠" },
            new { value = "生活习惯", label = "生活习惯", icon = "🌱" },
            new { value = "社交支持", label = "社交支持", icon = "🤝" },
            new { value = "专业帮助", label = "专业帮助", icon = "💼" },
            new { value = "日常小技巧", label = "日常小技巧", icon = "✨" }
        };

        return Ok(new { success = true, data = categories });
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPost(Guid id)
    {
        try
        {
            var post = await _context.Posts
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (post == null)
                return NotFound(new { success = false, message = "帖子不存在" });

            return Ok(new
            {
                success = true,
                data = new PostResponseDto
                {
                    Id = post.Id,
                    Title = post.Title,
                    Content = post.Content,
                    Category = post.Category,
                    Icon = post.Icon,
                    Likes = post.Likes,
                    Comments = post.Comments,
                    CreatedAt = post.CreatedAt,
                    Username = post.User?.Username ?? "匿名用户",
                    UserAvatar = post.User?.Avatar
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取帖子失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreatePost([FromBody] PostDto dto)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "参数校验失败" });

            var userId = GetUserId();

            var post = new Post
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = dto.Title,
                Content = dto.Content,
                Category = dto.Category,
                Icon = dto.Icon ?? "📖",
                CreatedAt = DateTime.UtcNow
            };

            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            _logger.LogInformation("用户 {UserId} 创建了帖子 {PostId}", userId, post.Id);

            return Ok(new
            {
                success = true,
                message = "发布成功",
                data = new PostResponseDto
                {
                    Id = post.Id,
                    Title = post.Title,
                    Content = post.Content,
                    Category = post.Category,
                    Icon = post.Icon,
                    Likes = post.Likes,
                    Comments = post.Comments,
                    CreatedAt = post.CreatedAt
                }
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建帖子失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpPost("{id}/like")]
    public async Task<IActionResult> LikePost(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var post = await _context.Posts.FindAsync(id);
            if (post == null)
                return NotFound(new { success = false, message = "帖子不存在" });

            var existing = await _context.UserLikes.FirstOrDefaultAsync(l =>
                l.UserId == userId && l.TargetType == "post" && l.TargetId == id);

            if (existing == null)
            {
                _context.UserLikes.Add(new UserLike
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    TargetType = "post",
                    TargetId = id,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                _context.UserLikes.Remove(existing);
            }
            await _context.SaveChangesAsync();

            post.Likes = await _context.UserLikes.CountAsync(l => l.TargetType == "post" && l.TargetId == id);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, liked = existing == null, likes = post.Likes });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "点赞帖子失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePost(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var post = await _context.Posts
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (post == null)
                return NotFound(new { success = false, message = "帖子不存在或无权限" });

            _context.Posts.Remove(post);
            await _context.SaveChangesAsync();

            _logger.LogInformation("用户 {UserId} 删除了帖子 {PostId}", userId, id);

            return Ok(new { success = true, message = "删除成功" });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除帖子失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePost(Guid id, [FromBody] UpdatePostDto dto)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "参数校验失败" });

            var userId = GetUserId();
            var post = await _context.Posts
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (post == null)
                return NotFound(new { success = false, message = "帖子不存在或无权限" });

            post.Title = dto.Title;
            post.Content = dto.Content;
            post.Category = dto.Category;
            post.Icon = dto.Icon ?? post.Icon;
            post.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("用户 {UserId} 更新了帖子 {PostId}", userId, id);

            return Ok(new
            {
                success = true,
                message = "更新成功",
                data = new PostResponseDto
                {
                    Id = post.Id,
                    Title = post.Title,
                    Content = post.Content,
                    Category = post.Category,
                    Icon = post.Icon,
                    Likes = post.Likes,
                    Comments = post.Comments,
                    CreatedAt = post.CreatedAt,
                    UpdatedAt = post.UpdatedAt,
                    Username = post.User?.Username
                }
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新帖子失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    public class UpdatePostDto
    {
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(10000)]
        public string Content { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Icon { get; set; }
    }
}
