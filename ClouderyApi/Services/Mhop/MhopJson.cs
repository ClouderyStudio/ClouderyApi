using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// MHOP 响应序列化约定：蛇形字段名 + UTC 时间带 Z，与 Python FastAPI 的返回结构保持一致，
/// 前端无需改动即可对接。请求体绑定沿用 MVC 默认策略，因此请求 DTO 用 [JsonPropertyName] 声明蛇形键名。
/// </summary>
public static class MhopJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // 中文不转义为 \uXXXX，保持与 Python 后端相同的可读输出
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new MhopUtcDateTimeConverter() },
    };

    /// <summary>统一错误响应体 { detail: "..." }，与 FastAPI 的 HTTPException 一致。</summary>
    public static IActionResult Error(int statusCode, string detail)
        => new JsonResult(new { detail }, Options) { StatusCode = statusCode };
}

/// <summary>数据库中读取的 DateTime 为 Unspecified，统一按 UTC 输出并带 Z 后缀，避免前端时区解析歧义。</summary>
public sealed class MhopUtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
