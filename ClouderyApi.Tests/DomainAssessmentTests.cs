using ClouderyApi.Shared.Exceptions;
using ClouderyApi.Modules.Mhop.Domain;

namespace ClouderyApi.Tests;

/// <summary>
/// 心理评估计分领域服务（AssessmentScoring）的纯单测：不依赖 HTTP、不依赖数据库。
/// 钉住题库校验顺序与文案、量表分档边界、危机题命中并入危机信号，以及自由文本评估的分支。
/// </summary>
public sealed class DomainAssessmentTests
{
    private static string Reason(Action action) => Assert.Throws<DomainRuleException>(action).Message;

    /// <summary>按题目数量铺满分值，并把 score 的余数分配到最后一题。</summary>
    private static Dictionary<string, int> Answers(int score, int count)
    {
        var values = new Dictionary<string, int>();
        for (var index = 0; index < count; index++)
        {
            var take = Math.Min(3, score);
            values[index.ToString()] = take;
            score -= take;
        }

        return values;
    }

    private static Dictionary<string, int> All(int value, int count)
    {
        var values = new Dictionary<string, int>();
        for (var index = 0; index < count; index++) values[index.ToString()] = value;
        return values;
    }

    [Fact]
    public void Phq9_all_zero_scores_normal_without_crisis()
    {
        var scored = AssessmentScoring.Evaluate("phq9", All(0, 9), "", false);
        Assert.Equal(0, scored.Score);
        Assert.Equal("无明显症状", scored.Level);
        Assert.Equal("normal", scored.LevelCode);
        Assert.False(scored.Crisis);
    }

    [Fact]
    public void Phq9_full_marks_scores_danger()
    {
        var scored = AssessmentScoring.Evaluate("phq9", All(3, 9), "", false);
        Assert.Equal(27, scored.Score);
        Assert.Equal("重度", scored.Level);
        Assert.Equal("danger", scored.LevelCode);
    }

    [Fact]
    public void Gad7_full_marks_scores_danger()
    {
        var scored = AssessmentScoring.Evaluate("gad7", All(3, 7), "", false);
        Assert.Equal(21, scored.Score);
        Assert.Equal("重度", scored.Level);
        Assert.Equal("danger", scored.LevelCode);
    }

    [Theory]
    [InlineData(0, "normal", "无明显症状")]
    [InlineData(4, "normal", "无明显症状")]
    [InlineData(5, "mild", "轻度")]
    [InlineData(9, "mild", "轻度")]
    [InlineData(10, "moderate", "中度")]
    [InlineData(14, "moderate", "中度")]
    [InlineData(15, "severe", "中重度")]
    [InlineData(19, "severe", "中重度")]
    [InlineData(20, "danger", "重度")]
    [InlineData(27, "danger", "重度")]
    public void Phq9_bands_follow_the_ceiling_table(int score, string code, string label)
    {
        var scored = AssessmentScoring.Evaluate("phq9", Answers(score, 9), "", false);
        Assert.Equal(score, scored.Score);
        Assert.Equal(code, scored.LevelCode);
        Assert.Equal(label, scored.Level);
    }

    [Fact]
    public void Missing_answer_is_rejected_before_scoring()
    {
        var answers = All(0, 9);
        answers.Remove("8");
        Assert.Equal("请完成量表全部题目", Reason(() => AssessmentScoring.Evaluate("phq9", answers, "", false)));
    }

    [Fact]
    public void Null_answers_are_rejected_as_incomplete()
    {
        Assert.Equal("请完成量表全部题目", Reason(() => AssessmentScoring.Evaluate("phq9", null, "", false)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Out_of_range_answer_is_rejected(int value)
    {
        var answers = All(0, 9);
        answers["3"] = value;
        Assert.Equal("量表作答值非法", Reason(() => AssessmentScoring.Evaluate("phq9", answers, "", false)));
    }

    [Fact]
    public void Extra_answer_keys_are_ignored()
    {
        var answers = All(0, 9);
        answers["9"] = 3;
        answers["q"] = 3;
        var scored = AssessmentScoring.Evaluate("phq9", answers, "", false);
        Assert.Equal(0, scored.Score);
    }

    [Fact]
    public void Crisis_question_hit_raises_the_crisis_flag()
    {
        var answers = All(0, 9);
        answers["8"] = 1;
        var scored = AssessmentScoring.Evaluate("phq9", answers, "", false);
        Assert.Equal(1, scored.Score);
        Assert.Equal("normal", scored.LevelCode);
        Assert.True(scored.Crisis);
    }

    [Fact]
    public void Crisis_flag_passes_through_when_no_crisis_question_exists()
    {
        var scored = AssessmentScoring.Evaluate("gad7", All(0, 7), "", true);
        Assert.True(scored.Crisis);
        Assert.True(AssessmentScoring.Evaluate("gad7", All(0, 7), "", false).Crisis is false);
    }

    [Fact]
    public void Free_type_without_text_is_rejected()
    {
        Assert.Equal("请先描述你最近的状态与感受",
            Reason(() => AssessmentScoring.Evaluate(AssessmentScoring.FreeType, null, "", true)));
    }

    [Fact]
    public void Free_type_with_text_scores_nothing_but_keeps_crisis()
    {
        var scored = AssessmentScoring.Evaluate(AssessmentScoring.FreeType, null, "最近总是失眠", true);
        Assert.Null(scored.Score);
        Assert.Equal(string.Empty, scored.Level);
        Assert.Equal(string.Empty, scored.LevelCode);
        Assert.True(scored.Crisis);
    }

    [Fact]
    public void Free_text_is_expected_to_arrive_already_trimmed()
    {
        var scored = AssessmentScoring.Evaluate(AssessmentScoring.FreeType, null, " ", false);
        Assert.Null(scored.Score);
        Assert.Equal("请先描述你最近的状态与感受",
            Reason(() => AssessmentScoring.Evaluate(AssessmentScoring.FreeType, null, "", false)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    public void Unknown_type_is_rejected(string assessmentType)
    {
        Assert.Equal("未知的评估类型",
            Reason(() => AssessmentScoring.Evaluate(assessmentType, null, "有文本", false)));
    }

    [Fact]
    public void Free_type_constant_matches_the_client_contract()
    {
        Assert.Equal("free", AssessmentScoring.FreeType);
        Assert.Equal(0, AssessmentScoring.MinAnswer);
        Assert.Equal(3, AssessmentScoring.MaxAnswer);
    }

    [Fact]
    public void Scale_catalogue_exposes_phq9_and_gad7()
    {
        Assert.Equal("phq9", MhopScales.All["phq9"].Key);
        Assert.Equal(9, MhopScales.All["phq9"].Questions.Count);
        Assert.Equal(8, MhopScales.All["phq9"].CrisisIndex);
        Assert.Equal("gad7", MhopScales.All["gad7"].Key);
        Assert.Equal(7, MhopScales.All["gad7"].Questions.Count);
        Assert.Null(MhopScales.All["gad7"].CrisisIndex);
    }
}
