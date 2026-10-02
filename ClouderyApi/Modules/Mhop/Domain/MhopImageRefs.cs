using System.Text.Json;

namespace ClouderyApi.Modules.Mhop.Domain;

/// <summary>
/// 入库图片地址数组的统一解析 / 归一化 / 序列化：帖子 / 回复的 images 字段是 JSON 字符串数组。
/// 上传、删除与孤儿清理工具共用同一份实现，避免解析规则漂移后误删仍在引用的图片。
/// </summary>
public static class MhopImageRefs
{
    /// <summary>单个内容最多 9 张图（与上传接口限制一致）。</summary>
    public const int MaxPerContent = 9;

    /// <summary>解析 images 字段；为空或格式非法时返回空列表。</summary>
    public static List<string> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(raw);
            return parsed is null
                ? []
                : parsed.Where(u => !string.IsNullOrEmpty(u)).Take(MaxPerContent).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>归一化提交上来的图片列表：丢弃空白项并截断到 9 张。</summary>
    public static List<string> Normalize(IEnumerable<string>? urls)
        => urls is null
            ? []
            : urls.Where(u => !string.IsNullOrWhiteSpace(u)).Take(MaxPerContent).ToList();

    /// <summary>序列化入库；空列表存空串（保持历史格式）。</summary>
    public static string Serialize(IReadOnlyCollection<string> urls)
        => urls.Count > 0 ? JsonSerializer.Serialize(urls) : string.Empty;
}
