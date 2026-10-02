using ClouderyApi.Shared.Ai;

namespace ClouderyApi.Modules.Mhop.Domain;

/// <summary>
/// 内容安全：危机词识别（触发强制援助提示）+ 基础敏感词巡检。
/// 生产环境可替换为云厂商内容安全 API，调用方仅依赖 DetectCrisis / HitSensitive。
/// </summary>
public static class MhopModeration
{
    /// <summary>自伤 / 自杀危机信号（词表与 Cloudery 结果解读共用，见 Services/Ai/CrisisSupport）。</summary>
    public static readonly string[] CrisisWords = CrisisSupport.CrisisWords;

    /// <summary>基础违规词（演示词库；正式上线请替换为专业敏感词库 / 云审核 API）。</summary>
    public static readonly string[] SensitiveWords =
    [
        "色情", "裸体", "约炮", "迷奸", "冰毒", "海洛因", "卖毒品", "枪支",
        "炸药", "赌博网站", "刷单", "代开发票", "私家侦探", "高利贷",
        "加微信领", "点击链接", "六合彩", "代考",
    ];

    public static bool DetectCrisis(string? text) => CrisisSupport.DetectCrisis(text);

    public static List<string> HitSensitive(string? text)
        => string.IsNullOrEmpty(text) ? [] : SensitiveWords.Where(text.Contains).ToList();
}
