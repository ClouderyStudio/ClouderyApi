namespace ClouderyApi.Shared.Email;

/// <summary>
/// 邮箱验证码的规则常量：两种实现（进程内 / Redis）与对外返回体共用同一套数值，
/// 必须与 Python 版保持一致（6 位数字、10 分钟有效、最多错 5 次、60 秒重发间隔、每 IP 每小时 20 次）。
/// </summary>
public static class EmailCodeDefaults
{
    /// <summary>验证码有效期（秒）。</summary>
    public const int CodeTtlSeconds = 600;

    /// <summary>同一邮箱两次发码的最小间隔（秒）。</summary>
    public const int ResendIntervalSeconds = 60;

    /// <summary>同一验证码最多允许校验失败的次数，超过即作废。</summary>
    public const int MaxAttempts = 5;

    /// <summary>同一 IP 在窗口内允许的发码次数。</summary>
    public const int IpHourlyLimit = 20;

    /// <summary>IP 频控窗口（秒）。</summary>
    public const int IpWindowSeconds = 3600;

    /// <summary>进程内实现同时跟踪的验证码邮箱数上限（防随机邮箱刷量撑爆内存）。</summary>
    public const int MaxTrackedEmails = 20_000;

    /// <summary>进程内实现同时跟踪的 IP 数上限（防伪造 / 轮换 IP 撑爆内存）。</summary>
    public const int MaxTrackedIps = 20_000;
}
