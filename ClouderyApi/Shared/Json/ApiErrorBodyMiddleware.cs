using Microsoft.AspNetCore.Http;

namespace ClouderyApi.Shared.Json;

/// <summary>
/// 错误响应兜底中间件：框架自己产生的错误（未知路由 404、405、415、401 挑战、403）只有状态码
/// 没有响应体，未处理异常则直接中断连接——这里统一补成 <c>{ "detail": "..." }</c>，
/// 与控制器/限流写的错误体形状一致（见 docs/API-ERROR-SHAPE.md）。
///
/// 只补「状态码 ≥ 400 且响应体为空」的响应：已经写过体的（控制器、授权处理器）原样放行。
/// 必须注册在管道最前面，才能同时覆盖静态文件、CORS、鉴权与 MVC。
/// </summary>
public sealed class ApiErrorBodyMiddleware(
    RequestDelegate next,
    ILogger<ApiErrorBodyMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            // 全环境统一：异常写进日志（含堆栈），响应体统一成 { detail:"服务器内部错误" }。
            // 项目没有注册开发者异常页 / UseExceptionHandler，rethrow 只会得到空体 500，反而不一致。
            logger.LogError(
                ex,
                "请求处理发生未处理异常，返回统一 500 错误体：{Method} {Path}",
                context.Request.Method,
                context.Request.Path);
            await ApiError.WriteAsync(context, StatusCodes.Status500InternalServerError, ApiError.InternalDetail);
            return;
        }

        if (context.Response.StatusCode >= StatusCodes.Status400BadRequest
            && !context.Response.HasStarted
            && context.Response.ContentType is null)
        {
            await ApiError.WriteAsync(
                context,
                context.Response.StatusCode,
                ApiError.DefaultDetail(context.Response.StatusCode));
        }
    }
}
