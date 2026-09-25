using Casdoor.AspNetCore.Authentication;
using ClouderyApi.Data;
using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi;
using System.Collections.Concurrent;

// 维护开关在交给配置系统之前先摘出来：命令行配置提供程序不接受没有取值的裸开关。
var sweepOrphans = args.Any(a => a.Equals("--sweep-orphans", StringComparison.OrdinalIgnoreCase));
var sweepOrphansDelete = args.Any(a => a.Equals("--delete-orphans", StringComparison.OrdinalIgnoreCase));
var builder = WebApplication.CreateBuilder(
    args.Where(a => !a.Equals("--sweep-orphans", StringComparison.OrdinalIgnoreCase)
                    && !a.Equals("--delete-orphans", StringComparison.OrdinalIgnoreCase)).ToArray());

builder.Services.AddControllers(options => options.Filters.Add<MhopApiExceptionFilter>());

builder.Services.AddHttpClient("Casdoor"); // 供 AuthController 通过 IHttpClientFactory 使用

builder.Services.AddOpenApi();

builder.Services.AddDbContext<ClouderyApiContext>(options =>
{
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!);
});

builder.Services.AddDbContext<QisoulDbContext>(options =>
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!));

// ===== MHOP 公益心理辅助平台模块（从 Python FastAPI 后端迁移） =====
builder.Services.Configure<MhopOptions>(builder.Configuration.GetSection(MhopOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<MhopDbContext>(options =>
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!));
builder.Services.AddSingleton<MhopPasswordHasher>();
builder.Services.AddSingleton<IMhopJwtService, MhopJwtService>();
builder.Services.AddSingleton<MhopOnlineTracker>();
builder.Services.AddSingleton<MhopSmtpClient>();
builder.Services.AddSingleton<MhopEmailCodeService>();
builder.Services.AddSingleton<MhopCasdoorService>();
builder.Services.AddSingleton<MhopAiService>();
builder.Services.AddScoped<MhopCurrentUserAccessor>();

// 图片对象存储：local = 本机磁盘（由 /mhop/uploads 静态托管）；oss = 远端阿里云 OSS
builder.Services.AddSingleton<IMhopObjectStorage>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var provider = (configuration["Mhop:Storage:Provider"] ?? "local").Trim();
    if (provider.Equals("oss", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("aliyun", StringComparison.OrdinalIgnoreCase))
    {
        var ossOptions = new MhopOssOptions();
        configuration.GetSection("Mhop:Storage:Oss").Bind(ossOptions);
        return new MhopAliyunOssStorage(ossOptions, sp.GetRequiredService<ILogger<MhopAliyunOssStorage>>());
    }

    var contentRoot = sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
    return new MhopLocalObjectStorage(MhopUploadPaths.ResolveLocalRoot(configuration["Mhop:UploadDir"], contentRoot));
});
builder.Services.AddScoped<MhopUploadService>();
builder.Services.AddScoped<MhopContentService>();

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
        "https://qisoul.cldery.com",
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
        Title = "ClouderyApi",
        Description = "ClouderyApi",
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

// ===== MHOP：数据库自动迁移 + 种子数据 =====
// 迁移失败不阻塞启动（可用 dotnet ef database update --context MhopDbContext 手动执行）。
if (app.Configuration.GetValue("Mhop:AutoMigrate", app.Environment.IsDevelopment()))
{
    try
    {
        using var mhopScope = app.Services.CreateScope();
        mhopScope.ServiceProvider.GetRequiredService<MhopDbContext>().Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "MHOP 数据库自动迁移失败，可执行 dotnet ef database update --context MhopDbContext 手动迁移");
    }
}

if (app.Configuration.GetValue("Mhop:Seed", true))
{
    try
    {
        using var mhopScope = app.Services.CreateScope();
        await MhopSeeder.SeedAsync(
            mhopScope.ServiceProvider.GetRequiredService<MhopDbContext>(),
            mhopScope.ServiceProvider.GetRequiredService<MhopPasswordHasher>(),
            app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "MHOP 种子数据初始化失败");
    }
}

// ===== 基础限流（内存固定窗口，按客户端 IP） =====
// 缓解登录/发布/点赞等接口被爆破或刷量；分布式场景可替换为 Redis 实现。
var rateLimitStore = new ConcurrentDictionary<string, (int count, long windowStart)>();
app.Use(async (context, next) =>
{
    const int maxRequests = 300;      // 每窗口内最大请求数
    const int windowSeconds = 60;     // 窗口时长(秒)
    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var entry = rateLimitStore.GetOrAdd(ip, _ => (count: 0, windowStart: now));
    // 窗口滚动：距上次窗口起始超过 windowSeconds 则开新窗口
    if (now - entry.windowStart >= windowSeconds)
        entry = (count: 0, windowStart: now);
    entry.count++;
    rateLimitStore[ip] = entry;
    if (entry.count > maxRequests)
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
    : MhopUploadPaths.ResolveLocalRoot(app.Configuration["Mhop:UploadDir"], app.Environment.ContentRootPath);
Directory.CreateDirectory(Path.Combine(mhopUploadRoot, "avatars"));
Directory.CreateDirectory(Path.Combine(mhopUploadRoot, "posts"));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mhopUploadRoot),
    RequestPath = MhopUploadPaths.RequestPath,
});
app.Logger.LogInformation("MHOP 图片存储：{Provider}（本地兼容目录 {Root}）", mhopStorage.Provider, mhopUploadRoot);

app.UseAuthentication();

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