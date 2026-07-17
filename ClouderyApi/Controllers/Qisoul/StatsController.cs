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
public class StatsController : ControllerBase
{
    private readonly QisoulDbContext _context;
    private readonly ILogger<StatsController> _logger;

    public StatsController(QisoulDbContext context, ILogger<StatsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim))
            throw new UnauthorizedAccessException("用户未登录");
        return Guid.Parse(userIdClaim);
    }

    // ====== 获取统计数据 ======
    [HttpGet]
    public async Task<IActionResult> GetStats(
        [FromQuery] int days = 30,
        [FromQuery] string? view = "week") // week, month
    {
        try
        {
            var userId = GetUserId();
            var endDate = DateTime.Now.Date;
            var startDate = view == "week"
                ? endDate.AddDays(-7)
                : endDate.AddDays(-30);

            // 所有记录
            var records = await _context.MoodRecords
                .Where(m => m.UserId == userId && m.RecordDate >= startDate && m.RecordDate <= endDate)
                .ToListAsync();

            // 总记录数
            var totalRecords = await _context.MoodRecords
                .CountAsync(m => m.UserId == userId);

            // 记录天数
            var totalDays = await _context.MoodRecords
                .Where(m => m.UserId == userId)
                .Select(m => m.RecordDate.Date)
                .Distinct()
                .CountAsync();

            // 今日心情
            var todayMood = await _context.MoodRecords
                .Where(m => m.UserId == userId && m.RecordDate.Date == endDate)
                .OrderByDescending(m => m.RecordDate)
                .Select(m => m.MoodLabel)
                .FirstOrDefaultAsync();

            // 连续记录天数
            var streak = await CalculateStreak(userId);

            // 趋势数据
            var trends = records
                .GroupBy(m => m.RecordDate.Date)
                .Select(g => new MoodTrendDto
                {
                    Date = g.Key.ToString("MM-dd"),
                    AvgIntensity = g.Average(m => m.Intensity),
                    MoodType = g.Count() > 0 ? g.Last().MoodType : null
                })
                .OrderBy(t => t.Date)
                .ToList();

            // 情绪分布
            var distribution = records
                .GroupBy(m => m.MoodType)
                .Select(g => new MoodDistributionDto
                {
                    MoodType = g.Key,
                    MoodLabel = g.First().MoodLabel,
                    Count = g.Count()
                })
                .ToList();

            return Ok(new
            {
                success = true,
                data = new StatsResponseDto
                {
                    TotalDays = totalDays,
                    TotalRecords = totalRecords,
                    TodayMood = todayMood ?? "未记录",
                    Streak = streak,
                    Trends = trends,
                    Distribution = distribution
                }
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { success = false, message = "请先登录" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取统计数据失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    // ====== 计算连续记录天数 ======
    private async Task<int> CalculateStreak(Guid userId)
    {
        var records = await _context.MoodRecords
            .Where(m => m.UserId == userId)
            .Select(m => m.RecordDate.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();

        if (records.Count == 0) return 0;

        var streak = 0;
        var current = DateTime.Now.Date;

        foreach (var date in records)
        {
            if (date == current)
            {
                streak++;
                current = current.AddDays(-1);
            }
            else if (date == current.AddDays(1))
            {
                // 昨天有记录，继续
                continue;
            }
            else
            {
                break;
            }
        }

        return streak;
    }
}