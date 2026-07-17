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
public class MoodController : ControllerBase
{
    private readonly QisoulDbContext _context;
    private readonly ILogger<MoodController> _logger;

    public MoodController(QisoulDbContext context, ILogger<MoodController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ====== 获取用户 ID ======
    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim))
            throw new UnauthorizedAccessException("用户未登录");

        return Guid.Parse(userIdClaim);
    }

    // ====== 获取心情记录列表 ======
    [HttpGet]
    public async Task<IActionResult> GetRecords(
        [FromQuery] int days = 30,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var userId = GetUserId();
            var query = _context.MoodRecords
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.RecordDate);

            // 日期范围过滤
            if (startDate.HasValue)
                query = (IOrderedQueryable<MoodRecord>)query.Where(m => m.RecordDate >= startDate.Value);
            if (endDate.HasValue)
                query = (IOrderedQueryable<MoodRecord>)query.Where(m => m.RecordDate <= endDate.Value);
            else
                query = (IOrderedQueryable<MoodRecord>)query.Where(m => m.RecordDate >= DateTime.Now.AddDays(-days));

            var records = await query
                .Select(m => new MoodRecordResponseDto
                {
                    Id = m.Id,
                    MoodType = m.MoodType,
                    MoodLabel = m.MoodLabel,
                    Intensity = m.Intensity,
                    Note = m.Note,
                    Diary = m.Diary,
                    Tags = m.Tags,
                    RecordDate = m.RecordDate,
                    CreatedAt = m.CreatedAt,
                    Username = m.User != null ? m.User.Username : null
                })
                .ToListAsync();

            return Ok(new { success = true, data = records });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取心情记录失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    // ====== 获取单条心情记录 ======
    [HttpGet("{id}")]
    public async Task<IActionResult> GetRecord(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var record = await _context.MoodRecords
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);

            if (record == null)
                return NotFound(new { success = false, message = "记录不存在" });

            return Ok(new
            {
                success = true,
                data = new MoodRecordResponseDto
                {
                    Id = record.Id,
                    MoodType = record.MoodType,
                    MoodLabel = record.MoodLabel,
                    Intensity = record.Intensity,
                    Note = record.Note,
                    Diary = record.Diary,
                    Tags = record.Tags,
                    RecordDate = record.RecordDate,
                    CreatedAt = record.CreatedAt,
                    Username = record.User?.Username
                }
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取心情记录失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    // ====== 创建心情记录 ======
    [HttpPost]
    public async Task<IActionResult> CreateRecord([FromBody] MoodRecordDto dto)
    {
        try
        {
            var userId = GetUserId();

            var record = new MoodRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                MoodType = dto.MoodType,
                MoodLabel = dto.MoodLabel ?? GetMoodLabel(dto.MoodType),
                Intensity = dto.Intensity,
                Note = dto.Note,
                Diary = dto.Diary,
                Tags = dto.Tags,
                RecordDate = dto.RecordDate ?? DateTime.Now,
                CreatedAt = DateTime.Now
            };

            _context.MoodRecords.Add(record);
            await _context.SaveChangesAsync();

            _logger.LogInformation("用户 {UserId} 创建了心情记录 {RecordId}", userId, record.Id);

            return Ok(new
            {
                success = true,
                message = "记录保存成功",
                data = new MoodRecordResponseDto
                {
                    Id = record.Id,
                    MoodType = record.MoodType,
                    MoodLabel = record.MoodLabel,
                    Intensity = record.Intensity,
                    Note = record.Note,
                    Diary = record.Diary,
                    Tags = record.Tags,
                    RecordDate = record.RecordDate,
                    CreatedAt = record.CreatedAt
                }
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建心情记录失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    // ====== 更新心情记录 ======
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateRecord(Guid id, [FromBody] MoodRecordDto dto)
    {
        try
        {
            var userId = GetUserId();
            var record = await _context.MoodRecords
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);

            if (record == null)
                return NotFound(new { success = false, message = "记录不存在" });

            // 更新字段
            record.MoodType = dto.MoodType;
            record.MoodLabel = dto.MoodLabel ?? GetMoodLabel(dto.MoodType);
            record.Intensity = dto.Intensity;
            record.Note = dto.Note;
            record.Diary = dto.Diary;
            record.Tags = dto.Tags;
            if (dto.RecordDate.HasValue)
                record.RecordDate = dto.RecordDate.Value;

            await _context.SaveChangesAsync();

            _logger.LogInformation("用户 {UserId} 更新了心情记录 {RecordId}", userId, id);

            return Ok(new { success = true, message = "更新成功" });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新心情记录失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    // ====== 删除心情记录 ======
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRecord(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var record = await _context.MoodRecords
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);

            if (record == null)
                return NotFound(new { success = false, message = "记录不存在" });

            _context.MoodRecords.Remove(record);
            await _context.SaveChangesAsync();

            _logger.LogInformation("用户 {UserId} 删除了心情记录 {RecordId}", userId, id);

            return Ok(new { success = true, message = "删除成功" });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除心情记录失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    // ====== 辅助方法 ======
    private string GetMoodLabel(string moodType)
    {
        return moodType switch
        {
            "happy" => "开心",
            "calm" => "平静",
            "grateful" => "感恩",
            "excited" => "兴奋",
            "neutral" => "一般",
            "tired" => "疲惫",
            "sad" => "难过",
            "anxious" => "焦虑",
            "angry" => "愤怒",
            "lonely" => "孤独",
            _ => moodType
        };
    }
}