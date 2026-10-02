namespace ClouderyApi.Models;

/// <summary>
/// 领域规则被违反：非法状态转换、内容为空、超长等。
/// 只承载面向用户的原因文本，由 API 层的异常过滤器统一映射成 400，领域层不依赖 HTTP。
/// </summary>
public sealed class DomainRuleException : Exception
{
    public DomainRuleException(string message) : base(message)
    {
    }
}
