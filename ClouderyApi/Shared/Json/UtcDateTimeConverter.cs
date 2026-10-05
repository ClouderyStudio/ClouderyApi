using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClouderyApi.Shared.Json;

/// <summary>
/// MVC 默认序列化器（Cloudery / Zhuxs / Scforge / Identity 控制器）的时间约定：
/// 数据库的 datetime(6) 读回来是 <see cref="DateTimeKind.Unspecified"/>，直接序列化会丢掉 Z 后缀，
/// 客户端按本地时间解析就会得到错误的时刻。这里统一按 UTC 输出（写入时用的就是 UTC），
/// 与 MHOP 的 <see cref="MhopUtcDateTimeConverter"/> 同一规则。
/// MHOP 走自己的 <see cref="MhopJson.Options"/>，不受这里影响。
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToUtc(value));

    /// <summary>库里的 Unspecified 就是 UTC；Local 先换算再标注。</summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value,
    };
}
