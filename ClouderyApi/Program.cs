using ClouderyApi.Shared.Domain;
using ClouderyApi.Shared.Exceptions;
using Casdoor.AspNetCore.Authentication;
using ClouderyApi.Modules.Identity.Infrastructure.Persistence;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Shared.Ai;
using ClouderyApi.Shared.Authorization;
using ClouderyApi.Shared.Options;
using ClouderyApi.Modules.Cloudery.Application;
using ClouderyApi.Modules.Cloudery.Infrastructure.Persistence;
using ClouderyApi.Modules.Mhop.Infrastructure;
using ClouderyApi.Modules.Identity.Application;
using ClouderyApi.Modules.Mhop.Application;
using ClouderyApi.Modules.Mhop.Application.Events;
using ClouderyApi.Modules.Mhop.Domain.Events;
using ClouderyApi.Modules.Zhuxs.Application;
using ClouderyApi.Modules.Zhuxs.Infrastructure.Persistence;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Infrastructure;
using ClouderyApi.Modules.Scforge.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using ClouderyApi.Shared.Directory;
using ClouderyApi.Shared.Filters;
using ClouderyApi.Shared.RateLimit;
using ClouderyApi.Shared.Online;
using ClouderyApi.Shared.Email;
using ClouderyApi.Shared.Redis;

// 维护开关在交给配置系统之前先摘出来：命令行配置提供程序不接受没有取值的裸开关。
var sweepOrphans = args.Any(a => a.Equals("--sweep-orphans", StringComparison.OrdinalIgnoreCase));
var sweepOrphansDelete = args.Any(a => a.Equals("--delete-orphans", StringComparison.OrdinalIgnoreCase));
var runMigrate = args.Any(a => a.Equals("--migrate", StringComparison.OrdinalIgnoreCase));
var runSeed = args.Any(a => a.Equals("--seed", StringComparison.OrdinalIgnoreCase));
var builder = WebApplication.CreateBuilder(
    args.Where(a => !a.Equals("--sweep-orphans", StringComparison.OrdinalIgnoreCase)
                    && !a.Equals("--delete-orphans", StringComparison.OrdinalIgnoreCase)
                    && !a.Equals("--migrate", StringComparison.OrdinalIgnoreCase)
                    && !a.Equals("--seed", StringComparison.OrdinalIgnoreCase)).ToArray());

builder.Services.AddControllers(options => options.Filters.Add<MhopApiExceptionFilter>());

builder.Services.AddHttpClient("Casdoor"); // 供 AuthController 通过 IHttpClientFactory 使用
builder.Services.AddHttpClient("SckeyServer"); // 供 ServerController 转发 SCKEY 请求

builder.Services.AddOpenApi();

// Stage 5 §5.1 第二步：Cloudery / Zhuxs 各自独立上下文（同库、不同 DbContext）。
// ClouderyContext 沿用既有 __EFMigrationsHistory（3 个既有迁移已记录其中，换新表会被判未应用）；
// ZhuxsContext 的表早于 EF 迁移，改用独立历史表 + 幂等 baseline 迁移。
builder.Services.AddDbContext<ClouderyContext>(options =>
{
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!);
});

builder.Services.AddDbContext<ZhuxsContext>(options =>
{
    options.UseMySQL(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        mySql => mySql.MigrationsHistoryTable(ZhuxsContext.MigrationsHistoryTableName));
});

// 应用服务只依赖 IClouderyDbContext / IZhuxsDbContext（Stage 5 §5.1 第一步），
// 这里把两个边界接口分别绑定到拆分后的独立上下文。
builder.Services.AddScoped<IClouderyDbContext>(sp => sp.GetRequiredService<ClouderyContext>());
builder.Services.AddScoped<IZhuxsDbContext>(sp => sp.GetRequiredService<ZhuxsContext>());

// ===== SCForge 生存战争插件、模组资源平台模块 =====
// 独立上下文 scforge_* 表；与 Cloudery / Identity / Mhop 共用 __EFMigrationsHistory，
// 因此迁移 Id 必须全局唯一（ScforgeInitial 为首次迁移）。
builder.Services.Configure<ScforgeOptions>(builder.Configuration.GetSection(ScforgeOptions.SectionName));
builder.Services.AddDbContext<ScforgeDbContext>(options =>
{
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!);
});
builder.Services.AddScoped<IScforgeDbContext>(sp => sp.GetRequiredService<ScforgeDbContext>());

// 身份直接读 Casdoor Cookie 会话的 Claims（不查库、不签发第二套令牌）。
builder.Services.AddScoped<ScforgeCurrentUser>();

// API Key 通道：把 Authorization: Bearer scf_… 换算成同形状的 Claims，
// 让「作者本人」判定与应用层用例零改动。作用域读取走 Accessor。
builder.Services.AddScoped<ScforgeApiKeyAccessor>();

// 供 `--migrate` 前滚 SCForge 域的迁移（scforge_* 表）。
// 必须注册：否则 --migrate 会在解析该服务时抛 InvalidOperationException 并以退出码 1 中止部署。
builder.Services.AddScoped<ScforgeMaintenanceService>();

// 文件边界：Scforge:Storage:Provider 决定实现。
//   local = 本机磁盘（插件包私有目录 + public/ 下的图片由 /scforge/uploads 静态托管）
//   oss   = 阿里云 OSS（图片公共读外链；插件包保持私有，仍由下载接口流式下发）
builder.Services.AddSingleton<IScforgeFileStore>(sp =>
{
    var fileOptions = sp.GetRequiredService<IOptions<ScforgeOptions>>().Value;
    var provider = (fileOptions.Storage.Provider ?? "local").Trim();
    if (provider.Equals("oss", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("aliyun", StringComparison.OrdinalIgnoreCase))
    {
        return new OssScforgeFileStore(fileOptions, sp.GetRequiredService<ILogger<OssScforgeFileStore>>());
    }

    var contentRoot = sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
    return new LocalScforgeFileStore(
        fileOptions,
        ScforgeUploadPaths.ResolveLocalRoot(fileOptions.UploadDir, contentRoot),
        sp.GetRequiredService<ILogger<LocalScforgeFileStore>>());
});

builder.Services.AddScoped<ScforgePluginAppService>();
builder.Services.AddScoped<ScforgeCommentAppService>();
builder.Services.AddScoped<ScforgeVoteAppService>();

// 后台：管理员权限解析（scforge_admins + 配置白名单引导）与后台用例编排。
builder.Services.AddScoped<ScforgeAdminAccessor>();
builder.Services.AddScoped<ScforgeAdminAppService>();
builder.Services.AddScoped<ScforgeGameVersionAppService>();
builder.Services.AddScoped<ScforgeApiKeyAppService>();

// 跨模块用户目录（Identity 实现）：SCForge 指定管理员时按用户名/邮箱找人。
builder.Services.AddScoped<IUserDirectory, ClouderyApi.Modules.Identity.Application.UserDirectory>();

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!));

// ===== 通用大模型（OpenAI 兼容）配置 =====
// 读根级 Llm 节；某项留空时回退到既有的 Mhop:Llm，因此只为 MHOP 配过模型的现网无需改动。
builder.Services.AddOptions<LlmOptions>()
    .Bind(builder.Configuration.GetSection(LlmOptions.SectionName))
    .PostConfigure(options =>
    {
        var legacy = builder.Configuration.GetSection("Mhop:Llm");
        if (string.IsNullOrWhiteSpace(options.BaseUrl)) options.BaseUrl = legacy["BaseUrl"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(options.ApiKey)) options.ApiKey = legacy["ApiKey"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(options.Model)) options.Model = legacy["Model"] ?? "glm-4-flash";
        if (options.TimeoutSeconds <= 0) options.TimeoutSeconds = 30;
    });
builder.Services.AddSingleton<ILlmClient, LlmClient>();

// 心理学站点的测评结果 AI 解读（公开接口，按 IP 单独限流，见 IpRateLimitAttribute）
builder.Services.AddSingleton<ResultAnalysisService>();

// 心理学站点的测评结果云端存档（登录用户的多平台共享，见 ExamResultsController）
builder.Services.AddScoped<ExamResultService>();

// ===== Stage 2 应用层用例（Cloudery / Zhuxs / Identity） =====
// 控制器只保留 HTTP 绑定，用例编排下沉到 UseCases/<Context>。注册顺序与依赖生命周期：
// 解读/判分这类无状态编排与既有服务保持一致（ResultAnalysisService 为 Singleton）。
builder.Services.AddScoped<MembersAppService>();
builder.Services.AddScoped<ExamPaperAppService>();
builder.Services.AddScoped<ExamResultAppService>();
builder.Services.AddSingleton<ResultAnalysisAppService>();
builder.Services.AddScoped<WhitelistsAppService>();
builder.Services.AddScoped<TermsAppService>();
builder.Services.AddScoped<ApplicationsAppService>();
builder.Services.AddScoped<UserSyncService>();

// ===== MHOP 公益心理辅助平台模块（从 Python FastAPI 后端迁移） =====
builder.Services.Configure<MhopOptions>(builder.Configuration.GetSection(MhopOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<MhopDbContext>(options =>
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!));

// Stage 4 领域事件基座：SaveChanges 提交后进程内派发（选型 A，零迁移）。
builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

// 内容侧订阅者（依赖均为 Singleton）：待审内容排队送 AI 初筛；帖子人工放行时生成 / 沿用 AI 自动回复。
builder.Services.AddSingleton<IDomainEventHandler<PostSubmittedForReview>, PostSubmittedForReviewHandler>();
builder.Services.AddSingleton<IDomainEventHandler<ReplySubmittedForReview>, ReplySubmittedForReviewHandler>();
builder.Services.AddSingleton<IDomainEventHandler<PostPublished>, PostPublishedHandler>();

// 瓶子侧订阅者（依赖均为 Singleton）：投瓶 / 发消息后排队送 AI 初筛。
builder.Services.AddSingleton<IDomainEventHandler<BottleThrown>, BottleThrownHandler>();
builder.Services.AddSingleton<IDomainEventHandler<BottleMessageSent>, BottleMessageSentHandler>();

builder.Services.AddSingleton<MhopPasswordHasher>();

// 数据库维护动作（迁移 / 种子）集中在此服务，供 CLI 与启动期兜底共用。
builder.Services.AddScoped<DatabaseMaintenanceService>();
builder.Services.AddSingleton<IMhopJwtService, MhopJwtService>();
builder.Services.AddSingleton<MhopOnlineTracker>();
builder.Services.AddSingleton<MhopSmtpClient>();
builder.Services.AddSingleton<MhopEmailCodeService>();
builder.Services.AddSingleton<CasdoorAppService>();
builder.Services.AddSingleton<MhopAiService>();
builder.Services.AddSingleton<MhopContentReviewService>();
builder.Services.AddScoped<MhopCurrentUserAccessor>();

// 图片对象存储：local = 本机磁盘（由 /mhop/uploads 静态托管）；oss = 远端阿里云 OSS
builder.Services.AddSingleton<IMhopObjectStorage>(sp =>
{
    var mhopOptions = sp.GetRequiredService<IOptions<MhopOptions>>().Value;
    var provider = mhopOptions.Storage.Provider.Trim();
    if (provider.Equals("oss", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("aliyun", StringComparison.OrdinalIgnoreCase))
    {
        return new MhopAliyunOssStorage(mhopOptions.Storage.Oss, sp.GetRequiredService<ILogger<MhopAliyunOssStorage>>());
    }

    var contentRoot = sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
    return new MhopLocalObjectStorage(MhopUploadPaths.ResolveLocalRoot(mhopOptions.UploadDir, contentRoot));
});
builder.Services.AddScoped<MhopUploadService>();
builder.Services.AddScoped<MhopContentService>();
builder.Services.AddScoped<BottleAppService>();
builder.Services.AddScoped<AssessmentAppService>();
builder.Services.AddScoped<ForumAppService>();
builder.Services.AddScoped<AuthAppService>();
builder.Services.AddScoped<AdminAppService>();
builder.Services.AddHostedService<MhopBottleTimeoutService>();

// ===== 横切配置（Options 模式，见 docs/DDD-STAGE5-CROSS-CUTTING.md §5.2）=====
builder.Services.Configure<CasdoorSettings>(builder.Configuration.GetSection(CasdoorSettings.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.Configure<SckeyOptions>(builder.Configuration.GetSection(SckeyOptions.SectionName));
builder.Services.PostConfigure<SckeyOptions>(options =>
{
    // 旧键名 SurvivalCraft:SCKEY_* 保留为兼容回退；?? 语义只覆盖“键缺失”，显式空串仍是有效取值。
    options.ApiBase ??= builder.Configuration["SurvivalCraft:SCKEY_API_BASE"] ?? "https://api.sckey.net";
    options.BearerToken ??= builder.Configuration["SurvivalCraft:SCKEY_BEARER_TOKEN"] ?? "";
});
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection(CorsSettings.SectionName));

// 限流计数存储：配了 Redis:ConnectionString 就跨实例共享（重启不清零），
// 留空则走进程内实现，本地开发与集成测试不依赖任何外部服务。
builder.Services.AddRateLimitStore(builder.Configuration);

// 在线人数与邮箱验证码存储：与限流同一套开关——配了 Redis:ConnectionString 就跨实例共享，
// 留空则用进程内实现。验证码是安全凭证，Redis 不可用时按 fail-closed 拒绝（见 RedisEmailCodeStore）。
builder.Services.AddOnlineTrackerStore(builder.Configuration);
builder.Services.AddEmailCodeStore(builder.Configuration);

// Data Protection 密钥环：默认写在容器内 ~/.aspnet，容器一重建就全员掉线。
// 配了 Redis 落 Redis（多实例共享），否则落到内容根下的 keys/（1Panel 把宿主目录挂到 /app，可持久）。
builder.Services.AddClouderyDataProtection(builder.Configuration, builder.Environment);

// 管理员 policy 授权（Stage 5.3）：[AdminOnly] 只声明 policy，判定与 401/403 形状在 Shared/Authorization。
builder.Services.AddAdminOnlyAuthorization();

// ===== 可信反向代理头 =====
// 生产在 Nginx / 云负载均衡之后时 RemoteIpAddress 是代理 IP，按 IP 的限流会退化成
// 「全站共用一个计数桶」。显式配置可信代理后改用 X-Forwarded-For 取真实客户端 IP。
// 两项（Proxies / Networks）都为空时不启用，避免信任伪造头导致限流被绕过。
var forwardedHeadersSection = builder.Configuration.GetSection(TrustedProxyOptions.SectionName);
builder.Services.Configure<TrustedProxyOptions>(forwardedHeadersSection);
var trustedProxyOptions = forwardedHeadersSection.Get<TrustedProxyOptions>() ?? new TrustedProxyOptions();
var useForwardedHeaders = trustedProxyOptions.TryResolve(out var trustedProxies, out var trustedNetworks);
if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.None;
        if (trustedProxyOptions.TrustForwardedFor) options.ForwardedHeaders |= ForwardedHeaders.XForwardedFor;
        if (trustedProxyOptions.TrustForwardedProto) options.ForwardedHeaders |= ForwardedHeaders.XForwardedProto;
        if (trustedProxyOptions.TrustForwardedHost) options.ForwardedHeaders |= ForwardedHeaders.XForwardedHost;
        // 清空框架默认的可信列表（默认含回环），改由配置显式声明，
        // 避免「默认信任 localhost」在多容器部署里被绕过。
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in trustedProxies) options.KnownProxies.Add(proxy);
        foreach (var network in trustedNetworks) options.KnownIPNetworks.Add(network);
    });
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCasdoor(builder.Configuration.GetSection("Casdoor"))
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.HttpOnly = true; // 安全：禁止前端 JS 读取会话 Cookie，防 XSS 窃取会话
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/identity/auth/login";
        options.LogoutPath = "/identity/auth/logout";
    });

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new [] {
        "http://localhost:5173",
        "http://localhost:5174",
        "https://localhost:5173",
        "http://localhost:5175",
        "https://localhost:5174",
        "https://mhop.cldery.com",
        "https://cldery.com",
        "https://www.cldery.com"
    };

builder.Services.AddCors(c =>
{
    c.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowCredentials()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// CSRF 防护白名单（与 CORS 同源），供下方中间件使用
var allowedOriginSet = allowedOrigins.ToHashSet(StringComparer.OrdinalIgnoreCase);

builder.Services.AddSwaggerGen(u =>
{
    u.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "Ver:1.0.0",
        Title = "ClouderyApi · 云术工作室",
        Description = "云术工作室（Cloudery Studio）旗下服务的统一后端。SCForge 生存战争插件、模组资源平台位于 /scforge。",
        Contact = new OpenApiContact
        {
            Name = "JustQiyi",
            Email = "justqiyi@qq.com"
        }
    });
});

var app = builder.Build();

// ===== 维护工具：清理对象存储中的历史孤儿图片 =====
// 用法：dotnet ClouderyApi.dll --sweep-orphans [--delete-orphans]
// 放在迁移 / 种子之前，一次性维护动作不应触发数据库变更。
if (sweepOrphans)
{
    using var sweepScope = app.Services.CreateScope();
    return await MhopOrphanSweeper.RunAsync(sweepScope.ServiceProvider, sweepOrphansDelete);
}

// ===== 维护工具：数据库迁移 / 种子数据 =====
// 用法：dotnet ClouderyApi.dll --migrate [--seed]；部署脚本在重启容器前执行。
// 迁移一律前滚；这里显式执行，失败以非 0 退出码中止（避免带着未迁移的库继续跑）。
if (runMigrate || runSeed)
{
    using var maintenanceScope = app.Services.CreateScope();
    var maintenance = maintenanceScope.ServiceProvider.GetRequiredService<DatabaseMaintenanceService>();
    try
    {
        // SCForge 的 scforge_* 表与 MHOP 是两个独立上下文，必须分别前滚；
        // 漏掉任何一个都会表现为「部署成功但新表不存在」。
        if (runMigrate)
        {
            await maintenance.MigrateAsync();
            await maintenanceScope.ServiceProvider.GetRequiredService<ScforgeMaintenanceService>().MigrateAsync();
        }
        if (runSeed) await maintenance.SeedAsync();
        return 0;
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "数据库维护动作失败（--migrate / --seed）");
        return 1;
    }
}

// 可信代理头必须在所有中间件最前面：它改写 RemoteIpAddress，之后的限流/CORS 才拿得到真实客户端 IP。
if (useForwardedHeaders)
    app.UseForwardedHeaders();

// ===== MHOP：数据库自动迁移 + 种子数据 =====
// 默认关闭（Mhop:AutoMigrate 留空时仅 Development 打开）：生产由部署脚本显式执行 --migrate。
// 迁移失败不阻塞启动（可用 dotnet ef database update --context MhopDbContext 手动执行）。
var mhopOptions = app.Services.GetRequiredService<IOptions<MhopOptions>>().Value;
if (mhopOptions.AutoMigrate ?? app.Environment.IsDevelopment())
{
    try
    {
        using var mhopScope = app.Services.CreateScope();
        await mhopScope.ServiceProvider.GetRequiredService<DatabaseMaintenanceService>().MigrateAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "MHOP 数据库自动迁移失败，可执行 dotnet ef database update --context MhopDbContext 手动迁移");
    }
}

// 种子默认关闭（Mhop:Seed），生产由部署脚本显式执行 --seed。
if (mhopOptions.Seed)
{
    try
    {
        using var mhopScope = app.Services.CreateScope();
        await mhopScope.ServiceProvider.GetRequiredService<DatabaseMaintenanceService>().SeedAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "MHOP 种子数据初始化失败");
    }
}

// ===== 基础限流（固定窗口，按客户端 IP） =====
// 缓解登录/发布/点赞等接口被爆破或刷量。计数落在 IRateLimitStore：
// 配了 Redis:ConnectionString 就是跨实例共享的计数（重启不清零），留空则是原来的进程内实现。
var rateLimitStore = app.Services.GetRequiredService<IRateLimitStore>();
app.Logger.LogInformation("限流计数存储：{Store}", rateLimitStore.GetType().Name);
var onlineTrackerStore = app.Services.GetRequiredService<IOnlineTrackerStore>();
app.Logger.LogInformation("在线人数存储：{Store}", onlineTrackerStore.GetType().Name);
var emailCodeStore = app.Services.GetRequiredService<IEmailCodeStore>();
app.Logger.LogInformation("邮箱验证码存储：{Store}", emailCodeStore.GetType().Name);
app.Use(async (context, next) =>
{
    const int maxRequests = 300;      // 每窗口内最大请求数
    const int windowSeconds = 60;     // 窗口时长(秒)
    var ip = ClientIp.Resolve(context);
    var counter = await rateLimitStore.IncrementAsync($"ratelimit:global:{ip}", windowSeconds);
    if (counter.Count > maxRequests)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(new { success = false, message = "请求过于频繁，请稍后再试" });
        return;
    }
    await next();
});

app.UseCors("AllowAllOrigins");

// CSRF 防护中间件：
// Cookie 会话为 SameSite=None，跨站请求会携带 Cookie，必须校验 Origin。
// 对 POST/PUT/PATCH/DELETE：若请求带 Origin 头（浏览器必然携带），则必须在白名单内，
// 否则拒绝；无 Origin（同源 curl/服务端调用）放行。
if(!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        var method = context.Request.Method;
        if (method == HttpMethods.Post || method == HttpMethods.Put ||
            method == HttpMethods.Patch || method == HttpMethods.Delete)
        {
            if (context.Request.Headers.TryGetValue("Origin", out var originValues))
            {
                foreach (var originValue in originValues)
                {
                    if (string.IsNullOrEmpty(originValue)) continue;
                    if (!allowedOriginSet.Contains(originValue))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new { success = false, message = "跨站请求被拒绝" });
                        return;
                    }
                }
            }
        }

        await next();
    });
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseHttpsRedirection();

// ===== MHOP 图片本地静态托管：/mhop/uploads/* =====
// 即使已切到远端 OSS，也保留本地托管，让迁移前的历史图片继续可访问。
var mhopStorage = app.Services.GetRequiredService<IMhopObjectStorage>();
var mhopUploadRoot = mhopStorage is MhopLocalObjectStorage localStorage
    ? localStorage.Root
    : MhopUploadPaths.ResolveLocalRoot(mhopOptions.UploadDir, app.Environment.ContentRootPath);
Directory.CreateDirectory(Path.Combine(mhopUploadRoot, "avatars"));
Directory.CreateDirectory(Path.Combine(mhopUploadRoot, "posts"));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mhopUploadRoot),
    RequestPath = MhopUploadPaths.RequestPath,
});
app.Logger.LogInformation("MHOP 图片存储：{Provider}（本地兼容目录 {Root}）", mhopStorage.Provider, mhopUploadRoot);

// ===== SCForge 本地上传目录的静态托管：/scforge/uploads/* =====
// 只有 Storage:Provider=local 时图片才落在这里；改用 oss 后图片走外链，这一段自然闲置（保留以便随时切回）。
// 只托管 public/ 下的图片；插件包在私有目录里，只能经 /scforge/versions/{id}/download 获取，
// 这样隐藏插件的包不会被静态路径绕过，下载计数也只有一个口径。
var scforgeFileOptions = app.Services.GetRequiredService<IOptions<ScforgeOptions>>().Value;
var scforgeUploadRoot = ScforgeUploadPaths.ResolveLocalRoot(scforgeFileOptions.UploadDir, app.Environment.ContentRootPath);
var scforgePublicRoot = Path.Combine(scforgeUploadRoot, "public");
Directory.CreateDirectory(Path.Combine(scforgePublicRoot, "images"));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(scforgePublicRoot),
    RequestPath = ScforgeUploadPaths.RequestPath,
    OnPrepareResponse = context =>
    {
        // 用户上传的图片：禁止内容嗅探，降低被当成脚本解析的风险。
        context.Context.Response.Headers.XContentTypeOptions = "nosniff";
    },
});
var scforgeStore = app.Services.GetRequiredService<IScforgeFileStore>();
app.Logger.LogInformation(
    "SCForge 存储：{Provider}{Remote}（上传目录 {Root}，公开图片 {Public}）",
    scforgeStore.Provider,
    scforgeStore.IsRemote ? "（远端 OSS）" : string.Empty,
    scforgeUploadRoot,
    scforgePublicRoot);

app.UseAuthentication();

// API Key 认证通道。必须紧跟 UseAuthentication（要读 Cookie 解析结果以确保"会话优先"），
// 且早于控制器：校验通过后写入 HttpContext.User，作者判定与权限作用域随即生效。
// 校验失败不拒绝，交由各写端点按既有约定返回 401 中文错误体，保证对外错误形状不变。
app.UseMiddleware<ScforgeApiKeyMiddleware>();

app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI(u => { u.SwaggerEndpoint("/swagger/v1/swagger.json", "WebAPI_v1"); });
}

app.Run();

// 使用了 return 值，顶层语句必须显式给出正常启动时的返回码
return 0;