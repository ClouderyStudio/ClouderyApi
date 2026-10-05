using System.Globalization;
using ClouderyApi.Shared.Json;
using ClouderyApi.Shared.RateLimit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ClouderyApi.Shared.Filters;

/// <summary>
/// 按客户端 IP 的固定窗口限流，用于成本敏感或易被刷的公开接口（如 AI 解读）。
/// 全局中间件已有 300 次/分钟的宽松额度（Program.cs），这里用更紧的额度叠加限制单接口。
/// 计数落在 <see cref="IRateLimitStore"/>：配了 <c>Redis:ConnectionString</c> 就跨实例共享、重启不清零，
/// 留空则是进程内实现（与接入 Redis 之前的行为完全一致）。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class IpRateLimitAttribute : ActionFilterAttribute
{
    /// <summary>窗口内允许的请求数。</summary>
    public int MaxRequests { get; set; } = 10;

    /// <summary>窗口长度（秒）。</summary>
    public int WindowSeconds { get; set; } = 300;

    /// <summary>
    /// 429 响应的错误体形状。Cloudery / Zhuxs 返回裸对象 <c>{ success, message }</c>，
    /// MHOP 走MhopJson 的 <c>{ detail }</c>（与 Python FastAPI 一致）——挂在 MHOP 控制器上时必须显式打开，
    /// 否则前端读不到 <c>detail</c>。
    /// </summary>
    public bool UseMhopErrorShape { get; set; }

    /// <summary>
    /// 重写异步版过滤器方法而不是同步的 <c>OnActionExecuting</c>：计数现在可能是一次 Redis 往返，
    /// 在同步方法里等异步结果会白占一个线程池线程（限流是每个请求都要经过的路径）。
    /// </summary>
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (MaxRequests <= 0 || WindowSeconds <= 0)
        {
            await next();
            return;
        }

        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        var ip = ClientIp.Resolve(context.HttpContext);
        // 特性由 MVC 构造、没有构造注入，只能从请求作用域取服务；这里取到的是单例存储。
        var store = context.HttpContext.RequestServices.GetRequiredService<IRateLimitStore>();
        var counter = await store.IncrementAsync($"ratelimit:endpoint:{ip}|{path}", WindowSeconds);

        if (counter.Count <= MaxRequests)
        {
            await next();
            return;
        }

        var retryAfter = counter.RetryAfterSeconds;
        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        if (UseMhopErrorShape)
        {
            context.Result = MhopJson.Error(StatusCodes.Status429TooManyRequests, RateLimitMessage);
            return;
        }

        // 用 ObjectResult（走 MVC 输出格式化器）而不是 JsonResult，中文不会被转成 \uXXXX，
        // 与 Cloudery 模块其它接口的响应风格保持一致。
        context.Result = new ObjectResult(new
        {
            success = false,
            message = RateLimitMessage,
            retryAfterSeconds = retryAfter,
        })
        {
            StatusCode = StatusCodes.Status429TooManyRequests,
        };
    }

    /// <summary>限流文案。对外逐字不变，既有断言依赖它。</summary>
    private const string RateLimitMessage = "分析请求过于频繁，请稍后再试";
}
