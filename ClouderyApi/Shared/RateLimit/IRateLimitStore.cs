namespace ClouderyApi.Shared.RateLimit;

/// <summary>
/// 固定窗口计数存储，供全局限流中间件与 <see cref="ClouderyApi.Shared.Filters.IpRateLimitAttribute"/> 共用。
/// 口径统一为：key 第一次出现即开启窗口并计数，窗口到期后从 1 重新开始。
/// </summary>
public interface IRateLimitStore
{
    /// <summary>
    /// 把 <paramref name="key"/> 在当前窗口内的计数加一，并返回计数与窗口剩余秒数。
    /// 实现自行处理存储不可用的情况（降级而非抛异常），调用方不需要 try/catch。
    /// </summary>
    ValueTask<RateLimitCounter> IncrementAsync(string key, int windowSeconds);
}

/// <summary>一次限流计数的结果。</summary>
/// <param name="Count">当前窗口内的累计次数（含本次）。</param>
/// <param name="RetryAfterSeconds">窗口剩余秒数，用于 Retry-After 头；至少为 1。</param>
public readonly record struct RateLimitCounter(long Count, int RetryAfterSeconds);
