namespace ClouderyApi.Shared.Ai;

/// <summary>
/// 危机干预的公共口径：援助文案与危机词表由 MHOP（社区/漂流瓶/测评）与 Cloudery 结果分析共用，
/// 保证任何入口给出的热线内容与触发条件完全一致。
/// 注意：<see cref="Prefix"/> 与 <see cref="CrisisWords"/> 的文案即为对外可见内容，非必要不要改动。
/// </summary>
public static class CrisisSupport
{
    /// <summary>全国心理援助热线。</summary>
    public const string HotlineNumber = "12356";

    /// <summary>危机时强制置于回复开头的援助信息。</summary>
    public const string Prefix =
        "我注意到你正经历非常痛苦、甚至可能想伤害自己的时刻，我很担心你的安全。" +
        "请不要独自硬撑：\n" +
        "1）立即拨打全国心理援助热线 12356（24小时、免费、保密），" +
        "或北京心理危机研究与干预中心 010-82951332；\n" +
        "2）如果你觉得自己可能马上做出伤害自己的事，请立刻拨打 110 或 120，" +
        "或直接前往最近医院的急诊；\n" +
        "3）现在就联系一位你信任的家人或朋友，告诉他/她你的感受，并尽量和人待在一起，远离危险物品。\n\n";

    /// <summary>自伤 / 自杀危机信号。</summary>
    public static readonly string[] CrisisWords =
    [
        "自杀", "自尽", "轻生", "想死", "不想活", "活着没意思", "活不下去", "活够了",
        "结束生命", "离开这个世界", "一了百了", "找死", "寻死", "同归于尽",
        "自残", "割腕", "割自己", "跳楼", "上吊", "烧炭", "安眠药", "伤害自己",
        "毁掉自己", "撑不下去了", "解脱",
    ];

    public static bool DetectCrisis(string? text)
        => !string.IsNullOrEmpty(text) && CrisisWords.Any(text.Contains);

    /// <summary>文本中是否已包含援助热线号码。</summary>
    public static bool HasHotline(string? text)
        => !string.IsNullOrEmpty(text) && text.Contains(HotlineNumber, StringComparison.Ordinal);

    /// <summary>危机场景下保证回复开头带有援助热线信息（已包含则原样返回）。</summary>
    public static string EnsureHotline(string? reply, bool crisis)
    {
        var text = reply ?? string.Empty;
        return crisis && !HasHotline(text) ? Prefix + text : text;
    }
}
