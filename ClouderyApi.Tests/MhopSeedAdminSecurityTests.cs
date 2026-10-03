using System.Net;
using System.Text;
using System.Text.Json;
using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Infrastructure;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// 锁定种子管理员账号的安全属性：口令不得是仓库里的公开字面量，且必须能真实登录。
/// <para>
/// 历史事故：种子硬编码 admin123 / 1234567 两个超管口令。仓库一旦公开，这些口令
/// 即等同于无口令——任何人都能爆破后台。这里的测试让「弱口令回归」直接失败。
/// </para>
/// </summary>
public sealed class MhopSeedAdminSecurityTests : IntegrationTestBase
{
    /// <summary>曾经出现在源码里的弱口令，出现即视为回归。</summary>
    private static readonly string[] KnownWeakPasswords = ["admin123", "1234567", "admin", "password", "123456"];

    [Fact]
    public async Task Seeded_superadmin_password_is_not_a_known_weak_literal()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
        var admin = await db.MhopUsers.SingleAsync(u => u.Username == ClouderyApiFactory.TestSeedAdminUsername);

        Assert.Equal(MhopUserRole.SuperAdmin, admin.Role);
        Assert.NotEmpty(admin.PasswordHash);

        // 直接登录逐个试：弱口令能登录成功即回归。
        foreach (var weak in KnownWeakPasswords)
        {
            Assert.False(Verify(weak, admin.PasswordHash),
                $"种子超管使用了已知弱口令 {weak}——源码里的固定口令等同无口令");
        }
    }

    [Fact]
    public async Task Seed_does_not_create_a_second_hardcoded_superadmin()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();

        // 曾经存在 root/1234567 这个"应急超管"；同一份种子不应再凭空造出弱口令超管。
        var superAdmins = await db.MhopUsers
            .Where(u => u.Role == MhopUserRole.SuperAdmin)
            .Select(u => u.Username)
            .ToListAsync();

        Assert.Equal(new[] { ClouderyApiFactory.TestSeedAdminUsername }, superAdmins);
    }

    [Fact]
    public async Task Seeded_superadmin_can_log_in_with_configured_password()
    {
        var response = await Client.PostAsync("/mhop/auth/login",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    username = ClouderyApiFactory.TestSeedAdminUsername,
                    password = ClouderyApiFactory.TestSeedAdminPassword,
                }),
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("access_token").GetString()));
    }

    /// <summary>
    /// 直接调用生产哈希实现验证口令，避免测试里另写一份 PBKDF2 而与生产算法漂移。
    /// </summary>
    private static bool Verify(string password, string hash)
    {
        var hasher = new MhopPasswordHasher();
        return hasher.Verify(password, hash);
    }
}