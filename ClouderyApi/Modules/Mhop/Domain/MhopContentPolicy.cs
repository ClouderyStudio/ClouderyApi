using System.Text.RegularExpressions;

namespace ClouderyApi.Modules.Mhop.Domain;

/// <summary>规则预筛结论。</summary>
public enum MhopPolicyVerdict
{
    /// <summary>未命中任何规则，需要交给大模型做语义判断。</summary>
    None = 0,

    /// <summary>命中自伤/自杀危机信号：按危机流程放行，不走大模型（模型会因安全策略拒答）。</summary>
    Crisis = 1,

    /// <summary>疑似风险：转人工，不调用大模型（联系方式/引流等确定性特征，模型容易误判为安全）。</summary>
    Suspect = 2,

    /// <summary>明确违规：转人工，不自动公开；消息场景同时自动隐藏。</summary>
    Violation = 3,
}

/// <summary>规则命中的结果。</summary>
public readonly record struct MhopPolicyResult(MhopPolicyVerdict Verdict, string Reason, string Keyword)
{
    public static readonly MhopPolicyResult None = new(MhopPolicyVerdict.None, string.Empty, string.Empty);
}

/// <summary>
/// 落地在代码里的确定性内容规则（先规则、后大模型）。
/// 存在的意义：
/// 1. 自伤/自杀、色情、毒品等内容会被大模型服务商的安全策略拒答（返回 5xx），
///    只靠大模型会把「最该拦的内容」判成「AI 不可用」，这里先把确定性结论拿到手；
/// 2. 联系方式、引流链接等特征由正则判定比大模型稳定得多，也不会被模型一句话放行；
/// 3. 本地规则零成本零延迟，只有真正需要语义判断的文本才走大模型。
/// 规则命中即短路，绝不再让大模型「翻案」；未命中才交给大模型。
/// </summary>
public static class MhopContentPolicy
{
    /// <summary>硬违规词。命中即违规：转人工且不自动公开（消息场景同时隐藏）。</summary>
    private static readonly (string[] Words, string Reason)[] ViolationRules =
    [
        (["冰毒", "海洛因", "摇头丸", "麻古", "氯胺酮", "k粉", "K粉", "大麻", "毒品", "吸毒", "贩毒", "制毒", "售毒", "买毒", "笑气", "上头电子烟", "阿片"],
            "涉毒品交易或吸食"),
        (["色情", "裸聊", "裸体", "裸照", "约炮", "一夜情", "援交", "卖淫", "嫖娼", "招嫖", "迷奸", "强奸", "轮奸", "幼女", "未成年少女", "成人视频", "黄色网站", "激情视频", "上门服务", "包养", "外围女", "涩情", "福利视频", "看片加"],
            "色情或性交易内容"),
        (["赌博", "博彩", "六合彩", "赌球", "百家乐", "棋牌代理", "线上赌场", "投注平台", "彩票网", "洗码", "反水"],
            "赌博推广内容"),
        (["枪支", "手枪", "步枪", "弹药", "炸药", "雷管", "爆炸物", "军火", "管制刀具", "气枪", "仿真枪", "弓弩", "卖枪"],
            "涉枪爆等违禁品"),
        (["刷单", "代开发票", "代考", "办证", "假证", "贷款代办", "高利贷", "套现", "洗钱", "跑分", "杀猪盘", "投资返利", "日赚", "月入过万", "高薪兼职", "兼职刷", "加微信领", "扫码领", "客服退款", "刷流水", "包下款"],
            "疑似诈骗或引流推广"),
        (["自杀方法", "怎么自杀", "如何自杀", "自杀方式", "无痛自杀", "安眠药怎么", "安眠药吃多少", "烧炭自杀", "上吊方法", "割腕方法", "相约自杀", "一起自杀", "一起去自杀", "组团自杀", "约死", "一起死", "教我死", "怎么死"],
            "含自杀方法或相约自杀内容"),
    ];

    /// <summary>疑似违规词。命中转人工复核（不自动公开）。</summary>
    private static readonly (string[] Words, string Reason)[] SuspectRules =
    [
        (["加我微信", "加个微信", "加你微信", "私聊", "私加", "留个联系", "留联系方式", "换平台聊", "线下见面", "约见", "同城服务", "陪聊收费", "付费陪聊", "包月陪聊", "红包返", "荐股", "内部消息", "带你赚钱"],
            "疑似索要联系方式、引流或金钱往来"),
        (["傻逼", "垃圾东西", "贱人", "滚蛋", "人渣", "神经病", "蠢货", "婊子", "死全家", "去死吧"],
            "疑似辱骂或攻击性内容"),
        (["私家侦探", "查开房", "查户籍", "人肉", "开盒", "身份证号", "银行卡号", "验证码", "代练", "外挂", "出售账号", "账号交易"],
            "疑似侵害隐私或违规交易"),
    ];

    /// <summary>手机号（中国大陆）。</summary>
    private static readonly Regex PhoneRegex = new(@"(?<!\d)1[3-9]\d{9}(?!\d)", RegexOptions.Compiled);

    /// <summary>微信号 / QQ 号等账号形态。</summary>
    private static readonly Regex ContactRegex = new(
        @"(?:微信|vx|VX|Vx|v信|weixin|微信号|QQ|qq|扣扣)\s*(?:号|ID|id|号码)?\s*(?:是|为|[:：=])?\s*(?:[A-Za-z][A-Za-z0-9_-]{5,}|\d{5,})",
        RegexOptions.Compiled);

    /// <summary>外链。</summary>
    private static readonly Regex UrlRegex = new(
        @"(?:https?://|www\.)[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=%]+",
        RegexOptions.Compiled);

    /// <summary>
    /// 规则预筛。返回 <see cref="MhopPolicyVerdict.None"/> 表示无确定结论，交给大模型。
    /// 危机信号放在最后判断：一条文本若同时命中违规/疑似规则，按更严重的结论处理。
    /// </summary>
    public static MhopPolicyResult Screen(string? text)
    {
        var value = text ?? string.Empty;
        if (value.Length == 0) return MhopPolicyResult.None;

        foreach (var (words, reason) in ViolationRules)
        {
            foreach (var word in words)
            {
                if (value.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return new MhopPolicyResult(MhopPolicyVerdict.Violation, reason, word);
            }
        }

        foreach (var (words, reason) in SuspectRules)
        {
            foreach (var word in words)
            {
                if (value.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return new MhopPolicyResult(MhopPolicyVerdict.Suspect, reason, word);
            }
        }

        // 联系方式 / 外链：只认「像账号」的确定性形态，避免把普通聊天里的「微信」二字也拦下
        if (PhoneRegex.IsMatch(value)) return new MhopPolicyResult(MhopPolicyVerdict.Suspect, "疑似索要或留下手机号", "phone");
        if (ContactRegex.IsMatch(value)) return new MhopPolicyResult(MhopPolicyVerdict.Suspect, "疑似索要或留下社交账号", "contact");
        if (UrlRegex.IsMatch(value)) return new MhopPolicyResult(MhopPolicyVerdict.Suspect, "疑似外链引流", "url");

        if (MhopModeration.DetectCrisis(value))
            return new MhopPolicyResult(MhopPolicyVerdict.Crisis, "含自伤/自杀风险信号", "crisis");

        return MhopPolicyResult.None;
    }
}