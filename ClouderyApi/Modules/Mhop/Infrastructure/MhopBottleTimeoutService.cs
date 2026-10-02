using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ClouderyApi.Modules.Mhop.Application;

namespace ClouderyApi.Modules.Mhop.Infrastructure;

/// <summary>
/// 漂流瓶兜底任务（每小时一轮）：
/// 1. 把「对话中但 7 天无新消息」的会话置为已结束（正常访问时 BottleAppService.ApplyTimeoutAsync 会惰性关闭）；
/// 2. 把「一直在待审核、却始终没拿到 AI 结论」的瓶子重新排队审核（进程中断等异常导致的审核任务丢失）。
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
                var bottleService = scope.ServiceProvider.GetRequiredService<BottleAppService>();
                var closed = await bottleService.ApplyTimeoutAsync();
                if (closed > 0)
                    logger.LogInformation("漂流瓶超时任务：自动结束 {Count} 个 7 天无消息的会话", closed);

                // 审核任务丢失的兜底：10 分钟仍无任何结论的待审瓶子重新排队
                var review = scope.ServiceProvider.GetRequiredService<MhopContentReviewService>();
                await review.RequeueStaleBottleReviewsAsync(TimeSpan.FromMinutes(10));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "漂流瓶超时任务执行失败");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
