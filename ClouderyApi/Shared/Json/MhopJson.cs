using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClouderyApi.Shared.Time;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Shared.Json;

/// <summary>
/// MHOP 响应序列化约定：蛇形字段名 + 北京时间（UTC+8，+08:00）时间，与 Python FastAPI 的返回结构保持一致，
/// 前端无需改动即可对接。请求体绑定沿用 MVC 默认策略，因此请求 DTO 用 [JsonPropertyName] 声明蛇形键名。
/// </summary>
public static class MhopJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // 中文不转义为 \uXXXX，保持与 Python 后端相同的可读输出
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new MhopBeijingDateTimeConverter() },
    };

    /// <summary>
    /// 统一错误响应体 { detail: "..." }，与 FastAPI 的 HTTPException 一致。
    /// 实现见 <see cref="ApiError.Result"/>（全站错误形状的唯一出口）。
    /// </summary>
    public static IActionResult Error(int statusCode, string detail)
        => ApiError.Result(statusCode, detail);
}

/// <summary>数据库中读取的 DateTime 为 Unspecified（实为 UTC），统一换算成北京时间 +08:00 输出，避免前端按浏览器时区解析。</summary>
public sealed class MhopBeijingDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(BeijingTime.From(value));
}
