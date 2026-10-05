namespace ClouderyApi.Shared.Time;

/// <summary>
/// 对外（HTTP 响应）时间的统一标准：北京时间（UTC+8）。
/// 数据库一律存 UTC（写入侧见 <see cref="UtcClock"/>）；MySQL 的 datetime(6) 读回来是
/// <see cref="DateTimeKind.Unspecified"/>，必须先把无时区的值按 UTC 认领、再换成 +08:00 呈现，
/// 否则客户端会按浏览器本地时区解析而得到错误的时刻。
/// 模块内的手工换算同口径：<c>ClouderyApi/Modules/Scforge/Application/Mapping/ScforgeMapper.cs</c> 的 ToBeijing。
/// </summary>
public static class BeijingTime
{
    /// <summary>北京时间偏移（中国全境 UTC+8，无夏令时）。</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    /// <summary>把库内/内存里的 DateTime 认领成 UTC 后转成北京时间。</summary>
    public static DateTimeOffset From(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => new DateTimeOffset(value).ToOffset(Offset),
        DateTimeKind.Unspecified => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToOffset(Offset),
        _ => new DateTimeOffset(value).ToOffset(Offset),
    };

    /// <summary>已是带偏移的时刻：只换成 +08:00 的呈现，时刻本身不变。</summary>
    public static DateTimeOffset From(DateTimeOffset value) => value.ToOffset(Offset);
}
