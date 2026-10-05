namespace ClouderyApi.Shared.Email;

/// <summary>发码结果；<see cref="Code"/> 为 null 表示被频控拦下，<see cref="RetryAfterSeconds"/> 是建议重试秒数。</summary>
public readonly record struct EmailCodeIssueResult(string? Code, int RetryAfterSeconds);

/// <summary>
/// 邮箱验证码的存储抽象。两种实现语义一致：
/// 6 位数字、10 分钟有效、校验成功即失效（一次性）、最多错 5 次；
/// 同一邮箱 60 秒发送间隔；同一 IP 每小时最多 20 次。
/// <para>
/// 与限流 / 在线人数不同，验证码是安全凭证，**fail-closed**：
/// Redis 不可用时宁可拒绝发码 / 拒绝登录，也不能因为存储降级而放行。
/// </para>
/// </summary>
public interface IEmailCodeStore
{
    /// <summary>同一 IP 的发码配额。返回 false 表示超限（或存储不可用）。</summary>
    ValueTask<bool> CheckIpRateAsync(string ip);

    /// <summary>生成并保存验证码；被频控或存储不可用时返回 null Code。</summary>
    ValueTask<EmailCodeIssueResult> IssueCodeAsync(string email);

    /// <summary>校验并一次性消费验证码。</summary>
    ValueTask<bool> VerifyCodeAsync(string email, string code);
}
