using System.Text.Json;
using System.Text.Json.Serialization;
using ClouderyApi.Shared.Time;

namespace ClouderyApi.Shared.Json;

/// <summary>
/// MVC 默认序列化器（Cloudery / Zhuxs / Scforge / Identity 控制器）的时间约定：对外一律写成
/// 北京时间（+08:00，见 <see cref="BeijingTime"/>）。读入侧不做时区改写，保持既有语义
/// （库里存的就是 UTC，写入路径自己负责归一化）。
/// MHOP 走自己的 <see cref="MhopJson.Options"/>，但在 <see cref="MhopBeijingDateTimeConverter"/> 里同口径。
/// </summary>
public sealed class BeijingDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(BeijingTime.From(value));
}

/// <summary>同上，用于 <see cref="DateTimeOffset"/> 字段（例如结果解读的 generatedAt）。</summary>
public sealed class BeijingDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(BeijingTime.From(value));
}
