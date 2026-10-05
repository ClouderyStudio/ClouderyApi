using System.Security.Claims;
using System.Text;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Domain;
using ClouderyApi.Modules.Scforge.Infrastructure;
using ClouderyApi.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClouderyApi.Tests;

/// <summary>
/// API Key 领域规则与认证通道的单元测试。
///
/// **不继承 <see cref="IntegrationTestBase"/>**：本类刻意只测不需要数据库的部分
/// （作用域集合、令牌哈希、作用域守卫、状态判定），因此在没配 MySQL 的机器上也能跑。
/// 端到端的签发 / 吊销 / 发布流程需要真实 MySQL，尚未纳入自动化测试。
/// </summary>
public sealed class ScforgeApiKeyDomainTests
{
    /* ---------------- 作用域集合 ---------------- */

    [Fact]
    public void Scopes_From_drops_unknown_and_dedupes()
    {
        var result = ScforgeApiKeyScopes.From(["publish", "publish", "不存在的码", "read"]);

        Assert.Equal(["publish", "read"], result);
    }

    [Fact]
    public void Scopes_Parse_invalid_json_yields_empty()
    {
        // 非法 JSON 必须退化成"无任何权限"，而不是抛异常把整条链路带崩。
        Assert.Empty(ScforgeApiKeyScopes.Parse("{ 不是 json"));
        Assert.Empty(ScforgeApiKeyScopes.Parse(""));
        Assert.Empty(ScforgeApiKeyScopes.Parse(null));
    }

    [Fact]
    public void Scopes_Roundtrip_survives_serialize_and_parse()
    {
        var original = ScforgeApiKeyScopes.From([ScforgeApiKeyScopes.Read, ScforgeApiKeyScopes.Manage]);
        var restored = ScforgeApiKeyScopes.Parse(ScforgeApiKeyScopes.Serialize(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void SelfService_excludes_manage()
    {
        // 「管理」作用域不能自助申请 —— 能在后台签发它的只有超管。
        Assert.DoesNotContain(ScforgeApiKeyScopes.Manage, ScforgeApiKeyScopes.SelfService);
        Assert.Contains(ScforgeApiKeyScopes.Publish, ScforgeApiKeyScopes.SelfService);
    }

    /* ---------------- 令牌哈希 ---------------- */

    [Fact]
    public void HashToken_is_deterministic_and_url_safe()
    {
        var hash = ScforgeApiKeyScopes.HashToken("scf_example");

        Assert.Equal(hash, ScforgeApiKeyScopes.HashToken("scf_example"));
        // base64url：不能含 + / =，否则拼进 URL 或日志会出问题。
        Assert.DoesNotContain('+', hash);
        Assert.DoesNotContain('/', hash);
        Assert.DoesNotContain('=', hash);
    }

    [Fact]
    public void HashToken_differs_for_different_tokens()
    {
        Assert.NotEqual(
            ScforgeApiKeyScopes.HashToken("scf_a"),
            ScforgeApiKeyScopes.HashToken("scf_b"));
    }

    /* ---------------- 作用域守卫 ---------------- */

    private static ScforgeApiKeyAccessor CreateAccessor(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "ScforgeApiKey");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = new HttpContextAccessor { HttpContext = context };
        return new ScforgeApiKeyAccessor(accessor);
    }

    private static Claim[] ApiKeyClaims(params string[] scopes) =>
    [
        new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new(ClaimTypes.Name, "key-user"),
        new(ScforgeApiKeyClaim.Method, "api-key"),
        new(ScforgeApiKeyClaim.Scopes, string.Join(',', scopes)),
    ];

    [Fact]
    public void Accessor_Key_with_scope_is_allowed()
    {
        var accessor = CreateAccessor(ApiKeyClaims(ScforgeApiKeyScopes.Publish));

        Assert.True(accessor.IsApiKey);
        Assert.True(accessor.Allows(ScforgeApiKeyScopes.Publish));
        Assert.False(accessor.Allows(ScforgeApiKeyScopes.Manage));
    }

    [Fact]
    public void Accessor_Key_without_scope_throws_403()
    {
        var accessor = CreateAccessor(ApiKeyClaims(ScforgeApiKeyScopes.Read));

        // 身份有效但权限不足 ⇒ 403 而非 401：脚本据此能区分"密钥失效"和"范围不够"。
        var ex = Assert.Throws<ScforgeApiException>(() => accessor.Require(ScforgeApiKeyScopes.Publish));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public void Accessor_cookie_session_bypasses_scope_check()
    {
        // 网页后台操作不受作用域限制 —— 作用域只约束机器凭据。
        var cookieClaims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "cookie-user"),
        };
        var accessor = CreateAccessor(cookieClaims);

        Assert.False(accessor.IsApiKey);
        Assert.True(accessor.Allows(ScforgeApiKeyScopes.Publish));
        Assert.True(accessor.Allows(ScforgeApiKeyScopes.Manage));

        // 不该因为是 Cookie 会话就抛。
        accessor.Require(ScforgeApiKeyScopes.Manage);
    }

    [Fact]
    public void Accessor_anonymous_treats_as_full_scope()
    {
        // 匿名请求走的是各端点自己的 401 判定，这里不越权也不误伤。
        var accessor = CreateAccessor();

        Assert.False(accessor.IsApiKey);
        Assert.True(accessor.Allows(ScforgeApiKeyScopes.Read));
    }

    /* ---------------- 状态判定 ---------------- */

    [Fact]
    public void Describe_marks_revoked_and_expired_keys()
    {
        var fresh = new ScforgeApiKey { CreatedAt = DateTime.UtcNow };
        Assert.True(ScforgeApiKeyAppService.Describe(fresh).Usable);
        Assert.Equal("有效", ScforgeApiKeyAppService.Describe(fresh).Status);

        var revoked = new ScforgeApiKey { RevokedAt = DateTime.UtcNow };
        Assert.False(ScforgeApiKeyAppService.Describe(revoked).Usable);
        Assert.Equal("已吊销", ScforgeApiKeyAppService.Describe(revoked).Status);

        var expired = new ScforgeApiKey { ExpiresAt = DateTime.UtcNow.AddMinutes(-1) };
        Assert.False(ScforgeApiKeyAppService.Describe(expired).Usable);
        Assert.Equal("已过期", ScforgeApiKeyAppService.Describe(expired).Status);
    }

    [Fact]
    public void Token_prefix_is_shared_between_signing_and_verifying()
    {
        // 签发方（Application）与校验方（Infrastructure）必须认同一个前缀，否则发出去的 Key 永远校验不过。
        Assert.Equal("scf_", ScforgeApiKeyScopes.TokenPrefix);
        Assert.Equal(ScforgeApiKeyScopes.TokenPrefix, ScforgeApiKeyMiddleware.TokenPrefix);
    }

    /* ---------------- 中间件：查库失败必须降级为匿名 ---------------- */

    [Fact]
    public async Task Middleware_db_failure_degrades_to_anonymous_instead_of_throwing()
    {
        // 回归测试：scforge_api_keys 表不存在时，EF 在碰 DbSet 时抛异常。
        // 该中间件在管道早期且覆盖所有端点，一旦让它把异常抛出去，
        // 任意伪造的 Authorization 头就能把全站公开接口打成 500（可匿名触发的放大故障）。
        // 正确行为：记警告、以匿名身份继续，由端点按既有约定返回 401。
        var reachedNext = false;
        var middleware = new ScforgeApiKeyMiddleware(
            (HttpContext _) => { reachedNext = true; return Task.CompletedTask; });

        var context = new DefaultHttpContext();
        // 长度 >= 20 才会走到查库分支（更短的令牌被中间件直接判为无效，省掉一次查库）。
        context.Request.Headers.Authorization = "Bearer " + ScforgeApiKeyScopes.TokenPrefix + new string('x', 40);

        await middleware.InvokeAsync(context, new ThrowingScforgeDbContext(), NullLogger<ScforgeApiKeyMiddleware>.Instance);

        Assert.True(reachedNext, "请求必须继续走到下一个中间件，而不是因异常变成 500");
        Assert.False(context.User.Identity?.IsAuthenticated ?? false, "查库失败必须降级为匿名（fail-closed）");
    }

    /// <summary>模拟"表未迁移"：任何触碰 DbSet 的操作都抛，贴近 EF 真实行为。</summary>
    private sealed class ThrowingScforgeDbContext : IScforgeDbContext
    {
        public DbSet<ScforgeApiKey> ScforgeApiKeys => throw new InvalidOperationException("Table 'api.scforge_api_keys' doesn't exist");
        public DbSet<ScforgePlugin> ScforgePlugins => throw new NotSupportedException();
        public DbSet<ScforgeVersion> ScforgeVersions => throw new NotSupportedException();
        public DbSet<ScforgeComment> ScforgeComments => throw new NotSupportedException();
        public DbSet<ScforgeVote> ScforgeVotes => throw new NotSupportedException();
        public DbSet<ScforgeAdmin> ScforgeAdmins => throw new NotSupportedException();
        public DbSet<ScforgeGameVersion> ScforgeGameVersions => throw new NotSupportedException();
        public DbSet<ScforgeAccessGrant> ScforgeAccessGrants => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
