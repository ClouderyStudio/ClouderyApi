using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// 守护漂流瓶 Status 的 EF sentinel 语义（docs/DDD-REFACTOR-PLAN.md 附录 D）：
/// MhopBottleStatus.Pending = 0 恰是 int 的 CLR 默认值，而该列配置了数据库默认值 Drifting(1)。
/// 若不显式给属性设置 sentinel，EF 会把「Status = Pending」当作「未赋值」，INSERT 时省略 status 列，
/// 数据库默认值 1 生效 —— 投瓶会直接以「漂流中」入库，MhopContentReviewService 的
/// 「只处理待审核」判断立即早退，投瓶的 AI 初筛从未真正执行。
/// </summary>
public sealed class MhopBottlePersistenceTests : IntegrationTestBase
{
    [Fact]
    public async Task Pending_bottle_status_survives_round_trip()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int bottleId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            var bottle = new MhopBottle
            {
                UserId = 7,
                Content = "sentinel 守护：待审核状态必须落库",
                Status = MhopBottleStatus.Pending,
                Crisis = false,
                LastMessageAt = now,
                CreatedAt = now,
                ThrowerLastReadAt = now,
            };
            db.MhopBottles.Add(bottle);
            await db.SaveChangesAsync();
            bottleId = bottle.Id;
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            var persisted = await db.MhopBottles.AsNoTracking().SingleAsync(b => b.Id == bottleId);
            Assert.Equal(MhopBottleStatus.Pending, persisted.Status);
        }
    }

    [Fact]
    public void Status_sentinel_is_not_the_clr_default()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
        var status = db.Model.FindEntityType(typeof(MhopBottle))!.FindProperty(nameof(MhopBottle.Status))!;
        Assert.Equal(-1, status.Sentinel);
    }
}
