namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// SCForge 域规则被违反（字段非法、状态不允许、配额超限…）。
///
/// 刻意**不复用** <c>Shared.Exceptions.DomainRuleException</c>：全局
/// <c>MhopApiExceptionFilter</c> 会把它渲染成 MHOP 形状的 <c>{ detail }</c> 响应，
/// 而 SCForge 与 Cloudery / Zhuxs 一致，对外一律是 <c>{ success, message }</c> 裸对象。
/// 领域层只承载面向用户的中文原因，由控制器翻译成 HTTP 状态码。
/// </summary>
public sealed class ScforgeRuleException(string message) : Exception(message);

/// <summary>带显式状态码的 SCForge 业务异常（401 / 403 / 404 / 409…）。</summary>
public sealed class ScforgeApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
