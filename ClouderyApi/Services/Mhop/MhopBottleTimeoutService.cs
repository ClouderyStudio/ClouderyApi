using ClouderyApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 漂流瓶会话超时兜底：每小时把「对话中但 7 天无新消息」的会话置为已结束。
/// 正常访问时 MhopBottleService.ApplyTimeoutAsync 会惰性关闭，本服务只做最终兜底。
/// </summary>
public sealed class MhopBottleTimeoutService(
    IServiceScopeFactory scopeFactory,
    ILogger<MhopBottleTimeoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 延后 2 分钟启动，避开服务启动高峰
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
                var bottleService = scope.ServiceProvider.GetRequiredService<MhopBottleService>();
                var closed = await bottleService.ApplyTimeoutAsync();
                if (closed > 0)
                    logger.LogInformation("漂流瓶超时任务：自动结束 {Count} 个 7 天无消息的会话", closed);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "漂流瓶超时任务执行失败");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
