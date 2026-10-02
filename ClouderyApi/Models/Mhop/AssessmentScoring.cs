namespace ClouderyApi.Models.Mhop;

/// <summary>一次心理评估的计分结论：量表总分与分档（自由文本评估无分值）。</summary>
public sealed record AssessmentScore(int? Score, string Level, string LevelCode, bool Crisis);

/// <summary>
/// 心理评估计分领域服务：校验量表作答题库、累计总分、映射分档，并把量表危机题命中并入危机信号。
/// 不负责自由文本筛查（由调用方先做关键词判定后以 crisis 传入），也不负责 AI 解读与持久化。
/// </summary>
public static class AssessmentScoring
{
    /// <summary>自由文本评估的类型标识（不参与量表分档）。</summary>
    public const string FreeType = "free";

    /// <summary>量表作答允许的取值区间（0=完全不会 … 3=几乎每天）。</summary>
    public const int MinAnswer = 0;

    public const int MaxAnswer = 3;

    public static AssessmentScore Evaluate(
        string assessmentType,
        IReadOnlyDictionary<string, int>? answers,
        string freeText,
        bool crisis)
    {
        int? score = null;
        var level = string.Empty;
        var levelCode = string.Empty;

        if (MhopScales.All.TryGetValue(assessmentType, out var scale))
        {
            var values = ReadAnswers(scale, answers);
            var total = 0;
            foreach (var value in values) total += value;
            score = total;
            (level, levelCode) = MhopScales.ScoreBand(assessmentType, total);
            if (scale.CrisisIndex is int crisisIndex && values[crisisIndex] > 0) crisis = true;
        }
        else if (assessmentType != FreeType)
        {
            throw new DomainRuleException("未知的评估类型");
        }
        else if (freeText.Length == 0)
        {
            throw new DomainRuleException("请先描述你最近的状态与感受");
        }

        return new AssessmentScore(score, level, levelCode, crisis);
    }

    private static List<int> ReadAnswers(MhopScale scale, IReadOnlyDictionary<string, int>? answers)
    {
        answers ??= new Dictionary<string, int>();
        var values = new List<int>(scale.Questions.Count);
        for (var index = 0; index < scale.Questions.Count; index++)
        {
            if (!answers.TryGetValue(index.ToString(), out var value))
                throw new DomainRuleException("请完成量表全部题目");
            values.Add(value);
        }

        if (values.Any(value => value is < MinAnswer or > MaxAnswer))
            throw new DomainRuleException("量表作答值非法");

        return values;
    }
}
