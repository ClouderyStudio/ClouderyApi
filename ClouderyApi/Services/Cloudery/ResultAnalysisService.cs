using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClouderyApi.Models.Cloudery.DTOs;
using ClouderyApi.Shared.Ai;

namespace ClouderyApi.Services.Cloudery;

/// <summary>
/// 测评结果的 AI 解读：把站点提交的量表结果整理成四段式提示词交给 <see cref="ILlmClient"/>，
/// 模型未配置或调用失败时返回内置的本地兜底文案，接口永远有内容可返回。
/// 与 MHOP 的 AssessAsync 一致：判定为危机场景时，正文开头强制补援助热线（CrisisSupport.Prefix）。
///
/// 提示词的实测要点（改动前请复测）：
/// 1) 显式要求「只依据给出的数据、不猜测未提及的经历、不提药物」后，解读不再出现诊断式判断；
/// 2) 显式要求「按 1)2)3)4) 分四段、纯文本、每段不超过 3 句话、总量 500 字内」，
///    输出才能被前端的 splitAnalysisSections 稳定切成四块；
/// 3) 量表的类型（计分／特质／类型）必须区分：MBTI、气质类型、心理年龄、七美德与七宗罪这类
///    量表的总分不是程度高低，按严重度解读会得出「得分为 51/100」这种没有意义的结论；
/// 4) 只把总分送过去时模型只能写「整体偏高、注意休息」这类放到任何量表都成立的话，
///    因此维度名称与画像数据（类型、指数、双轴、各维度年龄、效度）必须一并给出。
/// </summary>
public sealed class ResultAnalysisService
{
    private const int MaxNoteLength = 1200;
    private const int MaxDimensions = 16;
    private const int MaxProfile = 12;
    private const int MaxAnalysisLength = 2400;

    /// <summary>量表结果的类型，决定第一、二段怎么写。</summary>
    private enum ScoreKind
    {
        /// <summary>计分型：分数越高代表相关困扰越多。</summary>
        Severity,

        /// <summary>特质型：分数只表示相对倾向的强弱，无好坏。</summary>
        Trait,

        /// <summary>类型型：结果是类型或画像，分数没有高低含义。</summary>
        Type,
    }

    private static readonly string[] HighSeverityKeywords =
        ["重度", "严重", "高风险", "筛查阳性", "阳性", "很明显", "偏高"];

    /// <summary>结果是类型／画像的量表：总分不能当程度解读。</summary>
    private static readonly HashSet<string> TypeScaleIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "mbti", "temperament", "seven", "psy-age", "sixteenpf", "epq", "epq-rsc",
    };

    /// <summary>特质／倾向类量表：分数有意义，但不能病理化。</summary>
    private static readonly HashSet<string> TraitScaleIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "ipip-eis", "emotional-stability", "sccs", "bpns", "rses", "pss", "bis", "bpaq",
    };

    private static readonly string[] PersonalityCategoryKeywords =
        ["personality", "temperament", "trait", "人格", "性格", "气质"];

    private readonly ILlmClient _llm;
    private readonly ILogger<ResultAnalysisService> _logger;

    public ResultAnalysisService(ILlmClient llm, ILogger<ResultAnalysisService> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    /// <summary>生成解读；任何异常都不会冒泡给调用方（客户端取消除外）。</summary>
    public async Task<ResultAnalysisOut> AnalyzeAsync(
        ResultAnalysisIn input, CancellationToken cancellationToken = default)
    {
        var crisis = DetectCrisis(input);
        var kind = ResolveKind(input);
        var fallback = BuildLocalAnalysis(input, kind, crisis);

        try
        {
            if (!_llm.IsConfigured)
            {
                return Respond(fallback, "local", crisis);
            }

            var (text, engine) = await _llm.ChatAsync(
                BuildMessages(input, kind, crisis), cancellationToken, temperature: 0.6, maxTokens: 1200);

            if (engine != "llm" || string.IsNullOrWhiteSpace(text))
            {
                return Respond(fallback, "local", crisis);
            }

            var analysis = CrisisSupport.EnsureHotline(Normalize(text), crisis);
            return Respond(analysis, "llm", crisis);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 客户端已断开，不必再拼兜底文案
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "结果 AI 解读失败，返回本地兜底文本");
            return Respond(fallback, "local", crisis);
        }
    }

    /// <summary>站点风险标记、等级、分数口径与（用户同意带来的）备注中任一命中危机词即视为危机。</summary>
    private static bool DetectCrisis(ResultAnalysisIn input)
        => input.Risk
           || CrisisSupport.DetectCrisis(input.Level)
           || CrisisSupport.DetectCrisis(input.ScoreNote)
           || CrisisSupport.DetectCrisis(input.Note);

    /// <summary>优先采用站点给出的类型；缺失时按 testId 与 category 推断。</summary>
    private static ScoreKind ResolveKind(ResultAnalysisIn input)
    {
        switch (input.ScoreKind?.Trim().ToLowerInvariant())
        {
            case "type":
                return ScoreKind.Type;
            case "trait":
                return ScoreKind.Trait;
            case "severity":
                return ScoreKind.Severity;
        }

        if (TypeScaleIds.Contains(input.TestId)) return ScoreKind.Type;
        if (TraitScaleIds.Contains(input.TestId)) return ScoreKind.Trait;
        if (!string.IsNullOrWhiteSpace(input.Category)
            && PersonalityCategoryKeywords.Any(k => input.Category.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return ScoreKind.Trait;
        }

        return ScoreKind.Severity;
    }

    private static List<Dictionary<string, string>> BuildMessages(ResultAnalysisIn input, ScoreKind kind, bool crisis)
        =>
        [
            new() { ["role"] = "system", ["content"] = BuildSystemPrompt(input, kind, crisis) },
            new() { ["role"] = "user", ["content"] = BuildUserPrompt(input, kind) },
        ];

    /// <summary>按量表类型给出的写法要求：类型／特质／计分三种，决定第一段与第二段的重心。</summary>
    private static string KindInstruction(ScoreKind kind)
        => kind switch
        {
            ScoreKind.Type =>
                "【本次量表类型】类型／画像类量表：结果是类型或画像，总分只用于生成类型，没有高低好坏的含义。\n"
                + "【本类型写法要求】\n"
                + "- 第一段直接给出结果是什么类型或什么画像（类型码与类型名、原型、指数档位等），"
                + "禁止出现「得分为 X/Y 分」「等级为」这类表述，也不要把总分读成程度高低；\n"
                + "- 第二段围绕这个类型的内部结构展开（各偏好方向、功能位置与强度、各维度年龄、各维度百分比等），"
                + "说明这套组合为什么彼此呼应、在生活中可能表现成什么样；\n"
                + "- 第三段让用户把这一类型的优势用出来，同时照顾到相对薄弱或较少使用的那一侧。\n",
            ScoreKind.Trait =>
                "【本次量表类型】特质／倾向类量表：分数描述的是相对倾向的强弱，不代表好坏，也不代表病态。\n"
                + "【本类型写法要求】\n"
                + "- 可以引用分数与等级，但不要使用「正常／异常」「严重／轻度」「需要治疗」这类病理化措辞；\n"
                + "- 第二段重点讲这一倾向的两面性：在什么情境下它是资源、在什么情境下可能带来代价；\n"
                + "- 第三段落在可以练习的具体行为上，并说明练的是哪一项倾向。\n",
            _ =>
                "【本次量表类型】计分型量表：分数越高代表相关困扰越多，等级是程度分档。\n"
                + "【本类型写法要求】\n"
                + "- 第一段用总分与等级说明当前程度；\n"
                + "- 第二段不要把总分和等级再说一遍，而要挑出得分相对突出的 2-3 个维度，报出名称与分值，"
                + "说明这通常意味着什么、平时可能有哪些表现。\n",
        };

    private static string BuildSystemPrompt(ResultAnalysisIn input, ScoreKind kind, bool crisis)
    {
        var sb = new StringBuilder();
        sb.Append("""
            你是心理学量表解读助手，为刚完成一份在线心理测评的普通用户解读这份结果。你要像一位经验丰富的解读员那样，围绕这份量表自己的数据做具体剖析，让用户能拿着自己的报告对号入座，而不是给一段放在任何量表上都成立的通用评语。
            """);
        sb.Append(KindInstruction(kind));
        sb.Append("""
            硬性要求：
            1. 只依据下面给出的分数、等级、维度与画像数据作答，不猜测用户没有提到的经历，不贴疾病标签，不做医学诊断，不提药物名称或剂量；
            2. 按 1)2)3)4) 分四段输出，纯文本，不要 markdown 标题、表格、项目符号或星号加粗，全部使用中文；
            3. 每段不超过 3 句话，全文严格控制在 600 字以内（400-550 字最佳）；
            4. 必须点出输入中出现的具体名称与数值（维度名、类型码、字母、指数、百分比、岁数等），至少引用三个具体数据点；不要用「整体偏高」「普遍偏低」这类没有指向的说法；
            5. 禁止空话：不要出现「保持良好心态」「注意休息」「多与人沟通」这类不指向任何具体维度的句子；每条建议都要写清针对的是哪一个维度或哪一个数据；
            6. 不要把【量表自带解读】整句照抄，只能引用其中的维度名与等级名；
            7. 语气温暖、平实、尊重，不恐吓、不承诺疗效、不评价用户的人格好坏。
            8. 每一段都要落到输入数据里的具体名称或数值上，不要用「该量表」「此类人群」这类说法代替对这份结果的分析，也不要编造输入里没有的数字。
            四段内容：
            1) 结果解读：说明这份结果的核心结论是什么（分数、等级、类型或画像），并明确量表是自我了解的参考工具、不能替代临床诊断；
            2) 特征分析：这是最重要的一段。必须挑 2-3 个具体维度或画像项逐个剖析：先点出它的名称与数值，再说明这个数值、这一极或这一项在本量表里意味着什么，在日常里可能表现为哪些具体的样子（可以用「可能」「倾向于」）；
            3) 三条建议：只给三条，每条一句话，必须与第二段点出的具体维度或数值一一对应（写成「针对……这一项，可以……」），具体到当天就能做；
            4) 求助指引：
            """);

        if (crisis)
        {
            sb.Append("结果已提示较高的自伤风险，第四段必须直接建议尽快到综合医院心理科/精神科或当地心理援助中心做当面评估，"
                      + "给出全国心理援助热线 12356 与北京心理危机研究与干预中心 010-82951332，紧急情况拨打 110/120，"
                      + "并建议告知一位信任的人、尽量不要独处。第一段不要淡化风险，也不要说「没什么问题」。");
        }
        else if (IsHighSeverity(input))
        {
            sb.Append("结果程度偏高，第四段建议在两周内预约学校或社区的心理咨询、或医院心理科做进一步评估，"
                      + "可以点名第二段里偏高的那一项作为预约时想谈的内容。");
        }
        else
        {
            sb.Append("第四段只需说明：如果这种状态持续两周以上、或开始影响睡眠、饮食、学习与工作，再及时寻求专业帮助即可。");
        }

        if (!string.IsNullOrWhiteSpace(input.Suggestion))
        {
            sb.Append("【量表自带解读】如下，只作为你了解这份量表的参照，请用自己的话重写，不要照抄：")
              .Append(Truncate(input.Suggestion, 300));
        }

        return sb.ToString();
    }

    private static string BuildUserPrompt(ResultAnalysisIn input, ScoreKind kind)
    {
        var lines = new List<string>
        {
            $"【量表】{Title(input)}（{input.TestId}）"
                + (string.IsNullOrWhiteSpace(input.Category) ? "" : $"（类别：{input.Category}）"),
            $"【量表类型】{KindLabel(kind)}",
        };

        if (kind == ScoreKind.Type)
        {
            // 类型型量表的总分不能当程度，因此不提供【得分】行，只给类型与画像
            if (!string.IsNullOrWhiteSpace(input.Level)) lines.Add($"【结果类型】{input.Level}");
        }
        else
        {
            lines.Add($"【得分】{FormatScore(input.TotalScore)} / {FormatScore(input.MaxScore)} 分"
                      + (input.MinScore.HasValue ? $"（最低可能得分 {FormatScore(input.MinScore)}）" : ""));
            if (!string.IsNullOrWhiteSpace(input.Level)) lines.Add($"【等级】{input.Level}");
        }

        AppendProfile(lines, input.Profile);

        if (!string.IsNullOrWhiteSpace(input.TimeFrame)) lines.Add($"【作答时间范围】{input.TimeFrame}");
        if (!string.IsNullOrWhiteSpace(input.Respondent)) lines.Add($"【作答人】{input.Respondent}");
        if (!string.IsNullOrWhiteSpace(input.ScoreNote)) lines.Add($"【分数口径】{Truncate(input.ScoreNote, 200)}");
        lines.Add($"【风险标记】{(input.Risk ? "有（自伤／自杀相关筛查为阳性）" : "无")}");
        lines.Add($"【维度分数】{FormatDimensions(input.Dimensions)}");

        if (!string.IsNullOrWhiteSpace(input.Note))
        {
            lines.Add("【用户备注】用户已同意把下面这条私人备注一并提供给你，只用于理解他的处境，"
                      + "不要逐字复述，也不要据此推断他没写的内容：");
            lines.Add(Truncate(input.Note, MaxNoteLength));
        }

        return string.Join('\n', lines);
    }

    /// <summary>量表分类的中文说明；模型据此决定第一、二段的写法。</summary>
    private static string KindLabel(ScoreKind kind) => kind switch
    {
        ScoreKind.Type => "类型型（结果是类型或画像，总分只用于生成类型，没有高低含义，禁止按程度解读）",
        ScoreKind.Trait => "特质型（分数只表示相对倾向的强弱，没有好坏、没有病态之分）",
        _ => "计分型（分数越高代表相关困扰越多，等级是程度分档）",
    };

    private static void AppendProfile(List<string> lines, List<ResultAnalysisProfileItem>? profile)
    {
        var items = (profile ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Value))
            .Take(MaxProfile)
            .ToList();
        if (items.Count == 0) return;

        lines.Add("【画像数据】这份量表结果里的结构化信息（解读的主要依据）：");
        lines.AddRange(items.Select(p => $"- {p.Name!.Trim()}：{Truncate(p.Value!.Trim(), 220)}"));
    }

    /// <summary>本地兜底：不调用模型也能给出结构一致、且与量表类型匹配的四段式解读（engine=local）。</summary>
    private static string BuildLocalAnalysis(ResultAnalysisIn input, ScoreKind kind, bool crisis)
    {
        var parts = new List<string>();
        var profile = (input.Profile ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Value))
            .Take(4)
            .ToList();
        var topDimensions = (input.Dimensions ?? [])
            .Where(d => d.Score.HasValue && !string.IsNullOrWhiteSpace(d.Name))
            .OrderByDescending(d => d.Score!.Value)
            .Take(3)
            .Select(d => $"{d.Name}（{FormatScore(d.Score)}）")
            .ToList();

        if (kind == ScoreKind.Type)
        {
            var headline = profile.FirstOrDefault();
            parts.Add($"1) 结果解读：{Title(input)}给出的结果是"
                      + (string.IsNullOrWhiteSpace(input.Level) ? "一份类型画像" : $"「{input.Level}」")
                      + (headline is null ? "" : $"，{headline.Name}为 {Truncate(headline.Value!.Trim(), 120)}")
                      + "。这类量表描述的是倾向与画像，没有高低优劣之分，也不能替代临床诊断。");

            var evidence = profile.Skip(1).Select(p => $"{p.Name}（{Truncate(p.Value!.Trim(), 60)}）").ToList();
            if (evidence.Count == 0 && topDimensions.Count > 0) evidence.AddRange(topDimensions);
            parts.Add(evidence.Count > 0
                ? $"2) 特征分析：本次结果里最具体的几项是{string.Join("、", evidence)}，可以留意这几项在最近一两周里是怎么出现的。"
                : "2) 特征分析：单次作答容易受近期状态影响，建议结合最近一两周的情绪、睡眠与人际情况一起理解这份结果。");

            parts.Add("3) 三条建议：挑出上面最像你的一项，回忆最近一周它出现的两个具体场景并写下来；"
                      + "针对相对少用的那一面，这周安排一件小事去练，例如需要独处就先给自己留出固定的一段安静时间；"
                      + "把这份结果放到一个月后再看一次，对比那时的感受。");
        }
        else
        {
            var score = $"1) 结果解读：{Title(input)}的得分为 {FormatScore(input.TotalScore)}"
                        + (input.MaxScore.HasValue ? $"/{FormatScore(input.MaxScore)}" : "")
                        + (string.IsNullOrWhiteSpace(input.Level) ? "" : $"，等级为「{input.Level}」")
                        + (kind == ScoreKind.Trait
                            ? "。这类量表描述的是相对倾向，分数高低不代表好坏。"
                            : "。量表只是了解自己的一个参考，不能替代临床诊断。");
            parts.Add(score);

            var evidence = topDimensions.Count > 0 ? new List<string>(topDimensions) : [];
            if (evidence.Count == 0 && profile.Count > 0)
            {
                evidence.AddRange(profile.Select(p => $"{p.Name}（{Truncate(p.Value!.Trim(), 60)}）"));
            }
            parts.Add(evidence.Count > 0
                ? $"2) 特征分析：本次作答中相对突出的是{string.Join("、", evidence)}，可以留意这几项在最近一两周里的变化。"
                : "2) 特征分析：单次得分容易受近期状态影响，建议结合最近一两周的情绪、睡眠与人际情况一起理解这份结果。");

            var focus = topDimensions.Count > 0 ? topDimensions[0] : null;
            parts.Add(focus is null
                ? "3) 三条建议：把作息固定下来，尽量在同一时间睡觉、起床；每天安排 20-30 分钟快走或其他能微微出汗的活动；把此刻的感受用几句话写在纸上，感到吃力时找一个信任的人说一说。"
                : $"3) 三条建议：针对「{focus}」这一项，这周每天用一句话记下它出现的场景；针对相对最轻的那一项，给自己安排一件小而具体的事去做；"
                  + "把这三条各写一个当天能完成的小动作，睡前对照打勾。");
        }

        parts.Add(crisis
            ? "4) 求助指引：请尽快到综合医院心理科／精神科或当地心理援助中心做当面评估，也可以立即拨打全国心理援助热线 12356（24 小时、免费、保密）、"
              + "北京心理危机研究与干预中心 010-82951332；紧急情况请拨打 110 或 120，并告知一位你信任的人、尽量不要独处。"
            : kind == ScoreKind.Type
                ? "4) 求助指引：如果在某些情境里这个类型的特点让你持续难受、或影响到学习与人际关系，可以找学校／社区的心理咨询聊一聊。"
                : IsHighSeverity(input)
                    ? "4) 求助指引：建议在两周内预约学校／社区心理咨询或医院心理科做进一步评估，专业支持能帮你更快恢复。"
                    : "4) 求助指引：如果两周后状态没有改善，或开始影响睡眠、饮食、学习与工作，请及时寻求专业心理咨询。");

        return CrisisSupport.EnsureHotline(string.Join("\n\n", parts), crisis);
    }

    private static ResultAnalysisOut Respond(string analysis, string engine, bool crisis) => new()
    {
        Analysis = analysis,
        Engine = engine,
        Crisis = crisis,
        GeneratedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>模型偶尔会带上 markdown 标记或多余空行，这里做轻量清理并限制长度。</summary>
    private static string Normalize(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        normalized = Regex.Replace(normalized, @"^[ \t]*#{1,6}[ \t]*", "", RegexOptions.Multiline);
        normalized = normalized.Replace("**", "").Replace("__", "");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Length > MaxAnalysisLength
            ? normalized[..MaxAnalysisLength].TrimEnd() + "…"
            : normalized;
    }

    private static string FormatDimensions(List<ResultAnalysisDimension>? dimensions)
    {
        var items = (dimensions ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .Take(MaxDimensions)
            .Select(d =>
            {
                var max = d.Max.HasValue ? $"/{FormatScore(d.Max)}" : "";
                var level = string.IsNullOrWhiteSpace(d.Level) ? "" : $"（{d.Level}）";
                var desc = string.IsNullOrWhiteSpace(d.Desc) ? "" : $"，{Truncate(d.Desc!.Trim(), 60)}";
                return $"{d.Name}：{FormatScore(d.Score)}{max}{level}{desc}";
            })
            .ToList();

        return items.Count == 0 ? "无（本次结果未包含维度分数）" : string.Join("；", items);
    }

    private static bool IsHighSeverity(ResultAnalysisIn input)
    {
        if (input.Severity is >= 0.6) return true;
        return !string.IsNullOrWhiteSpace(input.Level)
               && HighSeverityKeywords.Any(input.Level.Contains);
    }

    private static string Title(ResultAnalysisIn input)
        => string.IsNullOrWhiteSpace(input.TestTitle) ? "本次测评" : $"「{input.TestTitle.Trim()}」";

    private static string FormatScore(double? value)
    {
        if (value is null) return "未知";
        var number = value.Value;
        return Math.Abs(number - Math.Round(number)) < 0.005
            ? Math.Round(number).ToString("0", CultureInfo.InvariantCulture)
            : number.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "…";
}
