namespace ClouderyApi.Shared.Time;

/// <summary>
/// 全站时间戳的唯一来源。MySQL 的 datetime(6) 只存到微秒，而 <see cref="DateTime.UtcNow"/> 的
/// tick 精度是 100ns：直接入库会被 MySQL 四舍五入，于是「刚写入的响应」与「读回来的响应」
/// 时间字符串不一致（见 docs/DDD-REFACTOR-PLAN.md 附录 C.4）。写入前统一截到微秒，两处就逐字相同。
/// </summary>
public static class UtcClock
{
    /// <summary>当前 UTC 时间，已截到微秒。</summary>
    public static DateTime Now() => TruncateToMicrosecond(DateTime.UtcNow);

    /// <summary>向下截到微秒（不四舍五入，避免入库时 MySQL 再进位）并标记为 UTC。</summary>
    public static DateTime TruncateToMicrosecond(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
}
