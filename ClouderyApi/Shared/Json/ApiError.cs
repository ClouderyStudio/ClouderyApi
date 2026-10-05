using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Shared.Json;

/// <summary>
/// 统一错误响应体：<c>{ "detail": "中文文案" }</c>，可带两个可选扩展字段
/// <c>errors</c>（仅 400 模型校验：字段名 → 消息数组）与 <c>retryAfterSeconds</c>（仅 429，
/// 与 <c>Retry-After</c> 头同源）。
///
/// 键名刻意用 <see cref="Dictionary{TKey,TValue}"/> 显式构造、而不是匿名类型：序列化走
/// <see cref="MhopJson.Options"/>，它的 <c>PropertyNamingPolicy</c> 是 <c>SnakeCaseLower</c>，
/// 匿名类型的 <c>retryAfterSeconds</c> 会被写成 <c>retry_after_seconds</c>；字典键只受
/// <c>DictionaryKeyPolicy</c> 影响，而那项未设置。
/// </summary>
public static class ApiError
{
    /// <summary>模型校验失败的统一文案（400，由 InvalidModelStateResponseFactory 使用）。</summary>
    public const string ValidationDetail = "参数校验失败";

    /// <summary>未处理异常的统一文案（500）。</summary>
    public const string InternalDetail = "服务器内部错误";

    /// <summary>构造错误响应体。</summary>
    public static Dictionary<string, object?> Body(
        string detail,
        IReadOnlyDictionary<string, string[]>? errors = null,
        int? retryAfterSeconds = null)
    {
        var body = new Dictionary<string, object?> { ["detail"] = detail };
        if (errors is { Count: > 0 }) body["errors"] = errors;
        if (retryAfterSeconds is not null) body["retryAfterSeconds"] = retryAfterSeconds.Value;
        return body;
    }

    /// <summary>
    /// 控制器用的错误结果：<see cref="JsonResult"/> 配 <see cref="MhopJson.Options"/>（蛇形键名、
    /// 中文不转义）。返回具体类型 <see cref="JsonResult"/>（而不是 <c>IActionResult</c>）是为了让
    /// 返回 <c>ActionResult&lt;T&gt;</c> 的控制器能直接 <c>return</c> 它。
    /// </summary>
    public static JsonResult Result(
        int statusCode,
        string detail,
        IReadOnlyDictionary<string, string[]>? errors = null,
        int? retryAfterSeconds = null)
        => new JsonResult(Body(detail, errors, retryAfterSeconds), MhopJson.Options) { StatusCode = statusCode };

    /// <summary>中间件直接写响应体：框架产生的空体 4xx/5xx 用它补齐 <c>{ detail }</c>。</summary>
    public static async Task WriteAsync(HttpContext context, int statusCode, string detail)
    {
        context.Response.StatusCode = statusCode;
        // 上游（如路由 / 状态码结果）可能已写入 Content-Length: 0，这里要显式清掉才能写出响应体
        context.Response.ContentLength = null;
        await context.Response.WriteAsJsonAsync(Body(detail), MhopJson.Options);
    }

    /// <summary>框架只给了状态码（没有文案）时的兜底文案。</summary>
    public static string DefaultDetail(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "请求参数有误",
        StatusCodes.Status401Unauthorized => "请先登录",
        StatusCodes.Status403Forbidden => "没有访问权限",
        StatusCodes.Status404NotFound => "资源不存在",
        StatusCodes.Status405MethodNotAllowed => "请求方法不被允许",
        StatusCodes.Status406NotAcceptable => "无法生成请求的响应格式",
        StatusCodes.Status408RequestTimeout => "请求超时，请稍后再试",
        StatusCodes.Status409Conflict => "请求与当前资源状态冲突",
        StatusCodes.Status413PayloadTooLarge => "请求内容过大",
        StatusCodes.Status415UnsupportedMediaType => "不支持的请求内容类型",
        StatusCodes.Status429TooManyRequests => "请求过于频繁，请稍后再试",
        >= 500 => InternalDetail,
        _ => "请求失败",
    };
}
