namespace ClouderyApi.Services.Mhop;

public sealed record MhopScaleOption(string Label, int Value);

public sealed record MhopScaleBand(int Ceiling, string Label, string Code);

public sealed class MhopScale
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Intro { get; init; }

    public required int Max { get; init; }

    public required IReadOnlyList<MhopScaleOption> Options { get; init; }

    public required IReadOnlyList<string> Questions { get; init; }

    public required IReadOnlyList<MhopScaleBand> Bands { get; init; }

    /// <summary>危机题目下标（该题 &gt; 0 即危机信号）；无则为 null。</summary>
    public int? CrisisIndex { get; init; }

    /// <summary>投影为 Python 后端 /api/assessments/scales 的返回结构（bands 为数组的数组）。</summary>
    public object ToResponse() => new
    {
        key = Key,
        name = Name,
        intro = Intro,
        max = Max,
        options = Options.Select(o => new { label = o.Label, value = o.Value }),
        questions = Questions,
        bands = Bands.Select(b => new object[] { b.Ceiling, b.Label, b.Code }),
        crisis_index = CrisisIndex,
    };
}

/// <summary>标准心理量表：PHQ-9（抑郁）与 GAD-7（焦虑）。</summary>
public static class MhopScales
{
    public static readonly IReadOnlyList<MhopScaleOption> OptionSet =
    [
        new("完全不会", 0),
        new("有几天", 1),
        new("一半以上的天数", 2),
        new("几乎每天", 3),
    ];

    public static readonly IReadOnlyDictionary<string, MhopScale> All = new Dictionary<string, MhopScale>
    {
        ["phq9"] = new MhopScale
        {
            Key = "phq9",
            Name = "PHQ-9 抑郁症筛查量表",
            Intro = "根据过去两周的状况作答。PHQ-9 是国际通用的抑郁症状筛查工具，结果仅供自我了解，不构成诊断。",
            Max = 27,
            Options = OptionSet,
            Questions =
            [
                "做事时提不起劲或没有兴趣",
                "感到心情低落、沮丧或绝望",
                "入睡困难、睡不安稳或睡眠过多",
                "感觉疲倦或没有活力",
                "食欲不振或吃得太多",
                "觉得自己很糟——或觉得自己很失败，或让自己/家人失望",
                "对事物专注有困难，例如阅读或看电视时",
                "动作或说话速度缓慢到别人已察觉？或相反——比平常更加烦躁、坐立不安",
                "有不如死掉或用某种方式伤害自己的念头",
            ],
            Bands =
            [
                new(4, "无明显症状", "normal"),
                new(9, "轻度", "mild"),
                new(14, "中度", "moderate"),
                new(19, "中重度", "severe"),
                new(27, "重度", "danger"),
            ],
            CrisisIndex = 8,
        },
        ["gad7"] = new MhopScale
        {
            Key = "gad7",
            Name = "GAD-7 广泛性焦虑筛查量表",
            Intro = "根据过去两周的状况作答。GAD-7 是国际通用的焦虑症状筛查工具，结果仅供自我了解，不构成诊断。",
            Max = 21,
            Options = OptionSet,
            Questions =
            [
                "感到紧张、焦虑或急躁",
                "不能停止或控制担忧",
                "对各种各样的事情担忧过多",
                "很难放松下来",
                "由于不安而无法静坐",
                "变得容易烦恼或急躁",
                "感到似乎将有可怕的事情发生而害怕",
            ],
            Bands =
            [
                new(4, "无明显症状", "normal"),
                new(9, "轻度", "mild"),
                new(14, "中度", "moderate"),
                new(21, "重度", "danger"),
            ],
            CrisisIndex = null,
        },
    };

    public static (string Label, string Code) ScoreBand(string scaleKey, int score)
    {
        var bands = All[scaleKey].Bands;
        foreach (var band in bands)
        {
            if (score <= band.Ceiling) return (band.Label, band.Code);
        }
        var last = bands[^1];
        return (last.Label, last.Code);
    }
}
