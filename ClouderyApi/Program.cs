using Casdoor.AspNetCore.Authentication;
using ClouderyApi.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddHttpClient("Casdoor"); // 供 AuthController 通过 IHttpClientFactory 使用

builder.Services.AddOpenApi();

builder.Services.AddDbContext<ClouderyApiContext>(options =>
{
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!);
});

builder.Services.AddDbContext<QisoulDbContext>(options =>
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!));

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

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI(u => { u.SwaggerEndpoint("/swagger/v1/swagger.json", "WebAPI_v1"); });
}

app.Run();