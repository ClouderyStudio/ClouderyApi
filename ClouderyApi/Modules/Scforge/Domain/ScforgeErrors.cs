namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// SCForge 域规则被违反（字段非法、状态不允许、配额超限…）。
///
/// 刻意**不复用** <c>Shared.Exceptions.DomainRuleException</c>：那个异常由全局
/// <c>MhopApiExceptionFilter</c> 处理，语义属于 MHOP 模块；SCForge 要自己决定状态码
/// （400 / 401 / 403 / 404 / 409），所以由 <c>ScforgeControllerBase.GuardAsync</c> 翻译成
/// 统一错误体 <c>{ "detail": "…" }</c>（<c>Shared.Json.ApiError</c> 产出）。
/// 领域层只承载面向用户的中文原因，由控制器翻译成 HTTP 状态码。
/// </summary>
public sealed class ScforgeRuleException(string message) : Exception(message);

/// <summary>带显式状态码的 SCForge 业务异常（401 / 403 / 404 / 409…）。</summary>
public sealed class ScforgeApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
